# Проверка BackgroundMonitor: 4 секунды сбора, вывод самых занятых процессов
param([string]$Exe)
$asm = [Reflection.Assembly]::LoadFrom($Exe)
$t = $asm.GetType("WinOpti.Fps.BackgroundMonitor")
$m = [Activator]::CreateInstance($t)
$t.GetMethod("Start").Invoke($m, $null)
Start-Sleep -Seconds 8
$samples = @($t.GetMethod("Stop").Invoke($m, $null)) | % { $_ }
"samples: {0}" -f $samples.Count
foreach ($s in $samples) {
  $top = $s.Cpu.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 4 | % { "{0}={1:N1}%" -f $_.Key, $_.Value }
  $gpu = if ($s.Gpu) { ($s.Gpu.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 3 | % { "{0}={1:N1}%" -f $_.Key, $_.Value }) -join ", " } else { "-" }
  "t={0:N1}s cpu: {1} | gpu: {2} | cpuPerf={3:N0}% gpuT={4} gpuMHz={5} why={6}" -f $s.T, ($top -join ", "), $gpu, $s.CpuPerf, $s.GpuSensors.TempC, $s.GpuSensors.CoreMhz, $s.GpuSensors.Reasons
}
