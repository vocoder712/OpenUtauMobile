using System;
using System.Threading;
using Avalonia.Threading;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 通用后台操作加载弹窗 ViewModel
/// </summary>
/// <remarks>
/// 由 <see cref="OpenUtauMobile.Services.LoadingPopupService"/> 创建并管理生命周期。
/// 后台任务可使用本类型更新进度或提示文本，不应直接持有或显示对应 View。
/// </remarks>
public class LoadingPopupViewModel : PopupViewModelBase, IProgress<double>
{
    private const double MinimumProgress = 0d;
    private const double MaximumProgress = 100d;
    private int _isClosed;

    /// <summary>当前后台操作说明</summary>
    [Reactive]
    public string Message { get; private set; }

    /// <summary>当前进度，范围为 0–100</summary>
    [Reactive]
    public double Progress { get; private set; }

    /// <summary>是否使用无确定进度的加载状态</summary>
    [Reactive]
    public bool IsIndeterminate { get; private set; }

    /// <summary>用于界面显示的进度文本</summary>
    public string ProgressText => $"{Progress:0}%";

    /// <param name="message">后台操作说明</param>
    public LoadingPopupViewModel(string message)
    {
        Message = NormalizeMessage(message);
        IsIndeterminate = true;
    }

    /// <param name="message">后台操作说明</param>
    /// <param name="progress">初始进度，超出 0–100 时自动截断</param>
    public LoadingPopupViewModel(string message, double progress)
    {
        Message = NormalizeMessage(message);
        Progress = NormalizeProgress(progress);
        IsIndeterminate = false;
    }

    /// <summary>报告确定进度</summary>
    public void Report(double value)
    {
        UpdateProgress(value);
    }

    /// <summary>更新确定进度和可选操作说明</summary>
    public void UpdateProgress(double progress, string? message = null)
    {
        RunOnUiThread(() =>
        {
            Progress = NormalizeProgress(progress);
            IsIndeterminate = false;
            if (message != null)
            {
                Message = NormalizeMessage(message);
            }

            this.RaisePropertyChanged(nameof(ProgressText));
        });
    }

    /// <summary>切换为无确定进度的加载状态</summary>
    public void SetIndeterminate(string? message = null)
    {
        RunOnUiThread(() =>
        {
            IsIndeterminate = true;
            if (message != null)
            {
                Message = NormalizeMessage(message);
            }
        });
    }

    /// <summary>更新后台操作说明</summary>
    public void UpdateMessage(string message)
    {
        RunOnUiThread(() => Message = NormalizeMessage(message));
    }

    /// <summary>由后台操作完成方关闭弹窗</summary>
    public void Close()
    {
        if (Interlocked.Exchange(ref _isClosed, 1) != 0)
        {
            return;
        }

        RunOnUiThread(() => RaiseClose(null), allowAfterClose: true);
    }

    /// <summary>后台操作期间忽略返回请求</summary>
    public override void RequestBack()
    {
    }

    private void RunOnUiThread(Action action, bool allowAfterClose = false)
    {
        if (!allowAfterClose && Volatile.Read(ref _isClosed) != 0)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (allowAfterClose || Volatile.Read(ref _isClosed) == 0)
            {
                action();
            }
        });
    }

    private static string NormalizeMessage(string message)
    {
        return string.IsNullOrWhiteSpace(message)
            ? L.S("LoadingPopup.DefaultMessage")
            : message;
    }

    private static double NormalizeProgress(double progress)
    {
        return double.IsFinite(progress)
            ? Math.Clamp(progress, MinimumProgress, MaximumProgress)
            : MinimumProgress;
    }
}
