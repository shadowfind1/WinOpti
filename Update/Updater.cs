using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WinOpti.Update
{
    /// <summary>Обновление через GitHub Releases: найти версию новее, скачать exe, проверить SHA-256, подменить себя и перезапуститься.</summary>
    public static class Updater
    {
        const string Repo = "shadowfind1/WinOpti";

        public sealed class Release
        {
            public string Tag, Name, Notes, Url, AssetUrl, Sha256;
            public long Size;
        }

        /// <summary>Версия этой сборки, например «2.0.0-beta.2» (из &lt;Version&gt; в csproj).</summary>
        public static string Current
        {
            get
            {
#if DEBUG
                // для проверки: --fake-version=1.9.0 делает вид, что стоит старая версия
                var fake = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--fake-version="));
                if (fake != null) return fake.Substring("--fake-version=".Length);
#endif
                var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
                int plus = info.IndexOf('+'); // SDK дописывает хеш коммита
                return plus > 0 ? info.Substring(0, plus) : info;
            }
        }

        /// <summary>Самый новый релиз WinOpti 2+, если он новее текущей версии; иначе null. Сетевые ошибки — тоже null: обновление не должно мешать работе.</summary>
        public static async Task<Release> CheckAsync()
        {
            try
            {
#if DEBUG
                // проверка подмены без GitHub: --update-from=<путь к exe> выдаёт его за новую версию 9.9.9
                var local = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--update-from="));
                if (local != null)
                {
                    var path = local.Substring("--update-from=".Length);
                    return new Release { Tag = "v9.9.9", Name = "Test 9.9.9", Notes = "Local test", Url = "https://github.com/" + Repo,
                                         AssetUrl = new Uri(path).AbsoluteUri, Size = new FileInfo(path).Length, Sha256 = Sha256(path) };
                }
#endif
                string json;
                using (var web = Client()) json = await web.DownloadStringTaskAsync($"https://api.github.com/repos/{Repo}/releases?per_page=15");
                var list = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<List<Dictionary<string, object>>>(json);
                Release best = null;
                foreach (var r in list)
                {
                    if (r.TryGetValue("draft", out var d) && d is bool draft && draft) continue;
                    var tag = r["tag_name"] as string;
                    if (tag == null || Compare(tag, "2.0.0-a") < 0) continue; // ветка v1 на Python сюда не относится
                    var asset = (r["assets"] as System.Collections.ArrayList)?.Cast<Dictionary<string, object>>()
                        .FirstOrDefault(a => ((string)a["name"]).EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                    if (asset == null) continue;
                    if (best != null && Compare(tag, best.Tag) <= 0) continue;
                    var digest = asset.TryGetValue("digest", out var dg) ? dg as string : null;
                    best = new Release
                    {
                        Tag = tag, Name = r["name"] as string ?? tag, Notes = r["body"] as string ?? "", Url = r["html_url"] as string,
                        AssetUrl = (string)asset["browser_download_url"], Size = Convert.ToInt64(asset["size"]),
                        Sha256 = digest != null && digest.StartsWith("sha256:") ? digest.Substring(7) : null,
                    };
                }
                return best != null && Compare(best.Tag, Current) > 0 ? best : null;
            }
            catch (Exception ex) { App.Log(ex); return null; }
        }

        /// <summary>Скачать в %LOCALAPPDATA%\WinOpti\update и проверить. Возвращает путь к новому exe.</summary>
        public static async Task<string> DownloadAsync(Release r, IProgress<double> progress)
        {
            var dir = Path.Combine(App.DataDir, "update");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"WinOpti-{r.Tag.TrimStart('v')}.exe");
            using (var web = Client())
            {
                web.DownloadProgressChanged += (s, e) => progress?.Report(r.Size > 0 ? Math.Min(1, (double)e.BytesReceived / r.Size) : e.ProgressPercentage / 100.0);
                await web.DownloadFileTaskAsync(r.AssetUrl, file);
            }
            var len = new FileInfo(file).Length;
            if (r.Size > 0 && len != r.Size) throw new InvalidDataException(L.T("Файл скачался не полностью", "The download is incomplete"));
            if (r.Sha256 != null && !r.Sha256.Equals(Sha256(file), StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                throw new InvalidDataException(L.T("Контрольная сумма не совпала — файл повреждён или подменён", "Checksum mismatch — the file is damaged or tampered with"));
            }
            return file;
        }

        /// <summary>Работающий exe нельзя перезаписать, но можно переименовать: старый → WinOpti.old.exe, новый на его место, запуск нового.</summary>
        public static void InstallAndRestart(string newExe)
        {
            var self = Process.GetCurrentProcess().MainModule.FileName;
            var old = Path.Combine(Path.GetDirectoryName(self), Path.GetFileNameWithoutExtension(self) + ".old.exe");
            if (File.Exists(old)) File.Delete(old);
            File.Move(self, old);
            try { File.Copy(newExe, self); }
            catch { File.Move(old, self); throw; } // вернуть как было, чтобы программа не пропала
            Process.Start(new ProcessStartInfo(self, "--updated") { UseShellExecute = true });
            System.Windows.Application.Current.Shutdown();
        }

        /// <summary>После обновления: удалить старый exe и скачанный файл, обновить копию для авто-игрового режима.</summary>
        public static void AfterUpdate()
        {
            try
            {
                var self = Process.GetCurrentProcess().MainModule.FileName;
                var old = Path.Combine(Path.GetDirectoryName(self), Path.GetFileNameWithoutExtension(self) + ".old.exe");
                for (int i = 0; i < 20 && File.Exists(old); i++)
                {
                    try { File.Delete(old); } catch (IOException) { System.Threading.Thread.Sleep(250); } // старая копия ещё закрывается
                }
                var dir = Path.Combine(App.DataDir, "update");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
#if !DEBUG
                Game.GameMode.RefreshCopy(); // отладочная сборка не должна подменять настоящий игровой режим
#endif
            }
            catch (Exception ex) { App.Log(ex); }
        }

        static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var w = new WebClient();
            w.Headers[HttpRequestHeader.UserAgent] = "WinOpti/" + Current; // без User-Agent GitHub отвечает 403
            w.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
            return w;
        }

        static string Sha256(string file)
        {
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(file))
                return string.Concat(sha.ComputeHash(f).Select(b => b.ToString("x2")));
        }

        /// <summary>Сравнение версий по semver: 2.0.0-beta &lt; 2.0.0-beta.2 &lt; 2.0.0 &lt; 2.0.1. Буква v в начале не важна.</summary>
        public static int Compare(string a, string b)
        {
            (int[] nums, string[] pre) Parse(string v)
            {
                v = v.Trim().TrimStart('v', 'V');
                int dash = v.IndexOf('-');
                var core = dash < 0 ? v : v.Substring(0, dash);
                var nums = core.Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).Concat(new[] { 0, 0, 0 }).Take(3).ToArray();
                return (nums, dash < 0 ? new string[0] : v.Substring(dash + 1).Split('.'));
            }
            var (an, ap) = Parse(a);
            var (bn, bp) = Parse(b);
            for (int i = 0; i < 3; i++) if (an[i] != bn[i]) return an[i].CompareTo(bn[i]);
            if (ap.Length == 0 || bp.Length == 0) return bp.Length.CompareTo(ap.Length); // без суффикса — новее
            for (int i = 0; i < Math.Min(ap.Length, bp.Length); i++)
            {
                bool ai = int.TryParse(ap[i], out var x), bi = int.TryParse(bp[i], out var y);
                int c = ai && bi ? x.CompareTo(y) : ai ? -1 : bi ? 1 : string.CompareOrdinal(ap[i], bp[i]);
                if (c != 0) return c;
            }
            return ap.Length.CompareTo(bp.Length);
        }
    }
}
