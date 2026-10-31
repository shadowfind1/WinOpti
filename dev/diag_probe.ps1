# Пробный прогон диагностики WinOpti: только чтение, ничего не меняет.
$ErrorActionPreference = 'SilentlyContinue'
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Collections.Generic;
public static class Disp {
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  public struct DEVMODE { [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dmDeviceName; public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput; public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string dmFormName; public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency; }
  [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)]
  public struct DISPLAY_DEVICE { public int cb; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=32)] public string DeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceString; public int StateFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceID; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string DeviceKey; }
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool EnumDisplayDevices(string dev, int i, ref DISPLAY_DEVICE dd, int flags);
  public static List<string> Report() {
    var r = new List<string>();
    for (int i = 0; ; i++) {
      var dd = new DISPLAY_DEVICE(); dd.cb = Marshal.SizeOf(dd);
      if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
      if ((dd.StateFlags & 1) == 0) continue; // не подключён к рабочему столу
      var cur = new DEVMODE(); cur.dmSize = (short)Marshal.SizeOf(cur);
      EnumDisplaySettings(dd.DeviceName, -1, ref cur);
      int best = 0; var m = new DEVMODE(); m.dmSize = (short)Marshal.SizeOf(m);
      for (int k = 0; EnumDisplaySettings(dd.DeviceName, k, ref m); k++)
        if (m.dmPelsWidth == cur.dmPelsWidth && m.dmPelsHeight == cur.dmPelsHeight && m.dmDisplayFrequency > best) best = m.dmDisplayFrequency;
      r.Add(dd.DeviceName + " | " + dd.DeviceString + " | " + cur.dmPelsWidth + "x" + cur.dmPelsHeight + " | now " + cur.dmDisplayFrequency + " Hz | max " + best + " Hz");
    }
    return r;
  }
}
"@
"== Мониторы"; [Disp]::Report()

"== Память"
Get-CimInstance Win32_PhysicalMemory | ForEach-Object { "{0} | {1} GB | rated {2} MT/s | configured {3} MT/s | type {4} | {5}" -f $_.BankLabel, [math]::Round($_.Capacity/1GB), $_.Speed, $_.ConfiguredClockSpeed, $_.SMBIOSMemoryType, $_.PartNumber.Trim() }
$cs = Get-CimInstance Win32_ComputerSystem; "Всего: {0:N1} GB" -f ($cs.TotalPhysicalMemory/1GB)
$os = Get-CimInstance Win32_OperatingSystem; "Свободно: {0:N1} GB" -f ($os.FreePhysicalMemory/1MB)

"== Процессор / видеокарты"
(Get-CimInstance Win32_Processor | Select-Object -First 1).Name
Get-CimInstance Win32_VideoController | ForEach-Object { "{0} | driver {1} | {2:yyyy-MM-dd}" -f $_.Name, $_.DriverVersion, $_.DriverDate }
"Ноутбук (есть батарея): " + [bool](Get-CimInstance Win32_Battery)

"== Питание"
powercfg /getactivescheme

"== Windows"
"{0} build {1} | uptime {2:N1} h" -f $os.Caption, $os.BuildNumber, ((Get-Date) - $os.LastBootUpTime).TotalHours
$dg = Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard
"VBS status: " + $dg.VirtualizationBasedSecurityStatus + " | running services: " + ($dg.SecurityServicesRunning -join ',')
"HVCI policy (memory integrity): " + (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity').Enabled
"HAGS HwSchMode: " + (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers').HwSchMode
"Game Mode AutoGameModeEnabled: " + (Get-ItemProperty 'HKCU:\Software\Microsoft\GameBar').AutoGameModeEnabled
"Game DVR AppCaptureEnabled: " + (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR').AppCaptureEnabled
"Mouse MouseSpeed (accel): " + (Get-ItemProperty 'HKCU:\Control Panel\Mouse').MouseSpeed
"GPU preferences: " + ((Get-ItemProperty 'HKCU:\Software\Microsoft\DirectX\UserGpuPreferences').PSObject.Properties | Where-Object Name -notlike 'PS*' | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join '; '

"== Диски"
Get-PhysicalDisk | ForEach-Object { "{0} | {1} | {2} | {3:N0} GB | health {4}" -f $_.FriendlyName, $_.MediaType, $_.BusType, ($_.Size/1GB), $_.HealthStatus }
Get-Volume | Where-Object DriveLetter | ForEach-Object { "{0}: {1:N0} / {2:N0} GB free ({3:P0})" -f $_.DriveLetter, ($_.SizeRemaining/1GB), ($_.Size/1GB), ($_.SizeRemaining / [math]::Max(1,$_.Size)) }

"== Фоновая нагрузка (2 с)"
$a = Get-Process | Select-Object Id, ProcessName, CPU
Start-Sleep -Seconds 2
$b = Get-Process | Select-Object Id, ProcessName, CPU, WorkingSet64
$n = [Environment]::ProcessorCount
$b | ForEach-Object { $p = $_; $o = $a | Where-Object Id -eq $p.Id; if ($o -and $p.CPU) { [pscustomobject]@{ Name = $p.ProcessName; Cpu = [math]::Round(($p.CPU - $o.CPU) / 2 / $n * 100, 1); RamMB = [math]::Round($p.WorkingSet64/1MB) } } } | Sort-Object Cpu -Descending | Select-Object -First 6 | ForEach-Object { "{0} {1}% CPU {2} MB" -f $_.Name, $_.Cpu, $_.RamMB }
"Топ по памяти:"; Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 5 | ForEach-Object { "{0} {1:N0} MB" -f $_.ProcessName, ($_.WorkingSet64/1MB) }
