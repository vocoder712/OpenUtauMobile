using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace OpenUtauMobile.Services;

/// <summary>
/// 基于 Avalonia 顶层窗口的跨平台剪贴板实现
/// </summary>
public sealed class AvaloniaClipboardService : IClipboardService
{
    public async Task<string?> GetTextAsync()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(GetTextAsync);
        }

        try
        {
            TopLevel? topLevel = AppService.GetTopLevel();
            IClipboard? clipboard = topLevel?.Clipboard;
            return clipboard == null ? null : await clipboard.TryGetTextAsync();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<bool> SetTextAsync(string text)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => SetTextAsync(text));
        }

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
