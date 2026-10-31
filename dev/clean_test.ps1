param([string]$Exe, [string]$OutPrefix, [int]$Files = 1500)
# Проверка индикатора очистки: кладёт в Temp пачку старых пустышек, жмёт «Очистить выбранное» через UI Automation
# и снимает окно во время очистки и после. Чистит то же, что и обычная кнопка (отмеченные пункты).
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Win {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  public struct RECT { public int L, T, R, B; }
}
"@
$junk = Join-Path $env:TEMP "winopti_progress_test"
New-Item -ItemType Directory -Force $junk | Out-Null
$buf = New-Object byte[] (256KB)
$old = (Get-Date).AddDays(-3)
1..$Files | % { $f = Join-Path $junk "f$_.bin"; [IO.File]::WriteAllBytes($f, $buf); [IO.File]::SetLastWriteTime($f, $old) }

function Shot($h, $out) {
  $r = New-Object Win+RECT; [Win]::GetWindowRect($h, [ref]$r) | Out-Null
  $bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
  $g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
  [Win]::PrintWindow($h, $hdc, 2) | Out-Null
  $g.ReleaseHdc($hdc); $g.Dispose(); $bmp.Save($out); $bmp.Dispose()
}

$p = Start-Process $Exe -ArgumentList "--page=clean" -PassThru
while ($p.MainWindowHandle -eq 0) { Start-Sleep -Milliseconds 50; $p.Refresh() }
$h = $p.MainWindowHandle
[Win]::SetWindowPos($h, [IntPtr]::Zero, 40, 40, 1180, 780, 0x0004) | Out-Null
$root = [Windows.Automation.AutomationElement]::FromHandle($h)
$cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty), "CleanButton"
$btn = $null
for ($i = 0; $i -lt 100 -and -not ($btn -and $btn.Current.IsEnabled); $i++) { Start-Sleep -Milliseconds 200; $btn = $root.FindFirst("Descendants", $cond) }
Shot $h "$OutPrefix-0-before.png"
$btn.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 300; Shot $h "$OutPrefix-1-during.png"
Start-Sleep -Milliseconds 600; Shot $h "$OutPrefix-2-during.png"
Start-Sleep -Seconds 4; Shot $h "$OutPrefix-3-after.png"
$p.CloseMainWindow() | Out-Null
Start-Sleep -Milliseconds 500
if (!$p.HasExited) { $p.Kill() }
"left in junk dir: {0} files" -f @(Get-ChildItem $junk -ErrorAction SilentlyContinue).Count
