# Test window for the self test: appears topmost, moves at MoveAt seconds, closes at CloseAt seconds.
# Default: a low window the cat can jump on. Tall: -Top 300 -Height 1000 (the cat has to climb a side).
param([int]$Left = 700, [int]$Top = 1000, [int]$Width = 900, [int]$Height = 300, [int]$MoveAt = 18, [int]$CloseAt = 26)
Add-Type -AssemblyName System.Windows.Forms
$form = New-Object System.Windows.Forms.Form
$form.Text = "Zaira test window"
$form.StartPosition = "Manual"
$form.Location = New-Object System.Drawing.Point($Left, $Top)
$form.Size = New-Object System.Drawing.Size($Width, $Height)
$form.TopMost = $true
$form.BackColor = [System.Drawing.Color]::FromArgb(40, 44, 52)
$start = Get-Date
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 200
$timer.Add_Tick({
    $t = ((Get-Date) - $start).TotalSeconds
    if ($t -ge $MoveAt -and $form.Left -eq $Left) { $form.Location = New-Object System.Drawing.Point(($Left + 150), ($Top - 50)) }
    if ($t -ge $CloseAt) { $form.Close() }
})
$timer.Start()
[void]$form.ShowDialog()
