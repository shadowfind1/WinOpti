using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using WinOpti.Sys;

namespace WinOpti.Tests
{
    public sealed class Gpu
    {
        public string Name;
        public bool Integrated;
    }

    public sealed class InstalledGame
    {
        public string Name, Exe;
        /// <summary>0 — решает Windows, 1 — энергосбережение (встроенная), 2 — высокая производительность (мощная).</summary>
        public int Preference;
    }

    /// <summary>
    /// Через какую видеокарту идут игры. На ноутбуках и ПК с включённой встроенной графикой Windows может запустить игру
    /// на встроенной — FPS падает в разы. Ещё частая ошибка — монитор подключён к материнской плате, а не к видеокарте.
    /// </summary>
    public static class GpuCheck
    {
        const string PrefKey = @"Software\Microsoft\DirectX\UserGpuPreferences";

        public static List<Gpu> Gpus()
        {
            var list = new List<Gpu>();
            foreach (var v in SysInfo.Wmi("SELECT Name, PNPDeviceID FROM Win32_VideoController"))
            {
                var name = (v["Name"] as string ?? "").Trim();
                var pnp = v["PNPDeviceID"] as string ?? "";
                if (!pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) continue; // «Базовый видеоадаптер», виртуальные и удалённые экраны
                list.Add(new Gpu { Name = name, Integrated = IsIntegrated(name, pnp) });
            }
            return list;
        }

        static bool IsIntegrated(string name, string pnp)
        {
            if (pnp.IndexOf("VEN_10DE", StringComparison.OrdinalIgnoreCase) >= 0) return false; // NVIDIA
            if (pnp.IndexOf("VEN_8086", StringComparison.OrdinalIgnoreCase) >= 0) return !Regex.IsMatch(name, @"Arc(\(TM\))?\s+[AB]\d", RegexOptions.IgnoreCase); // Intel: всё, кроме Arc A/B
            if (pnp.IndexOf("VEN_1002", StringComparison.OrdinalIgnoreCase) >= 0) return !Regex.IsMatch(name, @"\b(RX|R9|R7|Pro|FirePro|Vega\s*(56|64))\b", RegexOptions.IgnoreCase); // AMD: «Radeon Graphics», «Vega 8» — встроенные
            return false;
        }

        /// <summary>Есть мощная видеокарта, а основной монитор подключён к встроенной -> имя встроенной, иначе null.</summary>
        public static string MonitorOnIntegrated(List<Gpu> gpus)
        {
            if (!gpus.Any(g => !g.Integrated) || !gpus.Any(g => g.Integrated)) return null;
            var primary = Displays.All().FirstOrDefault(d => d.Primary);
            if (primary?.Adapter == null) return null;
            var gpu = gpus.FirstOrDefault(g => g.Name.Equals(primary.Adapter.Trim(), StringComparison.OrdinalIgnoreCase));
            return gpu != null && gpu.Integrated ? gpu.Name : null;
        }

        // ---------- установленные игры ----------

        // exe игр Steam, которые по имени папки не угадать
        static readonly Dictionary<string, string> SteamExe = new Dictionary<string, string>
        {
            ["730"] = @"game\bin\win64\cs2.exe", ["570"] = @"game\bin\win64\dota2.exe", ["578080"] = @"TslGame\Binaries\Win64\TslGame.exe",
            ["252490"] = "RustClient.exe", ["1172470"] = "r5apex.exe", ["359550"] = "RainbowSix.exe", ["271590"] = "GTA5.exe",
            ["1938090"] = "cod.exe", ["2357570"] = "Overwatch.exe", ["1085660"] = "destiny2.exe", ["440"] = "tf_win64.exe",
        };

