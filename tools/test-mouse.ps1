# Drives the real mouse against a running `--selftest-mouse` session.
# A click-catcher window sits under the cat: clicks that pass through the overlay land there and are logged.
param([string]$UserDir = "$env:APPDATA\Godot\app_userdata\Zaira Desktop Pet")
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class M {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
  public static void Down() { mouse_event(0x02, 0, 0, 0, IntPtr.Zero); }
  public static void Up()   { mouse_event(0x04, 0, 0, 0, IntPtr.Zero); }
}
"@
$result = Join-Path $UserDir "mouse_result.txt"
Remove-Item $result -ErrorAction SilentlyContinue
function Log($m) { Add-Content $result ("{0:HH:mm:ss.fff} {1}" -f (Get-Date), $m) }

$form = New-Object System.Windows.Forms.Form
$form.Text = "Zaira click catcher"
$form.StartPosition = "Manual"
$form.Location = New-Object System.Drawing.Point(300, 1150)
$form.Size = New-Object System.Drawing.Size(2900, 242)
$form.BackColor = [System.Drawing.Color]::FromArgb(60, 70, 80)
$form.Add_MouseDown({ param($s, $e) Log ("CATCHER click at {0},{1}" -f [System.Windows.Forms.Cursor]::Position.X, [System.Windows.Forms.Cursor]::Position.Y) })
$form.Show()

function Targets {
  $f = Join-Path $UserDir "targets.txt"
  for ($i = 0; $i -lt 100 -and -not (Test-Path $f); $i++) { Start-Sleep -Milliseconds 200 }
  $p = (Get-Content $f -Raw).Trim().Split(' ')
  return @{ CatX = [int]$p[0]; CatY = [int]$p[1]; BowlX = [int]$p[2]; BowlY = [int]$p[3]; State = $p[4] }
}
function Pump($ms) { $end = (Get-Date).AddMilliseconds($ms); while ((Get-Date) -lt $end) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 15 } }
function MoveTo($x, $y) { [void][M]::SetCursorPos($x, $y); Pump 30 }

Pump 3500
$t = Targets; Log "targets $($t.CatX) $($t.CatY) bowl $($t.BowlX) $($t.BowlY) state $($t.State)"

# 1. grab, lift, spin, drop
MoveTo $t.CatX $t.CatY; Pump 600
[M]::Down(); Pump 150
for ($i = 1; $i -le 15; $i++) { MoveTo $t.CatX ($t.CatY - 10 * $i) }
for ($i = 1; $i -le 20; $i++) { MoveTo ($t.CatX + 8 * $i) ($t.CatY - 150) }
Pump 300
[M]::Up(); Log "released"
Pump 4000

# 2. stroke the cat
$t = Targets; Log "after drop: cat $($t.CatX) $($t.CatY) state $($t.State)"
for ($k = 0; $k -lt 6; $k++) {
  for ($i = -6; $i -le 6; $i++) { MoveTo ($t.CatX + 6 * $i) ($t.CatY + 5) }
  for ($i = 6; $i -ge -6; $i--) { MoveTo ($t.CatX + 6 * $i) ($t.CatY + 5) }
}
$t = Targets; Log "after stroking: state $($t.State)"

# 3. click on empty desktop: must reach the catcher
MoveTo ($t.CatX + 600) ($t.CatY + 10); Pump 400
[M]::Down(); Pump 60; [M]::Up(); Pump 400
Log "clicked empty spot at $($t.CatX + 600),$($t.CatY + 10)"

# 4. double-click the bowl: fills it
MoveTo $t.BowlX $t.BowlY; Pump 500
[M]::Down(); Pump 40; [M]::Up(); Pump 90; [M]::Down(); Pump 40; [M]::Up()
Pump 800
Log "double-clicked bowl"
MoveTo 20 20
Pump 2000
$form.Close()
Log "done"
