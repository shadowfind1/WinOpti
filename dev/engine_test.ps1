# Проверка движка твиков WinOpti 2 с правами администратора: выключить и снова включить три твика разных типов.
$log = "C:\Users\smola\Desktop\WinOpti\engine-test.log"
"[{0:HH:mm:ss}] старт" -f (Get-Date) | Out-File $log -Encoding utf8
try {
    $asm = [Reflection.Assembly]::LoadFrom("C:\Users\smola\Desktop\WinOpti\WinOpti2\bin\Debug\net48\WinOpti.exe")
    $tweaks = $asm.GetType("WinOpti.Tweaks.Tweak").GetMethod("LoadAll").Invoke($null, $null)
    $eng = $asm.GetType("WinOpti.Tweaks.TweakEngine")
    foreach ($id in "faster_menus", "disable_maps_updates", "high_performance_power") {
        $t = ($tweaks | Where-Object Id -eq $id | Select-Object -First 1).psobject.BaseObject
        $s0 = $eng.GetMethod("GetState").Invoke($null, @($t))
        $w1 = $eng.GetMethod("Revert").Invoke($null, @($t))
        $s1 = $eng.GetMethod("GetState").Invoke($null, @($t))
        $w2 = $eng.GetMethod("Apply").Invoke($null, @($t))
        $s2 = $eng.GetMethod("GetState").Invoke($null, @($t))
        "{0}: было {1} -> выкл {2} -> вкл {3} | {4} {5}" -f $id, $s0, $s1, $s2, $w1, $w2 | Out-File $log -Append -Encoding utf8
    }
    (powercfg /getactivescheme) | Out-File $log -Append -Encoding utf8
} catch { $_ | Out-String | Out-File $log -Append -Encoding utf8 }
"[{0:HH:mm:ss}] конец" -f (Get-Date) | Out-File $log -Append -Encoding utf8

