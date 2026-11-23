using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using OpenUtauMobile.Services.Projects;
using Serilog;
using OpenUtau.Core;
using OpenUtau.Core.Pipeline;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services;

public static class AppService
{
    /// <summary>
    /// 获取当前TopLevel
    /// </summary>
    /// <returns></returns>
    public static TopLevel? GetTopLevel()
    {
        if (ServiceHub.DesktopWindowContext?.ActiveTopLevel is { } active) return active;
        return TopLevel.GetTopLevel(Avalonia.Application.Current?.ApplicationLifetime switch
        {
            // 桌面多窗口应用
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktopLifetime =>
                desktopLifetime.MainWindow,
            // Android 活动生命周期
            Avalonia.Controls.ApplicationLifetimes.IActivityApplicationLifetime =>
                App.ActivityMainView,
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
            case Avalonia.Controls.ApplicationLifetimes.IActivityApplicationLifetime:
                _ = FlushAndExitAsync();
                return true;
            case Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime
                when !OperatingSystem.IsBrowser():
                _ = FlushAndExitAsync();
                return true;
            case Avalonia.Controls.ApplicationLifetimes.ISingleViewApplicationLifetime:
                return false; // 在浏览器环境中，无法直接退出应用
            default:
                return false;
        }
    }

    /// <summary>退出前异步刷新合并的偏好请求，主线程继续处理平台输入。</summary>
    private static async Task FlushAndExitAsync()
    {
        UProject project = DocManager.Inst.Project;
        DocRevision revision = DocManager.Inst.Revision;
        try { await PreferencesWriter.Inst.FlushAsync(); }
        catch (Exception error) { Log.Error(error, "退出时刷新偏好失败"); }
        if (!ReferenceEquals(project, DocManager.Inst.Project) || revision != DocManager.Inst.Revision) return;
        if (ServiceHub.PlatformExitApplication is { } exitApplication)
        {
            exitApplication();
        }
        else if (!OperatingSystem.IsAndroid())
        {
            Environment.Exit(0);
        }
    }
}
