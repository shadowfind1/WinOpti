using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using WinOpti.Sys;

namespace WinOpti.Cleanup
{
    /// <summary>
    /// Пункт очистки: Scan — сколько байт можно освободить, Clean — удалить и вернуть, сколько освобождено.
    /// Удаляется только то, что Windows и программы создают заново сами. Занятые файлы пропускаются.
    /// </summary>
    public class CleanItem
    {
        public string Id, Name, Description;
        public bool Admin;
        /// <summary>Отмечен по умолчанию. Корзина и кэш шейдеров — нет: первое необратимо, второе нужно не всем.</summary>
        public bool DefaultOn = true;
        /// <summary>Заполняется после Clean, если очистка прошла, но не всё гладко.</summary>
        public string Warning;
        public Func<IEnumerable<string>> Roots;
        public TimeSpan MinAge = TimeSpan.Zero;
        /// <summary>Папки внутри Roots, которые этот пункт не трогает (они — отдельный пункт со своим выбором).</summary>
        public Func<IEnumerable<string>> Exclude = () => Enumerable.Empty<string>();

        /// <summary>Сколько уже удалено во время Clean — окно показывает это как прогресс.</summary>
        public long Done;

        public virtual long Scan() => Files.Walk(Roots(), MinAge, Exclude()).files.Sum(f => f.size);
        public virtual long Clean() => Files.Delete(Roots(), MinAge, Exclude(), n => System.Threading.Interlocked.Add(ref Done, n));
    }

    static class Files
    {
        // программы на PyInstaller (и WinOpti 1) распаковываются в %TEMP%\_MEIxxxxx — пока они работают, это их файлы
        static readonly Regex RunningApp = new Regex(@"^_MEI\d+$", RegexOptions.IgnoreCase);

