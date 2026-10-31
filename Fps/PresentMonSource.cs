using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace WinOpti.Fps
{
    /// <summary>
    /// Кадры через Intel PresentMon (MIT, подписан Intel) — тот же инструмент, которым замеряют обзорщики.
    /// exe встроен в WinOpti как ресурс и при первом замере распаковывается в %LOCALAPPDATA%\WinOpti\tools.
    /// Нужны права администратора: Windows отдаёт события кадров (ETW) только им.
    /// </summary>
    public sealed class PresentMonSource : IFrameSource
    {
        const string Resource = "WinOpti.Tools.PresentMon.exe";
        const string FileName = "PresentMon-2.6.0-x64.exe";

        /// <summary>Первые секунды замера отбрасываем: это момент входа в игру (переключение окна, догрузка),
        /// а не сама игра. Записываем на столько же дольше, чтобы чистое время замера осталось прежним.</summary>
        public const int WarmupSeconds = 3;

        static string ExtractExe()
        {
            var dir = Path.Combine(App.DataDir, "tools");
            var path = Path.Combine(dir, FileName);
            using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
            {
                if (res == null) throw new InvalidOperationException(L.T("В сборке нет модуля замера кадров.", "This build has no frame capture module."));
                if (File.Exists(path) && new FileInfo(path).Length == res.Length) return path;
                Directory.CreateDirectory(dir);
                using (var f = File.Create(path)) res.CopyTo(f);
            }
            return path;
        }

        public FrameCapture Capture(TimeSpan duration, CancellationToken cancel)
        {
            if (!Sys.SysInfo.IsAdmin)
                throw new InvalidOperationException(L.T("Для замера FPS нужны права администратора: Windows отдаёт данные о кадрах только им. Запусти WinOpti от имени администратора.", "The FPS test needs administrator rights: Windows only gives frame data to admins. Run WinOpti as administrator."));
            var exe = ExtractExe();
            var csv = Path.Combine(Path.GetTempPath(), $"winopti_frames_{Guid.NewGuid():N}.csv");
            var args = $"--output_file \"{csv}\" --timed {(int)Math.Ceiling(duration.TotalSeconds) + WarmupSeconds} --terminate_after_timed " +
                       "--stop_existing_session --session_name WinOpti --no_console_stats --v1_metrics --no_track_input --no_track_gpu";
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    p.StandardOutput.ReadToEndAsync();
                    while (!p.WaitForExit(200))
                    {
                        if (!cancel.IsCancellationRequested) continue;
                        try { p.Kill(); } catch { }
                        cancel.ThrowIfCancellationRequested();
                    }
                    if (!File.Exists(csv))
                    {
                        var msg = err.Result.Trim();
                        throw new InvalidOperationException(msg.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0
                            ? L.T("Windows не дала начать замер: нужны права администратора.", "Windows didn’t allow the test to start: administrator rights needed.")
                            : L.T("Замер не получился", "The test failed") + (msg.Length > 0 ? ": " + msg.Split('\n').Last().Trim() : "."));
                    }
                }
                return Parse(csv, WarmupSeconds);
            }
            finally
            {
                try { File.Delete(csv); } catch { }
            }
        }

        /// <summary>CSV PresentMon (метрики 1.x): Application, …, TimeInSeconds, MsBetweenPresents. Группируем по процессу,
        /// кадры раньше skipSeconds от начала записи пропускаем.</summary>
        public static FrameCapture Parse(string csv, double skipSeconds = 0)
        {
            var capture = new FrameCapture();
            var result = capture.Frames;
            using (var r = new StreamReader(csv))
            {
                var header = r.ReadLine()?.Split(',');
                if (header == null) return capture;
                int Col(string name) => Array.FindIndex(header, h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));
                int app = Col("Application"), ms = Col("MsBetweenPresents"), time = Col("TimeInSeconds");
                if (app < 0 || ms < 0) throw new InvalidOperationException(L.T("Неизвестный формат данных замера.", "Unknown capture data format."));
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    var cols = line.Split(',');
                    if (cols.Length <= Math.Max(app, ms)) continue;
                    if (!double.TryParse(cols[ms], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                    double at = -1;
                    if (time >= 0 && time < cols.Length) double.TryParse(cols[time], NumberStyles.Float, CultureInfo.InvariantCulture, out at);
                    if (skipSeconds > 0 && at >= 0 && at < skipSeconds) continue;
                    var name = cols[app];
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                    if (!result.TryGetValue(name, out var list)) { result[name] = list = new List<double>(); capture.Times[name] = new List<double>(); }
                    list.Add(v);
                    capture.Times[name].Add(at);
                }
            }
            // первый кадр процесса не имеет предыдущего — у PresentMon там 0
            foreach (var kv in result) if (kv.Value.Count > 0 && kv.Value[0] <= 0) { kv.Value.RemoveAt(0); capture.Times[kv.Key].RemoveAt(0); }
            return capture;
        }
    }
}
