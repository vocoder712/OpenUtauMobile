param([int]$X, [int]$Y, [int]$WaitMs = 1500)
$sig = @'
using System;
using System.Runtime.InteropServices;
public static class Clicker {
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr x);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }
}
'@
Add-Type -TypeDefinition $sig
[Clicker]::Click($X, $Y)
Write-Output "clicked $X,$Y"
Start-Sleep -Milliseconds $WaitMs
