using System.Collections.Generic;

namespace WinOpti.Sys
{
    internal sealed class DisplayInfo
    {
        public string Device;      // \\.\DISPLAY1
        public string Monitor;     // имя монитора, если Windows его знает
        public string Adapter;     // видеокарта, к которой подключён монитор
        public bool Primary;
        public int Width, Height, Hz, MaxHz;
    }

    /// <summary>Мониторы: текущая частота и максимальная при том же разрешении.</summary>
    internal static class Displays
    {
        public static List<DisplayInfo> All()
        {
            var list = new List<DisplayInfo>();
            for (uint i = 0; ; i++)
            {
                var dd = Native.DISPLAY_DEVICE.Create();
                if (!Native.EnumDisplayDevices(null, i, ref dd, 0)) break;
                if ((dd.StateFlags & Native.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0) continue;

                var cur = Native.DEVMODE.Create();
                if (!Native.EnumDisplaySettings(dd.DeviceName, Native.ENUM_CURRENT_SETTINGS, ref cur)) continue;

                int max = cur.dmDisplayFrequency;
                var m = Native.DEVMODE.Create();
                for (int k = 0; Native.EnumDisplaySettings(dd.DeviceName, k, ref m); k++)
                    if (m.dmPelsWidth == cur.dmPelsWidth && m.dmPelsHeight == cur.dmPelsHeight && m.dmDisplayFrequency > max)
                        max = m.dmDisplayFrequency;

                var mon = Native.DISPLAY_DEVICE.Create();
                string monitor = Native.EnumDisplayDevices(dd.DeviceName, 0, ref mon, 0) ? mon.DeviceString : null;

                list.Add(new DisplayInfo
                {
                    Device = dd.DeviceName,
                    Monitor = monitor,
                    Adapter = dd.DeviceString,
                    Primary = (dd.StateFlags & Native.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0,
                    Width = cur.dmPelsWidth,
                    Height = cur.dmPelsHeight,
                    Hz = cur.dmDisplayFrequency,
                    MaxHz = max,
                });
            }
            return list;
        }

        /// <summary>Поставить частоту при текущем разрешении. Права администратора не нужны.</summary>
        public static bool SetHz(string device, int hz)
        {
            var dm = Native.DEVMODE.Create();
            if (!Native.EnumDisplaySettings(device, Native.ENUM_CURRENT_SETTINGS, ref dm)) return false;
            dm.dmDisplayFrequency = hz;
            dm.dmFields = Native.DM_DISPLAYFREQUENCY;
            return Native.ChangeDisplaySettingsEx(device, ref dm, System.IntPtr.Zero, Native.CDS_UPDATEREGISTRY, System.IntPtr.Zero)
                   == Native.DISP_CHANGE_SUCCESSFUL;
        }
    }
}
