using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace WinOpti.Fps
{
    /// <summary>Один срез: секунда от начала замера и нагрузка по процессам (в процентах от всего процессора / видеокарты).</summary>
    public sealed class LoadSample
    {
        public double T;
        public Dictionary<string, double> Cpu = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, double> Gpu;
        /// <summary>Частота процессора в % от базовой (больше 100 — турбо). null — счётчик недоступен.</summary>
        public double? CpuPerf;
        /// <summary>Датчики видеокарты NVIDIA; null — не NVIDIA.</summary>
        public Tests.NvidiaSettings.GpuSensors? GpuSensors;
    }

    /// <summary>
    /// Что ещё работало, пока шёл замер. Процессор — раз в секунду одним вызовом NtQuerySystemInformation:
    /// без открытия процессов, поэтому видны и защищённые (Защитник, служебные), и это почти ничего не стоит игре.
    /// Видеокарта — раз в 2 секунды по счётчикам «GPU Engine». Поток с низким приоритетом.
    /// </summary>
    public sealed class BackgroundMonitor : IDisposable
    {
        readonly List<LoadSample> _samples = new List<LoadSample>();
        readonly Stopwatch _clock = new Stopwatch();
        readonly Thread _thread;
        volatile bool _stop;

        public BackgroundMonitor()
        {
            _thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "WinOpti load monitor" };
        }

        public void Start() { _clock.Start(); _thread.Start(); }

        /// <summary>Секунды от начала мониторинга — в этих же секундах надо считать моменты кадров.</summary>
        public double Now => _clock.Elapsed.TotalSeconds;

        public List<LoadSample> Stop()
        {
            _stop = true;
            _thread.Join(3000);
            lock (_samples) return _samples.ToList();
        }

        public void Dispose() => _stop = true;

        void Loop()
        {
            Dictionary<IntPtr, (string name, long time)> prev = null;
            double prevT = 0;
            Dictionary<string, CounterSample> prevGpu = null;
            int cores = Environment.ProcessorCount, tick = 0;
            PerformanceCounter perf = null;
            try { perf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total"); perf.NextValue(); } catch { perf = null; }
            bool nvidia = Tests.NvidiaSettings.ReadSensors() != null;
            while (!_stop)
            {
                try
                {
                    double t = _clock.Elapsed.TotalSeconds;
                    var now = Snapshot();
                    var sample = new LoadSample { T = t };
                    if (prev != null && t > prevT)
                    {
                        double wall = (t - prevT) * 1e7 * cores; // в тиках по 100 нс на все ядра
                        foreach (var kv in now)
                        {
                            if (!prev.TryGetValue(kv.Key, out var p) || p.name != kv.Value.name) continue;
                            double pct = (kv.Value.time - p.time) / wall * 100;
                            if (pct <= 0.05) continue;
                            sample.Cpu.TryGetValue(kv.Value.name, out var sum);
                            sample.Cpu[kv.Value.name] = sum + pct; // у Chrome и Discord десятки процессов — складываем по имени
                        }
                    }
                    if (tick++ % 2 == 0) sample.Gpu = GpuByProcess(ref prevGpu, now);
                    try { sample.CpuPerf = perf?.NextValue(); } catch { }
                    if (nvidia) sample.GpuSensors = Tests.NvidiaSettings.ReadSensors();
                    if (prev != null) lock (_samples) _samples.Add(sample);
                    prev = now;
                    prevT = t;
                }
                catch { }
                for (int i = 0; i < 10 && !_stop; i++) Thread.Sleep(100);
            }
        }

        // ---------- процессор ----------

        [DllImport("ntdll.dll")]
        static extern int NtQuerySystemInformation(int cls, IntPtr buffer, int length, out int returned);

        int _bufSize = 1 << 20;

        /// <summary>pid -> (имя без .exe, суммарное время процессора в тиках по 100 нс).</summary>
        Dictionary<IntPtr, (string name, long time)> Snapshot()
        {
            const int SystemProcessInformation = 5;
            while (true)
            {
                var buf = Marshal.AllocHGlobal(_bufSize);
                try
                {
                    int rc = NtQuerySystemInformation(SystemProcessInformation, buf, _bufSize, out int need);
                    if (rc == unchecked((int)0xC0000004)) { _bufSize = Math.Max(need, _bufSize * 2) + 65536; continue; } // мал буфер
                    if (rc != 0) throw new InvalidOperationException("NtQuerySystemInformation: " + rc.ToString("x"));
                    var result = new Dictionary<IntPtr, (string name, long time)>();
                    int offset = 0;
                    while (true)
                    {
                        var p = buf + offset;
                        // SYSTEM_PROCESS_INFORMATION (x64): UserTime 40, KernelTime 48, ImageName 56 (Length, Buffer @64), UniqueProcessId 80
                        long user = Marshal.ReadInt64(p, 40), kernel = Marshal.ReadInt64(p, 48);
                        int len = Marshal.ReadInt16(p, 56);
                        var namePtr = Marshal.ReadIntPtr(p, 64);
                        var pid = Marshal.ReadIntPtr(p, 80);
                        if (pid != IntPtr.Zero) // 0 — «бездействие системы»
                        {
                            var name = namePtr == IntPtr.Zero ? "System" : Marshal.PtrToStringUni(namePtr, len / 2);
                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                            result[pid] = (name, user + kernel);
                        }
                        int next = Marshal.ReadInt32(p, 0);
                        if (next == 0) break;
                        offset += next;
                    }
                    return result;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }

        // ---------- видеокарта ----------

        static readonly Regex PidRe = new Regex(@"^pid_(\d+)_", RegexOptions.Compiled);

        /// <summary>Загрузка видеокарты по процессам: по каждому процессу берём самый занятый движок (3D, видео, копирование).</summary>
        static Dictionary<string, double> GpuByProcess(ref Dictionary<string, CounterSample> prev, Dictionary<IntPtr, (string name, long time)> procs)
        {
            InstanceDataCollectionCollection data;
            try { data = new PerformanceCounterCategory("GPU Engine").ReadCategory(); }
            catch { return null; } // старые драйверы или Windows без счётчиков GPU
            var util = data["Utilization Percentage"];
            if (util == null) return null;
            var cur = new Dictionary<string, CounterSample>();
            var perPid = new Dictionary<int, double>();
            foreach (InstanceData inst in util.Values)
            {
                cur[inst.InstanceName] = inst.Sample;
                if (prev == null || !prev.TryGetValue(inst.InstanceName, out var before)) continue;
                var m = PidRe.Match(inst.InstanceName);
                if (!m.Success) continue;
                double v = CounterSample.Calculate(before, inst.Sample);
                int pid = int.Parse(m.Groups[1].Value);
                perPid.TryGetValue(pid, out var max);
                perPid[pid] = Math.Max(max, v);
            }
            bool first = prev == null;
            prev = cur;
            if (first) return null;
            var byName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in perPid)
            {
                if (kv.Value <= 0.1 || !procs.TryGetValue(new IntPtr(kv.Key), out var p)) continue;
                byName.TryGetValue(p.name, out var sum);
                byName[p.name] = Math.Min(100, sum + kv.Value);
            }
            return byName;
        }
    }
}
