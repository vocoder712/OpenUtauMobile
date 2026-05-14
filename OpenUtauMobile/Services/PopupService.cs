using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using DialogHostAvalonia;
using OpenUtauMobile.ViewModels;
using Serilog;

namespace OpenUtauMobile.Services;

/// <summary>
/// 封装的弹窗服务
/// </summary>
/// <remarks>支持泛型、mvvm绑定、关闭回调等特性</remarks>
public static class PopupService
{
    private const string MainDialogHostIdentifier = "MainDialogHost";

    public static async Task<T?> Show<T>(ContentControl view, PopupViewModelBase vm)
        => await RunOnUiThreadAsync(() => ShowWithViewCore<T>(view, vm, MainDialogHostIdentifier));

    private static async Task<T?> ShowWithViewCore<T>(ContentControl view, PopupViewModelBase vm, string? dialogIdentifier)
    {
        try
        {
            EventHandler<object?> handler = (_, parameter) => DialogHost.Close(dialogIdentifier, parameter);

            view.DataContext = vm;
            vm.ClosingEvent += handler;
            object? result = await DialogHost.Show(
                view,
                dialogIdentifier,
                openedEventHandler: null,
                closingEventHandler: (_, _) =>
                {
                    vm.ClosingEvent -= handler;
                });

            await Dispatcher.UIThread.InvokeAsync(() => { },
                DispatcherPriority.Send); // 解决弹窗库本身的一个异步时序竞态问题，强制把后续返回逻辑推到最低优先级，让弹窗彻底关闭

            switch (result)
            {
                case T typedResult:
                    return typedResult;
                case null:
                    return default;
                default:
                    Log.Error("弹窗返回了非预期类型的结果: {Result} (expected {ExpectedType})", result, typeof(T));
                    return default;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "弹窗发生异常");
            ToastService.Enqueue("弹窗发生异常，已记录日志");
            return default;
        }
    }

    /// <summary>
    /// 用ViewLocator 自动转的重载
    /// </summary>
    /// <param name="vm"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static async Task<T?> Show<T>(PopupViewModelBase vm)
        => await RunOnUiThreadAsync(() => ShowWithViewLocatorCore<T>(vm, MainDialogHostIdentifier));

    private static async Task<T?> ShowWithViewLocatorCore<T>(PopupViewModelBase vm, string? dialogIdentifier)
    {
        EventHandler<object?> handler = (_, parameter) => DialogHost.Close(dialogIdentifier, parameter);

        vm.ClosingEvent += handler;
        object? result = await DialogHost.Show(
            vm,
            dialogIdentifier,
            openedEventHandler: null,
            closingEventHandler: (_, _) =>
            {
                vm.ClosingEvent -= handler;
            });

        return result is T typed ? typed : default;
    }

    private static Task<T?> RunOnUiThreadAsync<T>(Func<Task<T?>> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return action();

        TaskCompletionSource<T?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                tcs.SetResult(await action());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, DispatcherPriority.Send);

        return tcs.Task;
    }
}