using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace WinOpti.Sys
{
    /// <summary>Чтение железа и состояния Windows: WMI, powercfg, счётчики процессов.</summary>
    internal static class SysInfo
    {
        public static bool IsAdmin
        {
            get { try { return Native.IsUserAnAdmin(); } catch { return false; } }
        }

        public static List<ManagementObject> Wmi(string query, string scope = @"root\cimv2")
        {
            var result = new List<ManagementObject>();
            using (var s = new ManagementObjectSearcher(scope, query))
                foreach (ManagementObject o in s.Get()) result.Add(o);
            return result;
        }

        public static (int code, string output) Run(string file, string args)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                // консольные утилиты (sc, schtasks, powercfg) пишут в OEM-кодировке — иначе русский текст ошибок нечитаем
                StandardOutputEncoding = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                StandardErrorEncoding = System.Text.Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return (p.ExitCode, o);
            }
        }

        public const string PlanPowerSaver = "a1841308-3541-4fab-bc81-f71556f20b4a";
        public const string PlanBalanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public const string PlanHigh = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public const string PlanUltimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        /// <summary>GUID и имя активного плана питания.</summary>
        public static (string guid, string name) ActivePowerPlan()
        {
            var (_, o) = Run("powercfg", "/getactivescheme");
            var m = Regex.Match(o, @"([0-9a-f]{8}-[0-9a-f-]{27})\s*\((.+)\)", RegexOptions.IgnoreCase);
            return m.Success ? (m.Groups[1].Value.ToLowerInvariant(), m.Groups[2].Value.Trim()) : (null, null);
        }

        public static List<string> PowerPlans()
        {
            var (_, o) = Run("powercfg", "/list");
            return Regex.Matches(o, @"[0-9a-f]{8}-[0-9a-f-]{27}", RegexOptions.IgnoreCase).Cast<Match>()
                .Select(m => m.Value.ToLowerInvariant()).ToList();
        }

        public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Native.GetTickCount64());

        /// <summary>"10" или "11" — как в поле windows у твиков.</summary>
        public static string WindowsVersion => Environment.OSVersion.Version.Build >= 22000 ? "11" : "10";

        public static bool IsLaptop => Wmi("SELECT BatteryStatus FROM Win32_Battery").Count > 0;

        /// <summary>Файл лежит на механическом жёстком диске? null — не удалось понять (сетевой диск, USB, нет данных).</summary>
        public static bool? IsHdd(string path)
        {
            try
            {
                var letter = char.ToUpperInvariant(System.IO.Path.GetPathRoot(path)[0]);
                var part = Wmi("SELECT DiskNumber, DriveLetter FROM MSFT_Partition", @"root\Microsoft\Windows\Storage")
                    .FirstOrDefault(p => Convert.ToChar(p["DriveLetter"] ?? 0) == letter);
                if (part == null) return null;
                var disk = Wmi($"SELECT MediaType FROM MSFT_PhysicalDisk WHERE DeviceId = '{part["DiskNumber"]}'", @"root\Microsoft\Windows\Storage").FirstOrDefault();
                if (disk == null) return null;
                int media = Convert.ToInt32(disk["MediaType"] ?? 0);
                return media == 3 ? true : media == 4 ? false : (bool?)null; // 3 — HDD, 4 — SSD, 0 — не указано
            }
            catch { return null; }
        }

        /// <summary>Ноутбук работает от батареи (BatteryStatus 1 = разряжается).</summary>
        public static bool OnBattery => Wmi("SELECT BatteryStatus FROM Win32_Battery")
            .Any(b => Convert.ToInt32(b["BatteryStatus"] ?? 0) == 1);

        /// <summary>Загрузка процессора по процессам за interval: [имя, % от всего процессора].</summary>
        public static List<(string name, double cpu)> CpuHogs(TimeSpan interval)
        {
            var before = new Dictionary<int, (string, TimeSpan)>();
            foreach (var p in Process.GetProcesses())
            {
                try { before[p.Id] = (p.ProcessName, p.TotalProcessorTime); } catch { }
                finally { p.Dispose(); }
            }
            System.Threading.Thread.Sleep(interval);
            var self = Process.GetCurrentProcess().Id;
            var res = new Dictionary<string, double>();
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == self || p.Id == 0 || !before.TryGetValue(p.Id, out var b)) continue;
                    double share = (p.TotalProcessorTime - b.Item2).TotalMilliseconds / interval.TotalMilliseconds / Environment.ProcessorCount * 100;
                    res[b.Item1] = (res.TryGetValue(b.Item1, out var v) ? v : 0) + share;
                }
                catch { }
                finally { p.Dispose(); }
            }
            return res.OrderByDescending(x => x.Value).Select(x => (x.Key, x.Value)).ToList();
        }
    }
}
