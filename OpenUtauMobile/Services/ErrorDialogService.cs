using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services;

/// <summary>
/// 全局错误弹窗服务，任意线程均可调用 <see cref="Show"/>。
/// 注册/注销由 MainView.OnAttachedToVisualTree / OnDetachedFromVisualTree 管理。
/// </summary>
public static class ErrorDialogService
{
    private static Func<ErrorDialogViewModel, Task>? _show;
    private static int _isReady; // 0 = 未注册, 1 = 已注册

    /// <summary>
    /// 注册弹窗回调（由 MainView 在 OnAttachedToVisualTree 中调用）。
    /// </summary>
    public static void Register(Func<ErrorDialogViewModel, Task> show)
    {
        _show = show;
        Interlocked.Exchange(ref _isReady, 1);
    }

    /// <summary>
    /// 注销弹窗回调（由 MainView 在 OnDetachedFromVisualTree 中调用）。
    /// </summary>
    public static void Unregister()
    {
        _show = null;
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
}