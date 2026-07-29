using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtauMobile.Controls;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services;

/// <summary>
/// 在通用加载弹窗中运行后台操作
/// </summary>
/// <remarks>
/// 适用于安装、导入、导出等需要阻止用户继续操作，且通常无法立即完成的后台任务。
/// 短暂操作或不阻塞交互的状态提示应优先使用 Toast 或页面内加载状态。
///
/// 无法获取进度时调用 <see cref="RunAsync(string, Func{LoadingPopupViewModel, Task})"/>；
/// 可获取 0–100 进度时调用带初始进度的重载，并通过
/// <see cref="LoadingPopupViewModel.UpdateProgress"/> 持续更新进度和说明。
///
/// Service 会先显示弹窗，再运行任务，并在任务成功或抛出异常时关闭弹窗。
/// 异常会在弹窗完全关闭后继续向调用方传播，因此成功提示和错误弹窗应由调用方在
/// <c>await RunAsync(...)</c> 之后处理。调用方不应自行关闭 LoadingPopup。
/// </remarks>
/// <example>
/// <code>
/// await LoadingPopupService.RunAsync(message, _ => InstallAsync());
///
/// await LoadingPopupService.RunAsync(message, 0d, async loading =>
/// {
///     loading.UpdateProgress(50d, nextMessage);
///     await ExportAsync();
/// });
/// </code>
/// </example>
public static class LoadingPopupService
{
    /// <summary>使用无确定进度的加载弹窗运行后台操作</summary>
    public static async Task RunAsync(
        string message,
        Func<LoadingPopupViewModel, Task> operation)
    {
        LoadingPopupViewModel viewModel = new(message);
        await RunAsync(viewModel, operation);
    }

    /// <summary>使用确定进度的加载弹窗运行后台操作</summary>
    public static async Task RunAsync(
        string message,
        double initialProgress,
        Func<LoadingPopupViewModel, Task> operation)
    {
        LoadingPopupViewModel viewModel = new(message, initialProgress);
        await RunAsync(viewModel, operation);
    }

    private static async Task RunAsync(
        LoadingPopupViewModel viewModel,
        Func<LoadingPopupViewModel, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Task<object?> popupTask = PopupService.Show<object>(
            new LoadingPopup(),
            viewModel);

        // 先让弹窗完成布局和首帧渲染，再启动可能包含同步阶段的后台操作。
        await Dispatcher.UIThread.InvokeAsync(
            () => { },
            DispatcherPriority.Background);

        try
        {
            await operation(viewModel);
        }
        finally
        {
            viewModel.Close();
            await popupTask;
        }
    }
}
