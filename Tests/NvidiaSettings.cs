using System;
using System.Runtime.InteropServices;

namespace WinOpti.Tests
{
    /// <summary>
    /// Глобальные настройки драйвера NVIDIA (то же, что «Панель управления NVIDIA → Управление параметрами 3D → Глобальные»).
    /// Через NVAPI — официальный интерфейс драйвера, как у NVIDIA Profile Inspector. Без драйвера NVIDIA просто недоступно.
    /// </summary>
    public sealed class NvidiaSettings : IDisposable
    {
        // режим управления электропитанием: 0 — адаптивный, 1 — максимальная производительность, 5 — оптимальное энергопотребление
        public const uint PowerMode = 0x1057EB71;
        // «Режим низкой задержки»: максимум заранее подготовленных кадров (1 = «Вкл») и состояние в панели (0 выкл, 1 вкл, 2 ультра)
        public const uint MaxPrerendered = 0x007BA09E;
        public const uint LowLatencyCpl = 0x0005F543;
        public const uint UltraLowLatency = 0x10835000;

        [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr QueryInterface(uint id);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitializeFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CreateSessionFn(out IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SessionFn(IntPtr session);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetBaseProfileFn(IntPtr session, out IntPtr profile);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetSettingFn(IntPtr session, IntPtr profile, uint id, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetSettingFn(IntPtr session, IntPtr profile, IntPtr setting);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int RestoreDefaultFn(IntPtr session, IntPtr profile, uint id);

        static T Fn<T>(uint id) where T : class
        {
            var p = QueryInterface(id);
            if (p == IntPtr.Zero) throw new InvalidOperationException($"NVAPI: нет функции 0x{id:X8}");
            return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T;
        }

        // NVDRS_SETTING v1: version, имя (2048 символов), id, тип, где задано, 2 флага, предустановленное и текущее значения (по 4100 байт)
        const int SettingSize = 4 + 4096 + 4 * 5 + 4100 + 4100;
        const uint SettingVersion = SettingSize | (1u << 16);
        const int OffName = 4, OffId = 4100, OffType = 4104, OffLocation = 4108, OffCurrent = 4120 + 4100;
        const int SettingNotFound = -160;

        readonly IntPtr _session, _profile;
        readonly IntPtr _buf = Marshal.AllocHGlobal(SettingSize);

        NvidiaSettings(IntPtr session, IntPtr profile) { _session = session; _profile = profile; }

        /// <summary>Открыть настройки драйвера. -> null, если NVIDIA нет или драйвер не ответил.</summary>
        public static NvidiaSettings Open()
        {
            try
            {
                if (Fn<InitializeFn>(0x0150E828)() != 0) return null;
                if (Fn<CreateSessionFn>(0x0694D52E)(out var session) != 0) return null;
                if (Fn<SessionFn>(0x375DBD6B)(session) != 0 || Fn<GetBaseProfileFn>(0xDA8466A0)(session, out var profile) != 0)
                {
                    Fn<SessionFn>(0xDAD9CFF8)(session);
                    return null;
                }
                return new NvidiaSettings(session, profile);
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
        }

        void Clear()
        {
            for (int i = 0; i < SettingSize; i += 8) Marshal.WriteInt64(_buf, i, 0);
            Marshal.WriteInt32(_buf, 0, (int)SettingVersion);
        }

        /// <summary>-> (значение, задано ли оно явно, а не по умолчанию драйвера; имя настройки в драйвере). Нет настройки — value null.</summary>
        public (uint? value, string name) Get(uint id)
        {
            Clear();
            int rc = Fn<GetSettingFn>(0x73BF8338)(_session, _profile, id, _buf);
            if (rc == SettingNotFound) return (null, null);
            if (rc != 0) throw new InvalidOperationException($"NVAPI GetSetting 0x{id:X8}: {rc}");
            return ((uint)Marshal.ReadInt32(_buf, OffCurrent), Marshal.PtrToStringUni(_buf + OffName));
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int NameFromIdFn(uint id, IntPtr name);

        /// <summary>Название настройки по номеру — так драйвер подтверждает, что номер верный.</summary>
        public static string NameOf(uint id)
        {
            var buf = Marshal.AllocHGlobal(4096);
            try { return Fn<NameFromIdFn>(0xD61CBE6E)(id, buf) == 0 ? Marshal.PtrToStringUni(buf) : null; }
            finally { Marshal.FreeHGlobal(buf); }
        }

        public void Set(uint id, uint value)
        {
            Clear();
            Marshal.WriteInt32(_buf, OffId, (int)id);
            Marshal.WriteInt32(_buf, OffType, 0); // NVDRS_DWORD_TYPE
            Marshal.WriteInt32(_buf, OffCurrent, (int)value);
            int rc = Fn<SetSettingFn>(0x577DD202)(_session, _profile, _buf);
            if (rc != 0) throw new InvalidOperationException($"NVAPI SetSetting 0x{id:X8}: {rc}");
        }

        /// <summary>Вернуть настройке значение драйвера по умолчанию.</summary>
        public void Reset(uint id)
        {
            int rc = Fn<RestoreDefaultFn>(0x53F0381E)(_session, _profile, id);
            if (rc != 0 && rc != SettingNotFound) throw new InvalidOperationException($"NVAPI RestoreDefault 0x{id:X8}: {rc}");
        }

        public void Save()
        {
            int rc = Fn<SessionFn>(0xFCBC7E14)(_session);
            if (rc != 0) throw new InvalidOperationException("NVAPI SaveSettings: " + rc + (rc == -137 ? L.T(" (нужны права администратора)", " (administrator rights needed)") : ""));
        }

        public void Dispose()
        {
            try { Fn<SessionFn>(0xDAD9CFF8)(_session); } catch { }
            Marshal.FreeHGlobal(_buf);
        }

        // ---------- датчики видеокарты (только чтение) ----------

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int EnumGpusFn([Out] IntPtr[] handles, out int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GpuBufFn(IntPtr gpu, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GpuUintFn(IntPtr gpu, out uint value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ThermalFn(IntPtr gpu, uint sensorIndex, IntPtr data);

        /// <summary>Причины, по которым драйвер снизил частоту (NV_GPU_PERF_DECREASE).</summary>
        [Flags]
        public enum Throttle : uint { None = 0, Thermal = 0x1, PowerLimit = 0x2, Battery = 0x4, Api = 0x8, InsufficientPower = 0x10 }

        public struct GpuSensors
        {
            public int TempC;        // температура ядра, -1 если нет
            public int CoreMhz;      // текущая частота ядра, 0 если нет
            public Throttle Reasons;
        }

        static IntPtr _gpu;

        /// <summary>Температура, частота и причины сброса частоты первой видеокарты NVIDIA. null — не NVIDIA.</summary>
        public static GpuSensors? ReadSensors()
        {
            try
            {
                if (_gpu == IntPtr.Zero)
                {
                    if (Fn<InitializeFn>(0x0150E828)() != 0) return null;
                    var handles = new IntPtr[64];
                    if (Fn<EnumGpusFn>(0xE5AC921F)(handles, out int n) != 0 || n == 0) return null;
                    _gpu = handles[0];
                }
                var s = new GpuSensors { TempC = -1 };
                // NV_GPU_THERMAL_SETTINGS v2: version, count, 3 датчика по 5 int (controller, min, max, current, target)
                const int thermalSize = 8 + 3 * 20;
                var buf = Marshal.AllocHGlobal(264);
                try
                {
                    for (int i = 0; i < 264; i += 4) Marshal.WriteInt32(buf, i, 0);
                    Marshal.WriteInt32(buf, 0, thermalSize | (2 << 16));
                    if (Fn<ThermalFn>(0xE3640A56)(_gpu, 15, buf) == 0 && Marshal.ReadInt32(buf, 4) > 0) s.TempC = Marshal.ReadInt32(buf, 8 + 12);
                    // NV_GPU_CLOCK_FREQUENCIES v2: version, тип (0 = текущие), 32 домена по 8 байт (присутствует, МГц*1000); домен 0 — ядро
                    for (int i = 0; i < 264; i += 4) Marshal.WriteInt32(buf, i, 0);
                    Marshal.WriteInt32(buf, 0, 264 | (2 << 16));
                    if (Fn<GpuBufFn>(0xDCB616C3)(_gpu, buf) == 0 && (Marshal.ReadInt32(buf, 8) & 1) == 1) s.CoreMhz = Marshal.ReadInt32(buf, 12) / 1000;
                }
                finally { Marshal.FreeHGlobal(buf); }
                if (Fn<GpuUintFn>(0x7F7F4600)(_gpu, out uint reasons) == 0) s.Reasons = (Throttle)reasons;
                return s;
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
            catch (InvalidOperationException) { return null; }
        }
    }
}
