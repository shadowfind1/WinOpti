# Разово: вернуть замеры CS2 пользователя (240→277), стёртые демо-прогоном 31.10. Цифры — с карточки результата.
param([string]$Exe)
$asm = [Reflection.Assembly]::LoadFrom($Exe)
$runT = $asm.GetType("WinOpti.Fps.FpsRun"); $statT = $asm.GetType("WinOpti.Fps.FrameStats"); $store = $asm.GetType("WinOpti.Fps.FpsStore")
function Run($when, $label, $avg, $low1, $low01, $stutters) {
  $s = [Activator]::CreateInstance($statT)
  $s.AvgFps = [double]$avg; $s.Low1 = [double]$low1; $s.Low01 = [double]$low01; $s.Stutters = $stutters; $s.Seconds = 60.0; $s.Frames = [int]($avg * 60)
  $r = [Activator]::CreateInstance($runT)
  $r.Game = "cs2"; $r.When = [datetime]$when; $r.Label = $label; $r.Stats = $s
  return $r
}
$list = [Activator]::CreateInstance([Collections.Generic.List``1].MakeGenericType($runT))
$list.Add((Run "2026-10-31T07:40:00" "После" 277 109 16 27))
$list.Add((Run "2026-10-31T07:05:00" "До" 240 89 11 29))
$store.GetMethod("Save").Invoke($null, [object[]]@(,$list))
"saved"
