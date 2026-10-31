using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using WinOpti.Sys;

namespace WinOpti.Tests
{
    /// <summary>Снимок настроек драйвера AMD — ТОЛЬКО чтение. Два снимка «до» и «после» переключения Anti-Lag/Chill в AMD Software
    /// показывают, какой ключ реестра за что отвечает: так появятся настоящие переключатели без живой карты у разработчика.</summary>
    public static class AmdProbe
    {
        const string DisplayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

        /// <summary>Пишет снимок на рабочий стол и возвращает путь к файлу.</summary>
        public static string Snapshot()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"WinOpti AMD probe · WinOpti {Update.Updater.Current} · Windows build {Environment.OSVersion.Version.Build} · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();

            sb.AppendLine("== GPUs");
            foreach (var o in SysInfo.Wmi("SELECT Name, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController"))
                sb.AppendLine($"{o["Name"]} | driver {o["DriverVersion"]} | {o["DriverDate"]} | {o["PNPDeviceID"]}");
            sb.AppendLine();

            var adlx = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "amdadlx64.dll");
            sb.AppendLine("== ADLX: " + (File.Exists(adlx) ? "present, " + FileVersionInfo.GetVersionInfo(adlx).FileVersion : "not found"));
            sb.AppendLine();

            using (var cls = Registry.LocalMachine.OpenSubKey(DisplayClass))
            {
                foreach (var name in cls?.GetSubKeyNames().Where(n => n.All(char.IsDigit)) ?? Enumerable.Empty<string>())
                {
                    using (var k = Open(cls, name))
                    {
                        var provider = (k?.GetValue("ProviderName") as string ?? "") + " " + (k?.GetValue("DriverDesc") as string ?? "");
                        if (k == null || !new[] { "AMD", "Advanced Micro", "ATI", "Radeon" }.Any(w => provider.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                        sb.AppendLine($"== HKLM\\{DisplayClass}\\{name}  ({provider.Trim()})");
                        Dump(k, sb, 0, 3);
                        sb.AppendLine();
                    }
                }
            }

            using (var cn = Registry.CurrentUser.OpenSubKey(@"Software\AMD"))
            {
                sb.AppendLine(@"== HKCU\Software\AMD");
                if (cn != null) Dump(cn, sb, 0, 4); else sb.AppendLine("(none)");
            }

            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"winopti-amd-{DateTime.Now:HHmmss}.txt");
            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            return file;
        }

        static RegistryKey Open(RegistryKey parent, string name)
        {
            try { return parent.OpenSubKey(name); } catch { return null; } // ветка Properties закрыта даже для чтения
        }

        static void Dump(RegistryKey k, StringBuilder sb, int depth, int maxDepth)
        {
            var pad = new string(' ', depth * 2);
            foreach (var v in k.GetValueNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                object val;
                try { val = k.GetValue(v, null, RegistryValueOptions.DoNotExpandEnvironmentNames); } catch { continue; }
                sb.AppendLine($"{pad}{(v == "" ? "(default)" : v)} = {Format(val)}");
            }
            if (depth >= maxDepth) return;
            foreach (var sub in k.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                using (var s = Open(k, sub))
                {
                    if (s == null) { sb.AppendLine($"{pad}[{sub}] (no access)"); continue; }
                    sb.AppendLine($"{pad}[{sub}]");
                    Dump(s, sb, depth + 1, maxDepth);
                }
            }
        }

        static string Format(object v)
        {
            switch (v)
            {
                case byte[] b: return b.Length > 64 ? $"bin[{b.Length}]" : "bin:" + BitConverter.ToString(b);
                case string[] m: return "[" + string.Join(", ", m) + "]";
                case int i: return $"{i} (0x{i:X})";
                default: return v?.ToString() ?? "null";
            }
        }
    }
}
