param([string]$Out = "shot.png", [int]$Monitor = 0)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing -ErrorAction Stop
$dir = Split-Path -Parent $Out
if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
$screens = [System.Windows.Forms.Screen]::AllScreens
if ($Monitor -ge $screens.Length) { throw "Monitor $Monitor not available ($($screens.Length) screens)" }
$b = $screens[$Monitor].Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "shot -> $Out ($((Get-Item $Out).Length) bytes)"
