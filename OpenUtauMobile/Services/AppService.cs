using System;
using Avalonia.Controls;

namespace OpenUtauMobile.Services;

public static class AppService
{
    /// <summary>
    /// 获取当前TopLevel
    /// </summary>
    /// <returns></returns>
    public static TopLevel? GetTopLevel()
    {
        return TopLevel.GetTopLevel(Avalonia.Application.Current?.ApplicationLifetime switch
        {
            // 桌面多窗口应用
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktopLifetime =>
                desktopLifetime.MainWindow,
            // 移动单窗口应用
            Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime singleViewLifetime =>
                singleViewLifetime.MainView,
            _ => null
        });
    }

    /// <summary>
    /// 退出应用程序
    /// </summary>
    /// <returns>是否成功</returns>
    public static bool ExitApplication()
    {
        switch (Avalonia.Application.Current?.ApplicationLifetime)
        {
            case Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktopLifetime:
                desktopLifetime.Shutdown();
                return true;
            case Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime
                when !OperatingSystem.IsBrowser():
                Environment.Exit(0);
                return true;
            case Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime:
                return false; // 在浏览器环境中，无法直接退出应用
            default:
                return false;
        }
    }
}