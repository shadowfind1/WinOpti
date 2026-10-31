using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace WinOpti.Tests
{
    /// <summary>
    /// Реальная частота опроса мыши. Слушаем Raw Input (WM_INPUT): Windows присылает отдельное сообщение на каждый отчёт мыши,
    /// в отличие от WM_MOUSEMOVE, который склеивается. Пока мышь непрерывно движется, считаем отчёты в окнах по 100 мс.
    /// </summary>
    public sealed class MouseRate : IDisposable
    {
        const int WM_INPUT = 0x00FF;
        const uint RID_INPUT = 0x10000003, RIM_TYPEMOUSE = 0;

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTHEADER { public uint Type, Size; public IntPtr Device, WParam; }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWMOUSE { public ushort Flags; public ushort Pad; public uint Buttons; public uint RawButtons; public int LastX, LastY; public uint Extra; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

        [DllImport("user32.dll")]
        static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, uint headerSize);

        readonly HwndSource _source;
        readonly Stopwatch _clock = Stopwatch.StartNew();
        // устройство -> моменты отчётов с движением (тики Stopwatch)
        readonly Dictionary<IntPtr, List<long>> _events = new Dictionary<IntPtr, List<long>>();
        readonly IntPtr _buf = Marshal.AllocHGlobal(64);
        bool _on;

        /// <summary>Вызывается на каждом отчёте с движением (в потоке окна).</summary>
        public event Action Moved;

        public MouseRate(HwndSource window)
        {
            _source = window;
        }

        public void Start()
        {
            lock (_events) _events.Clear();
            if (!_on)
            {
                // мышь: usage page 1, usage 2; без RIDEV_INPUTSINK — нужно только пока окно WinOpti активно
                var dev = new[] { new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = 0, Target = _source.Handle } };
                if (!RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
                    throw new InvalidOperationException("Windows не дала читать мышь напрямую (Raw Input).");
                _source.AddHook(Hook);
                _on = true;
            }
        }

        public void Stop()
        {
            if (!_on) return;
            _source.RemoveHook(Hook);
            // RIDEV_REMOVE: окно больше не получает WM_INPUT
            var dev = new[] { new RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = 0x1, Target = IntPtr.Zero } };
            RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            _on = false;
        }

        IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_INPUT) return IntPtr.Zero;
            long now = _clock.ElapsedTicks;
            uint headerSize = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            uint size = 64;
            if (GetRawInputData(lParam, RID_INPUT, _buf, ref size, headerSize) == unchecked((uint)-1)) return IntPtr.Zero;
            var header = Marshal.PtrToStructure<RAWINPUTHEADER>(_buf);
            if (header.Type != RIM_TYPEMOUSE) return IntPtr.Zero;
            var mouse = Marshal.PtrToStructure<RAWMOUSE>(_buf + (int)headerSize);
            if (mouse.LastX == 0 && mouse.LastY == 0) return IntPtr.Zero; // кнопки и колесо без движения не считаем
            lock (_events)
            {
                if (!_events.TryGetValue(header.Device, out var list)) _events[header.Device] = list = new List<long>(8192);
                list.Add(now);
            }
            Moved?.Invoke();
            return IntPtr.Zero;
        }

        /// <summary>Сколько секунд мышь двигалась без остановок (паузы дольше 50 мс не считаются).</summary>
        public double MovingSeconds
        {
            get
            {
                var t = Busiest();
                if (t == null || t.Count < 2) return 0;
                double sec = 0, gap = Stopwatch.Frequency * 0.05;
                for (int i = 1; i < t.Count; i++) { long d = t[i] - t[i - 1]; if (d < gap) sec += d; }
                return sec / Stopwatch.Frequency;
            }
        }

        List<long> Busiest()
        {
            lock (_events) return _events.Values.OrderByDescending(l => l.Count).FirstOrDefault()?.ToList();
        }

        /// <summary>
        /// -> (измеренная частота, ближайшая стандартная). Берём окна по 100 мс, где мышь двигалась всё окно,
        /// и смотрим верхний (90-й) перцентиль: при медленном движении мышь шлёт отчёты реже своей частоты.
        /// </summary>
        public (double measured, int standard) Result()
        {
            var t = Busiest();
            if (t == null || t.Count < 50) return (0, 0);
            long win = Stopwatch.Frequency / 10, gap = Stopwatch.Frequency / 20;
            var rates = new List<double>();
            int start = 0;
            for (int i = 1; i <= t.Count; i++)
            {
                if (i < t.Count && t[i] - t[i - 1] < gap) continue;
                // непрерывный отрезок [start, i) — режем на окна по 100 мс
                int a = start;
                for (int b = start; b < i; b++)
                {
                    if (t[b] - t[a] < win) continue;
                    rates.Add((b - a) * (double)Stopwatch.Frequency / (t[b] - t[a]));
                    a = b;
                }
                start = i;
            }
            if (rates.Count == 0) return (0, 0);
            rates.Sort();
            double measured = rates[(int)Math.Min(rates.Count - 1, Math.Floor(rates.Count * 0.9))];
            int[] standard = { 125, 250, 500, 1000, 2000, 4000, 8000 };
            int snap = standard.OrderBy(s => Math.Abs(Math.Log(measured / s))).First();
            return (measured, snap);
        }

        public void Dispose()
        {
            Stop();
            Marshal.FreeHGlobal(_buf);
        }
    }
}
