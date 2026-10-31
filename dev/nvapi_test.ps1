# Только чтение: названия и текущие значения настроек драйвера NVIDIA по номерам
param([string]$Exe)
$asm = [Reflection.Assembly]::LoadFrom($Exe)
$t = $asm.GetType("WinOpti.Tests.NvidiaSettings")
$nv = $t.GetMethod("Open").Invoke($null, $null)
if (-not $nv) { "NVAPI not available"; exit }
foreach ($id in 0x1057EB71, 0x007BA09E, 0x0005F543, 0x10835000) {
  $name = $t.GetMethod("NameOf").Invoke($null, @([uint32]$id))
  $v = $t.GetMethod("Get").Invoke($nv, @([uint32]$id))
  "0x{0:X8} name='{1}' value={2}" -f $id, $name, $v.Item1
}
$nv.Dispose()
