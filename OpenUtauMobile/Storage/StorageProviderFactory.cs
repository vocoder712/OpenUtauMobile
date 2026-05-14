using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace OpenUtauMobile.Storage;

public static class StorageProviderFactory
{
    public static IStorageProvider? GetStorageProvider()
    {
        return Avalonia.Application.Current?.ApplicationLifetime switch
        {
            // 桌面多窗口应用
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktopLifetime =>
                desktopLifetime.MainWindow?.StorageProvider,
            // 移动单窗口应用
            Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime singleViewLifetime => TopLevel
                .GetTopLevel(singleViewLifetime.MainView)
                ?.StorageProvider,
            _ => null
        };
    }
}