        static readonly Regex NotGameExe = new Regex(@"unins|setup|install|crash|report|redist|vc_?redist|dxsetup|prereq|easyanticheat|battleye|beservice|launcher|helper|update|cef|webhelper|7z|dotnet|python|ue4prereq|uploader|benchmark|config|editor|server|handler|link|unitycrash",
                                                    RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<InstalledGame> Games()
        {
            var games = new List<InstalledGame>();
            try { games.AddRange(SteamGames()); } catch { }
            try { games.AddRange(EpicGames()); } catch { }
            var valorant = @"C:\Riot Games\VALORANT\live\ShooterGame\Binaries\Win64\VALORANT-Win64-Shipping.exe";
            if (File.Exists(valorant)) games.Add(new InstalledGame { Name = "VALORANT", Exe = valorant });
            var lol = @"C:\Riot Games\League of Legends\Game\League of Legends.exe";
            if (File.Exists(lol)) games.Add(new InstalledGame { Name = "League of Legends", Exe = lol });
            // игры, которым уже назначали видеокарту вручную (через Windows или WinOpti)
            var prefs = Preferences();
            foreach (var path in prefs.Keys)
                if (!games.Any(g => g.Exe.Equals(path, StringComparison.OrdinalIgnoreCase)) && File.Exists(path) && LooksLikeGame(path))
                    games.Add(new InstalledGame { Name = Path.GetFileNameWithoutExtension(path), Exe = path });
            foreach (var g in games) g.Preference = prefs.TryGetValue(g.Exe, out var p) ? p : 0;
            return games.GroupBy(g => g.Exe, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        static bool LooksLikeGame(string path) => path.IndexOf(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase) >= 0
                                                 || path.IndexOf("Games", StringComparison.OrdinalIgnoreCase) >= 0;

        static IEnumerable<InstalledGame> SteamGames()
        {
            var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
            if (steam == null) yield break;
            steam = steam.Replace('/', '\\');
            var libs = new List<string> { steam };
            var vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\""))
                    libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            foreach (var lib in libs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (var acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                {
                    var text = File.ReadAllText(acf);
                    string Field(string key) { var m = Regex.Match(text, "\"" + key + "\"\\s+\"(.*?)\""); return m.Success ? m.Groups[1].Value : null; }
                    string id = Field("appid"), name = Field("name"), dir = Field("installdir");
                    if (id == null || dir == null || id == "228980" || Regex.IsMatch(name ?? "", "Redistributable|Steamworks|Proton|Runtime|SDK|Dedicated Server|Wallpaper Engine|SteamVR|Soundpad|OBS Studio|Blender|Lossless Scaling", RegexOptions.IgnoreCase)) continue;
                    var folder = Path.Combine(apps, "common", dir);
                    var exe = SteamExe.TryGetValue(id, out var rel) ? Path.Combine(folder, rel) : GuessExe(folder, name);
                    if (exe != null && File.Exists(exe)) yield return new InstalledGame { Name = name, Exe = exe };
                }
            }
        }

        static IEnumerable<InstalledGame> EpicGames()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests");
            if (!Directory.Exists(dir)) yield break;
            var json = new JavaScriptSerializer();
            foreach (var f in Directory.GetFiles(dir, "*.item"))
            {
                Dictionary<string, object> d;
                try { d = json.DeserializeObject(File.ReadAllText(f)) as Dictionary<string, object>; } catch { continue; }
                if (d == null) continue;
                var loc = d.TryGetValue("InstallLocation", out var l) ? l as string : null;
                var launch = d.TryGetValue("LaunchExecutable", out var e) ? e as string : null;
                var name = d.TryGetValue("DisplayName", out var n) ? n as string : null;
                if (loc == null || string.IsNullOrEmpty(launch)) continue;
                var exe = Path.Combine(loc, launch);
                if (File.Exists(exe)) yield return new InstalledGame { Name = name ?? Path.GetFileNameWithoutExtension(exe), Exe = exe };
            }
        }

        static string Letters(string s) => Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]", "");

        /// <summary>Главный exe игры: похожий на название (или его сокращение), иначе самый большой. Служебные exe не берём.</summary>
        static string GuessExe(string folder, string name)
        {
            if (!Directory.Exists(folder)) return null;
            var exes = new List<(FileInfo file, int depth)>();
            var stack = new Stack<(string dir, int depth)>();
            stack.Push((folder, 0));
            while (stack.Count > 0)
            {
                var (d, depth) = stack.Pop();
                try
                {
                    exes.AddRange(new DirectoryInfo(d).GetFiles("*.exe").Where(f => !NotGameExe.IsMatch(f.Name)).Select(f => (f, depth)));
                    if (depth < 5) foreach (var s in Directory.GetDirectories(d)) stack.Push((s, depth + 1));
                }
                catch { }
            }
            if (exes.Count == 0) return null;
            var full = Letters(name);
            var acronym = string.Concat(Regex.Split(name ?? "", @"[\s\-:]+").Where(w => w.Length > 0).Select(w => char.IsDigit(w[0]) ? w : w.Substring(0, 1))).ToLowerInvariant();
            int Score(FileInfo f)
            {
                var b = Letters(Path.GetFileNameWithoutExtension(f.Name));
                if (b.Length == 0) return 0;
                if (b == full || b == acronym) return 3;
                if (b.Length >= 3 && (full.Contains(b) || b.Contains(full))) return 2;
                int common = 0;
                while (common < Math.Min(b.Length, full.Length) && b[common] == full[common]) common++;
                return common >= 4 ? 1 : 0; // AimLab_tb.exe у «Aimlabs»
            }
            // похожее имя, потом ближе к корню папки (у Unity-игр exe лежит в корне), потом самый большой
            return exes.OrderByDescending(x => Score(x.file)).ThenBy(x => x.depth).ThenByDescending(x => x.file.Length).First().file.FullName;
        }

        // ---------- настройка Windows «Графика» ----------

        static Dictionary<string, int> Preferences()
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            using (var k = Registry.CurrentUser.OpenSubKey(PrefKey))
            {
                if (k == null) return d;
                foreach (var n in k.GetValueNames())
                {
                    var m = Regex.Match(k.GetValue(n) as string ?? "", @"GpuPreference=(\d)");
                    if (m.Success) d[n] = int.Parse(m.Groups[1].Value);
                }
            }
            return d;
        }

        /// <summary>То же, что «Параметры → Дисплей → Графика → Высокая производительность». Права администратора не нужны.</summary>
        public static void SetHighPerformance(InstalledGame g)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(PrefKey))
            {
                var old = k.GetValue(g.Exe) as string ?? "";
                // другие флаги (например, SwapEffectUpgradeEnable) сохраняем
                var rest = Regex.Replace(old, @"GpuPreference=\d;?", "");
                k.SetValue(g.Exe, "GpuPreference=2;" + rest, RegistryValueKind.String);
            }
            g.Preference = 2;
        }
    }
}
