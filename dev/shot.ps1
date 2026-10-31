param([string]$Exe, [string]$Out, [int]$Wait = 6, [int]$W = 1180, [int]$H = 780, [string]$AppArgs = "")
# Запуск программы, замер времени до появления окна и памяти, снимок ТОЛЬКО её окна (PrintWindow), закрытие.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class Win {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
  public struct RECT { public int L, T, R, B; }
}
"@
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = if ($AppArgs) { Start-Process $Exe -ArgumentList $AppArgs -PassThru } else { Start-Process $Exe -PassThru }
while ($p.MainWindowHandle -eq 0 -and $sw.ElapsedMilliseconds -lt 15000) { Start-Sleep -Milliseconds 20; $p.Refresh() }
"window after {0} ms" -f $sw.ElapsedMilliseconds
[Win]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, 40, 40, $W, $H, 0x0004) | Out-Null
Start-Sleep -Seconds $Wait
$p.Refresh()
"RAM working set {0:N0} MB, private {1:N0} MB" -f ($p.WorkingSet64/1MB), ($p.PrivateMemorySize64/1MB)
$r = New-Object Win+RECT
[Win]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$bmp = New-Object Drawing.Bitmap ($r.R - $r.L), ($r.B - $r.T)
$g = [Drawing.Graphics]::FromImage($bmp)
$hdc = $g.GetHdc()
[Win]::PrintWindow($p.MainWindowHandle, $hdc, 2) | Out-Null
$g.ReleaseHdc($hdc); $g.Dispose()
$bmp.Save($Out); $bmp.Dispose()
$p.CloseMainWindow() | Out-Null
Start-Sleep -Milliseconds 500
if (!$p.HasExited) { $p.Kill() }

