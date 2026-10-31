using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinOpti.Fps
{
    /// <summary>
    /// Какая игра сейчас открыта на весь экран. Замер стартует сам, когда она появится, —
    /// без таймера: на слабом ПК игра может грузиться минуту, и торопить человека незачем.
    /// </summary>
    public static class ForegroundGame
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        /// <summary>Имя процесса (без .exe), если активное окно — не системное и закрывает весь монитор (полный экран или окно без рамки).</summary>
        public static string Detect()
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return null;
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            if (!GetMonitorInfo(MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */), ref mi)) return null;
            var m = mi.rcMonitor;
            bool fullscreen = r.Left <= m.Left && r.Top <= m.Top && r.Right >= m.Right && r.Bottom >= m.Bottom;
            if (!fullscreen) return null;
            GetWindowThreadProcessId(hwnd, out uint pid);
            try
            {
                using (var p = Process.GetProcessById((int)pid))
                    return GamePicker.IsNotGame(p.ProcessName) ? null : p.ProcessName;
            }
            catch { return null; }
        }
    }
}
