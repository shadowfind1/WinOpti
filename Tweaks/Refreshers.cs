using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using WinOpti.Sys;

namespace WinOpti.Tweaks
{
    /// <summary>
    /// Применение без перезахода: после записи в реестр сообщаем Windows новые значения.
    /// Каждая функция читает то, что уже записано, поэтому одинаково работает после включения и после отката.
    /// </summary>
    public static class Refreshers
    {
        const uint SPIF_SENDCHANGE = 0x02;
        const uint SPI_SETMOUSE = 0x0004, SPI_SETANIMATION = 0x0049, SPI_SETMENUSHOWDELAY = 0x006B;
        const uint SPI_SETKEYBOARDSPEED = 0x000B, SPI_SETKEYBOARDDELAY = 0x0017;
        const uint SPI_GETFILTERKEYS = 0x0032, SPI_SETFILTERKEYS = 0x0033;
        const uint SPI_GETTOGGLEKEYS = 0x0034, SPI_SETTOGGLEKEYS = 0x0035;
        const uint SPI_GETSTICKYKEYS = 0x003A, SPI_SETSTICKYKEYS = 0x003B;

        [StructLayout(LayoutKind.Sequential)] struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }
        [StructLayout(LayoutKind.Sequential)] struct FLAGS { public uint cbSize; public uint dwFlags; }
        [StructLayout(LayoutKind.Sequential)] struct FILTERKEYS { public uint cbSize, dwFlags, iWaitMSec, iDelayMSec, iRepeatMSec, iBounceMSec; }

        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint ui, IntPtr pv, uint f);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint ui, int[] pv, uint f);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint ui, ref ANIMATIONINFO pv, uint f);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint ui, ref FLAGS pv, uint f);
        [DllImport("user32.dll", SetLastError = true)] static extern bool SystemParametersInfo(uint a, uint ui, ref FILTERKEYS pv, uint f);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint msg, UIntPtr w, string l, uint flags, uint timeout, out UIntPtr result);
        [DllImport("shell32.dll")] static extern void SHGetSetSettings(uint[] state, uint mask, bool set);
        [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr a, IntPtr b);

        static void Check(bool ok) { if (!ok) throw new System.ComponentModel.Win32Exception(); }

        static int? HkcuInt(string path, string name)
        {
            var v = Reg.Get(RegistryHive.CurrentUser, path, name);
            if (v is int i) return i;
            return v is string s && int.TryParse(s, out var n) ? n : (int?)null;
        }

        /// <summary>WM_SETTINGCHANGE всем окнам: «перечитайте настройку area».</summary>
        static void Broadcast(string area) => SendMessageTimeout((IntPtr)0xFFFF, 0x001A, UIntPtr.Zero, area, 0x0002, 2000, out _);

        static void Kill(params string[] images)
        {
            foreach (var img in images)
                foreach (var p in Process.GetProcessesByName(img))
                    using (p) try { p.Kill(); } catch { } // процесса нет или он защищён — не важно
        }

        static readonly Dictionary<string, Action> All = new Dictionary<string, Action>
        {
            ["animation"] = () =>
            {
                if (HkcuInt(@"Control Panel\Desktop\WindowMetrics", "MinAnimate") is int v)
                {
                    var info = new ANIMATIONINFO { cbSize = (uint)Marshal.SizeOf(typeof(ANIMATIONINFO)), iMinAnimate = v };
                    Check(SystemParametersInfo(SPI_SETANIMATION, info.cbSize, ref info, SPIF_SENDCHANGE));
                }
            },
            ["mouse"] = () =>
            {
                var t1 = HkcuInt(@"Control Panel\Mouse", "MouseThreshold1");
                var t2 = HkcuInt(@"Control Panel\Mouse", "MouseThreshold2");
                var sp = HkcuInt(@"Control Panel\Mouse", "MouseSpeed");
                if (t1 != null && t2 != null && sp != null) Check(SystemParametersInfo(SPI_SETMOUSE, 0, new[] { t1.Value, t2.Value, sp.Value }, SPIF_SENDCHANGE));
            },
            ["keyboard"] = () =>
            {
                if (HkcuInt(@"Control Panel\Keyboard", "KeyboardDelay") is int d) Check(SystemParametersInfo(SPI_SETKEYBOARDDELAY, (uint)d, IntPtr.Zero, SPIF_SENDCHANGE));
                if (HkcuInt(@"Control Panel\Keyboard", "KeyboardSpeed") is int s) Check(SystemParametersInfo(SPI_SETKEYBOARDSPEED, (uint)s, IntPtr.Zero, SPIF_SENDCHANGE));
            },
            ["accessibility"] = () =>
            {
                const string b = @"Control Panel\Accessibility\";
                if (HkcuInt(b + "StickyKeys", "Flags") is int sk)
                {
                    var f = new FLAGS { cbSize = (uint)Marshal.SizeOf(typeof(FLAGS)) };
                    Check(SystemParametersInfo(SPI_GETSTICKYKEYS, f.cbSize, ref f, 0));
                    f.dwFlags = (uint)sk;
                    Check(SystemParametersInfo(SPI_SETSTICKYKEYS, f.cbSize, ref f, SPIF_SENDCHANGE));
                }
                if (HkcuInt(b + "Keyboard Response", "Flags") is int fk)
                {
                    var f = new FILTERKEYS { cbSize = (uint)Marshal.SizeOf(typeof(FILTERKEYS)) };
                    Check(SystemParametersInfo(SPI_GETFILTERKEYS, f.cbSize, ref f, 0));
                    f.dwFlags = (uint)fk;
                    Check(SystemParametersInfo(SPI_SETFILTERKEYS, f.cbSize, ref f, SPIF_SENDCHANGE));
                }
                if (HkcuInt(b + "ToggleKeys", "Flags") is int tk)
                {
                    var f = new FLAGS { cbSize = (uint)Marshal.SizeOf(typeof(FLAGS)) };
                    Check(SystemParametersInfo(SPI_GETTOGGLEKEYS, f.cbSize, ref f, 0));
                    f.dwFlags = (uint)tk;
                    Check(SystemParametersInfo(SPI_SETTOGGLEKEYS, f.cbSize, ref f, SPIF_SENDCHANGE));
                }
            },
            ["menu_delay"] = () =>
            {
                if (HkcuInt(@"Control Panel\Desktop", "MenuShowDelay") is int v) Check(SystemParametersInfo(SPI_SETMENUSHOWDELAY, (uint)v, IntPtr.Zero, SPIF_SENDCHANGE));
            },
            ["theme"] = () => Broadcast("ImmersiveColorSet"),
            ["tray"] = () => Broadcast("TraySettings"),
            ["explorer_view"] = () =>
            {
                const string adv = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
                var hidden = HkcuInt(adv, "Hidden");
                var hideExt = HkcuInt(adv, "HideFileExt");
                const uint mask = 0x1 | 0x2; // SSF_SHOWALLOBJECTS | SSF_SHOWEXTENSIONS
                var state = new uint[16];
                SHGetSetSettings(state, mask, false);
                state[0] = (state[0] & ~0x3u) | (hidden == 1 ? 0x1u : 0) | (hideExt == 0 ? 0x2u : 0);
                SHGetSetSettings(state, mask, true);
                SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); // обновить открытые окна
            },
            ["start_menu"] = () => Kill("StartMenuExperienceHost"),
            ["search"] = () => Kill("SearchApp", "SearchUI", "SearchHost"),
            ["widgets"] = () => Kill("Widgets", "WidgetService"),
            ["explorer"] = RestartExplorer,
        };

        /// <summary>Перезапуск проводника. Из процесса администратора его запускать нельзя (панель задач станет «админской»),
        /// поэтому ждём автоперезапуск Windows, а если его нет — запускаем от пользователя сессии через планировщик.</summary>
        static void RestartExplorer()
        {
            Kill("explorer");
            for (int i = 0; i < 25; i++)
            {
                Thread.Sleep(200);
                if (Process.GetProcessesByName("explorer").Length > 0) return;
            }
            var user = Environment.UserDomainName + "\\" + Environment.UserName;
            if (!SysInfo.IsAdmin) { Process.Start("explorer.exe"); return; }
            var ps = $"$p = New-ScheduledTaskPrincipal -UserId '{user}' -LogonType Interactive -RunLevel Limited; " +
                     "Register-ScheduledTask -TaskName 'WinOptiExplorer' -Principal $p -Force -Action (New-ScheduledTaskAction -Execute 'explorer.exe') | Out-Null; " +
                     "Start-ScheduledTask -TaskName 'WinOptiExplorer'; Start-Sleep -Seconds 2; Unregister-ScheduledTask -TaskName 'WinOptiExplorer' -Confirm:$false";
            var (code, output) = SysInfo.Run("powershell", $"-NoProfile -Command \"{ps}\"");
            if (code != 0) throw new InvalidOperationException("проводник не запустился, запусти explorer.exe вручную: " + output.Trim());
        }

        /// <summary>-> текст предупреждения или null. Сбой не откатывает твик: значения уже записаны, эффект будет после перезахода.</summary>
        public static string Run(Tweak t)
        {
            var errors = new List<string>();
            foreach (var name in t.Refresh)
            {
                try { if (All.TryGetValue(name, out var act)) act(); }
                catch (Exception e) { errors.Add($"{name}: {e.Message}"); }
            }
            return errors.Count == 0 ? null : "не удалось применить сразу, нужен перезаход (" + string.Join("; ", errors) + ")";
        }
    }
}
