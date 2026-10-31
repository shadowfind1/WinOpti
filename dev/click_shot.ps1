param([string]$Exe, [string]$AppArgs, [string]$Button, [int]$After = 5, [string]$Out, [int]$H = 900)
# Открыть страницу, нажать кнопку по x:Name через UI Automation, через After секунд снять окно.
Add-Type -AssemblyName System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Win2 {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  public struct RECT { public int L, T, R, B; }
}
"@
$p = Start-Process $Exe -ArgumentList $AppArgs -PassThru
while ($p.MainWindowHandle -eq 0) { Start-Sleep -Milliseconds 50; $p.Refresh() }
$h = $p.MainWindowHandle
[Win2]::SetWindowPos($h, [IntPtr]::Zero, 40, 40, 1180, $H, 0x0004) | Out-Null
Start-Sleep -Seconds 2
$root = [Windows.Automation.AutomationElement]::FromHandle($h)
$cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::AutomationIdProperty), $Button
$btn = $root.FindFirst("Descendants", $cond)
$btn.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds $After
$r = New-Object Win2+RECT; [Win2]::GetWindowRect($h, [ref]$r) | Out-Null
$bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
[Win2]::PrintWindow($h, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose(); $bmp.Save($Out); $bmp.Dispose()
$p.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 500; if (!$p.HasExited) { $p.Kill() }
