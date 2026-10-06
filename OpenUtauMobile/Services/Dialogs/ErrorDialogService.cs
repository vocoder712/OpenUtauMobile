using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services.Dialogs;

/// <summary>
/// 全局错误弹窗服务，任意线程均可调用 <see cref="Show"/>。
/// 注册/注销由 MainView.OnAttachedToVisualTree / OnDetachedFromVisualTree 管理。
/// </summary>
public static class ErrorDialogService
{
    private static Func<ErrorDialogViewModel, Task>? _show;
    private static Action? _openDependencyManager;
    private static int _isReady; // 0 = 未注册, 1 = 已注册

    /// <summary>
    /// 注册弹窗回调（由 MainView 在 OnAttachedToVisualTree 中调用）。
    /// </summary>
    public static void Register(Func<ErrorDialogViewModel, Task> show, Action? openDependencyManager = null)
    {
        _show = show;
        _openDependencyManager = openDependencyManager;
        Interlocked.Exchange(ref _isReady, 1);
    }

    /// <summary>
    /// 注销弹窗回调（由 MainView 在 OnDetachedFromVisualTree 中调用）。
    /// </summary>
    public static void Unregister()
    {
        _show = null;
        _openDependencyManager = null;
        Interlocked.Exchange(ref _isReady, 0);
    }

    /// <summary>
    /// 在任意线程调用，将错误弹窗排队到 UI 线程显示。
    /// </summary>
    public static void Show(ErrorDialogViewModel vm)
    {
        if (_show == null) return;
        Func<ErrorDialogViewModel, Task> capture = _show;
        Dispatcher.UIThread.Post(() => _ = capture(vm));
    }

    public static void OpenDependencyManager()
    {
        Action? navigate = _openDependencyManager;
        if (navigate == null) return;
        // 先处理弹窗关闭，再由仍在挂载的宿主导航；直接打开的错误弹窗也复用此入口。
        Dispatcher.UIThread.Post(() =>
        {
            if (_openDependencyManager == navigate) navigate();
        }, DispatcherPriority.Background);
    }
}
