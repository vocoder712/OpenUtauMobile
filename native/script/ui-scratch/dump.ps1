Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$p = Get-Process OpenUtauMobile* | Where-Object MainWindowHandle -ne 0
$root = [System.Windows.Automation.AutomationElement]::FromHandle($p.MainWindowHandle)
Write-Output "window: $($root.Current.Name) at $([int]$root.Current.BoundingRectangle.X),$([int]$root.Current.BoundingRectangle.Y) $([int]$root.Current.BoundingRectangle.Width)x$([int]$root.Current.BoundingRectangle.Height)"
$btns = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
$i=0; foreach ($b in $btns) { $r=$b.Current.BoundingRectangle; if ($r.Right -gt $r.Left) { $i++; Write-Output ("b{0}: x={1} y={2} w={3} h={4} name='{5}'" -f $i,[int]$r.X,[int]$r.Y,[int]$r.Width,[int]$r.Height,$b.Current.Name) } }
Write-Output "--- texts ---"
$txts = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
foreach ($t in $txts) { $n=$t.Current.Name; $r=$t.Current.BoundingRectangle; if ($n -and $r.Right -gt $r.Left) { Write-Output "txt '$n' at $([int]$r.X),$([int]$r.Y)" } }
