using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace OpenUtauMobile.Services;

/// <summary>
/// 基于 Avalonia 顶层窗口的跨平台剪贴板实现
/// </summary>
public sealed class AvaloniaClipboardService : IClipboardService
{
    public async Task<bool> SetTextAsync(string text)
    {
        try
        {
            TopLevel? topLevel = AppService.GetTopLevel();
            IClipboard? clipboard = topLevel?.Clipboard;
            if (clipboard == null)
            {
                return false;
            }

            await clipboard.SetTextAsync(text);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