        /// <summary>Файлы и папки под roots. В ссылки и junction не заходим: иначе ссылка из Temp на чужую папку удалила бы её содержимое.</summary>
        public static (List<(string path, long size)> files, List<string> dirs) Walk(IEnumerable<string> roots, TimeSpan minAge, IEnumerable<string> exclude = null)
        {
            var skip = new HashSet<string>((exclude ?? Enumerable.Empty<string>()).Select(x => x.TrimEnd('\\')), StringComparer.OrdinalIgnoreCase);
            var files = new List<(string, long)>();
            var dirs = new List<string>();
            var stack = new Stack<string>();
            var cutoff = DateTime.UtcNow - minAge;
            foreach (var root in roots)
            {
                try
                {
                    var attr = File.GetAttributes(root);
                    if ((attr & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attr & FileAttributes.Directory) != 0) stack.Push(root);
                    else
                    {
                        var fi = new FileInfo(root);
                        if (fi.LastWriteTimeUtc <= cutoff) files.Add((root, fi.Length));
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                IEnumerable<FileSystemInfo> entries;
                try { entries = new DirectoryInfo(d).EnumerateFileSystemInfos().ToList(); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException) { continue; }
                foreach (var e in entries)
                {
                    try
                    {
                        if ((e.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (e is DirectoryInfo di)
                        {
                            if (RunningApp.IsMatch(di.Name) || skip.Contains(di.FullName.TrimEnd('\\'))) continue;
                            stack.Push(di.FullName);
                            dirs.Add(di.FullName);
                        }
                        else if (e is FileInfo fi && fi.LastWriteTimeUtc <= cutoff) files.Add((fi.FullName, fi.Length));
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                }
            }
            return (files, dirs);
        }

        public static long Delete(IEnumerable<string> roots, TimeSpan minAge, IEnumerable<string> exclude = null, Action<long> onFreed = null)
        {
            var (files, dirs) = Walk(roots, minAge, exclude);
            long freed = 0;
            foreach (var (path, size) in files)
            {
                try
                {
                    var attr = File.GetAttributes(path);
                    if ((attr & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attr & ~FileAttributes.ReadOnly); // «только чтение» мешает удалению
                    File.Delete(path);
                    freed += size;
                    onFreed?.Invoke(size);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { } // файл занят программой — пропускаем
            }
            foreach (var d in dirs.OrderByDescending(x => x.Length)) // сначала самые глубокие
            {
                try { Directory.Delete(d, false); } // удалится только пустая
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
            return freed;
        }
    }

    // ---------- особые пункты ----------

    /// <summary>Скачанные обновления Windows. Службы обновления на время очистки останавливаем (иначе файлы заняты), потом запускаем те, что работали.</summary>
    sealed class UpdateCache : CleanItem
    {
        static readonly string[] Services = { "wuauserv", "bits" };
        static bool Running(string s) => Regex.IsMatch(SysInfo.Run("sc.exe", "query " + s).output, @"STATE\s*:\s*4\b"); // 4 = RUNNING на любом языке

        public override long Clean()
        {
            Warning = null;
            var was = Services.Where(Running).ToList();
            foreach (var s in was) SysInfo.Run("powershell", $"-NoProfile -Command \"Stop-Service -Name {s} -Force\"");
            try { return base.Clean(); }
            finally
            {
                var failed = was.Where(s => { SysInfo.Run("powershell", $"-NoProfile -Command \"Start-Service -Name {s}\""); return !Running(s); }).ToList();
                if (failed.Count > 0)
                    Warning = $"службы {string.Join(", ", failed)} не запустились обратно; Windows запустит их сама при проверке обновлений или после перезагрузки";
            }
        }
    }

    sealed class DeliveryCache : CleanItem
    {
        public override long Clean()
        {
            long before = Scan();
            var (code, output) = SysInfo.Run("powershell", "-NoProfile -Command \"Delete-DeliveryOptimizationCache -Force -ErrorAction Stop\"");
            if (code != 0) throw new InvalidOperationException(output.Trim().Split('\n')[0]);
            return Math.Max(before - Scan(), 0);
        }
    }

    sealed class RecycleBin : CleanItem
    {
        [StructLayout(LayoutKind.Sequential)] struct SHQUERYRBINFO { public int cbSize; public long i64Size, i64NumItems; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHQueryRecycleBin(string root, ref SHQUERYRBINFO info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

        public override long Scan()
        {
            var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf(typeof(SHQUERYRBINFO)) };
            int hr = SHQueryRecycleBin(null, ref info);
            if (hr != 0) throw new IOException($"SHQueryRecycleBin: 0x{hr:x8}");
            return info.i64Size;
        }

        public override long Clean()
        {
            long before = Scan();
            if (before == 0) return 0;
            int hr = SHEmptyRecycleBin(IntPtr.Zero, null, 0x7); // без подтверждения, прогресса и звука
            if (hr != 0 && unchecked((uint)hr) != 0x8000FFFF) throw new IOException($"SHEmptyRecycleBin: 0x{hr:x8}"); // E_UNEXPECTED = уже пуста
            return Math.Max(before - Scan(), 0);
        }
    }

    // ---------- список ----------

    public static class Cleaner
    {
        static readonly string Win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static readonly string SysDrive = Path.GetPathRoot(Win);

        /// <summary>Защита от странного %TEMP% (например, C:\): чистим только папку с именем Temp/Tmp.</summary>
        static string CheckTemp(string path)
        {
            var name = Path.GetFileName(path.TrimEnd('\\'));
            if (!name.Equals("Temp", StringComparison.OrdinalIgnoreCase) && !name.Equals("Tmp", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"подозрительная папка {path}, пропускаю");
            return path;
        }

        public static readonly List<CleanItem> All = new List<CleanItem>
        {
            new CleanItem
            {
                Id = "user_temp", Name = "Временные файлы",
                Description = "Остатки установщиков, распакованные архивы, кэш программ. Файлы младше часа не трогаем, чтобы не помешать работающим программам.",
                Roots = () => new[] { CheckTemp(Path.GetTempPath()) }, MinAge = TimeSpan.FromHours(1),
                Exclude = () => new[] { Path.Combine(Path.GetTempPath(), @"NVIDIA Corporation\NV_Cache") }, // это кэш шейдеров — отдельный пункт
            },
            new CleanItem
            {
                Id = "windows_temp", Name = "Временные файлы Windows", Admin = true,
                Description = "Служебные временные файлы системы и установщиков в папке Windows\\Temp. Файлы младше часа не трогаем.",
                Roots = () => new[] { Path.Combine(Win, "Temp") }, MinAge = TimeSpan.FromHours(1),
            },
            new UpdateCache
            {
                Id = "update_cache", Name = "Кэш обновлений Windows", Admin = true,
                Description = "Файлы уже установленных обновлений. Windows их больше не использует. Не запускай очистку, пока идёт установка обновлений.",
                Roots = () => new[] { Path.Combine(Win, "SoftwareDistribution", "Download") },
            },
            new DeliveryCache
            {
                Id = "delivery_cache", Name = "Кэш раздачи обновлений", Admin = true,
                Description = "Копии обновлений и приложений, которые Windows хранит, чтобы раздавать другим компьютерам.",
                Roots = () => new[] { Path.Combine(Win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache") },
            },
            new CleanItem
            {
                Id = "error_reports", Name = "Отчёты об ошибках и дампы памяти", Admin = true,
                Description = "Отчёты о вылетах программ и дампы после «синих экранов». Нужны только для диагностики, а занимать могут гигабайты.",
                Roots = () => new[] { "ReportArchive", "ReportQueue", "Temp" }.Select(d => Path.Combine(ProgramData, @"Microsoft\Windows\WER", d))
                    .Concat(new[] { Path.Combine(Win, "Minidump"), Path.Combine(Win, "MEMORY.DMP") }),
            },
            new CleanItem
            {
                Id = "driver_installers", Name = "Старые установщики драйверов видеокарты", Admin = true,
                Description = "После установки драйвера NVIDIA и AMD оставляют распакованный установщик — часто несколько гигабайт. Сам драйвер он не трогает: удаляются только файлы установки.",
                Roots = () => new[] { Path.Combine(SysDrive, "NVIDIA"), Path.Combine(SysDrive, "AMD"), Path.Combine(ProgramData, @"NVIDIA Corporation\Downloader") },
            },
            new CleanItem
            {
                Id = "shader_cache", Name = "Кэш шейдеров видеокарты", DefaultOn = false,
                Description = "Видеокарта сохраняет сюда скомпилированные эффекты игр, чтобы не собирать их заново. Чисти, только если после обновления драйвера появились артефакты или статтеры: первые запуски игр после очистки будут подтормаживать, пока кэш соберётся снова.",
                Roots = () => new[] { @"NVIDIA\DXCache", @"NVIDIA\GLCache", "D3DSCache", @"AMD\DxCache", @"AMD\DxcCache", @"AMD\VkCache", @"AMD\GLCache" }
                    .Select(d => Path.Combine(Local, d)).Concat(new[] { Path.Combine(Local, @"Temp\NVIDIA Corporation\NV_Cache") }),
            },
            new RecycleBin
            {
                Id = "recycle_bin", Name = "Корзина", DefaultOn = false,
                Description = "Файлы, удалённые в корзину, на всех дисках. После очистки их уже не вернуть.",
                Roots = () => Enumerable.Empty<string>(),
            },
        };

        public static string Format(long bytes)
        {
            double n = bytes;
            if (n < 1024) return $"{n:0} Б";
            n /= 1024; if (n < 1024) return $"{n:0} КБ";
            n /= 1024; if (n < 1024) return $"{n:0.#} МБ";
            return $"{n / 1024:0.##} ГБ";
        }
    }
}
