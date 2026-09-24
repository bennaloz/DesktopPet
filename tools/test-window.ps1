# Test window for the self test: appears topmost at 700,1000, moves at ~18 s, closes at ~26 s.
Add-Type -AssemblyName System.Windows.Forms
$form = New-Object System.Windows.Forms.Form
$form.Text = "Zaira test window"
$form.StartPosition = "Manual"
$form.Location = New-Object System.Drawing.Point(700, 1000)
$form.Size = New-Object System.Drawing.Size(900, 300)
$form.TopMost = $true
$form.BackColor = [System.Drawing.Color]::FromArgb(40, 44, 52)
$start = Get-Date
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 200
$timer.Add_Tick({
    $t = ((Get-Date) - $start).TotalSeconds
    if ($t -ge 18 -and $form.Left -eq 700) { $form.Location = New-Object System.Drawing.Point(850, 950) }
    if ($t -ge 26) { $form.Close() }
})
$timer.Start()
[void]$form.ShowDialog()
