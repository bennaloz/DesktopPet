# Against a running `--selftest-mouse` session: asks Windows which window is under the cat, the bowl and an
# empty spot (the same hit-test real clicks use), and captures the overlay with PrintWindow.
param([string]$UserDir = "$env:APPDATA\Godot\app_userdata\Zaira Desktop Pet", [string]$Out = "$env:TEMP\zaira_probe.png")
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class H {
  [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
  [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint f);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  public static string At(int x, int y) {
    var h = GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2);
    var sb = new StringBuilder(256); GetWindowTextW(h, sb, 256); return sb.ToString();
  }
}
"@
$catcher = New-Object System.Windows.Forms.Form
$catcher.Text = "Zaira click catcher"; $catcher.StartPosition = "Manual"
$catcher.Location = New-Object System.Drawing.Point(0, 0); $catcher.Size = New-Object System.Drawing.Size(3000, 1400)
$catcher.Show(); [System.Windows.Forms.Application]::DoEvents()
$f = Join-Path $UserDir "targets.txt"
for ($i = 0; $i -lt 100 -and -not (Test-Path $f); $i++) { Start-Sleep -Milliseconds 200 }
Start-Sleep -Seconds 2
$p = (Get-Content $f -Raw).Trim().Split(' ')
$cx = [int]$p[0]; $cy = [int]$p[1]; $bx = [int]$p[2]; $by = [int]$p[3]
"cat   ($cx,$cy): " + [H]::At($cx, $cy)
"bowl  ($bx,$by): " + [H]::At($bx, $by)
"empty ($($cx + 400),$cy): " + [H]::At($cx + 400, $cy)
"empty (40,40): " + [H]::At(40, 40)
$ov = [System.Diagnostics.Process]::GetProcesses() | Where-Object { $_.MainWindowTitle -eq "Zaira Desktop Pet" } | Select-Object -First 1
if ($ov) {
  $r = New-Object H+RECT; [void][H]::GetWindowRect($ov.MainWindowHandle, [ref]$r)
  $bmp = New-Object System.Drawing.Bitmap(($r.R - $r.L), ($r.B - $r.T), [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
  $ok = [H]::PrintWindow($ov.MainWindowHandle, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
  $bmp.Save($Out); "printwindow ok=$ok size=$($bmp.Width)x$($bmp.Height) -> $Out"
} else { "overlay window not found by title" }
$catcher.Close()
