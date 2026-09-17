using OpenUtauMobile.Services.Dialogs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtauMobile.Services;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels;

public class MainViewModel : ViewModelBase
{
    [Reactive] public NavigateViewModelBase CurrentViewModel { get; set; }
    private readonly Stack<NavigateViewModelBase> _navigationStack = []; // 导航栈
    private bool _externalProjectOpeningReady;
    private bool _externalProjectDrainActive;

    public MainViewModel()
    {
        CurrentViewModel = new SplashScreenViewModel(this);
        _navigationStack.Push(CurrentViewModel);
        UpdatePlatformDisplayState();
        ExternalProjectOpenService.RegisterConsumer(RequestExternalProjectDrain);
        // 在UI线程上调用OnNavigatedTo
        Dispatcher.UIThread.Post(() =>
        {
            CurrentViewModel.OnNavigatedTo(); // 调用导航到新视图模型时的处理逻辑
        });
    }

    /// <summary>
    /// 导航到指定的视图模型
    /// </summary>
    /// <param name="vm">目标页面实例</param>
    public void Navigate(NavigateViewModelBase vm)
    {
        Dispatcher.UIThread.InvokeAsync(() => OnNavigate(vm));
    }

    private void OnNavigate(NavigateViewModelBase vm)
    {
        CurrentViewModel = vm;
        _navigationStack.Push(vm);
        UpdatePlatformDisplayState();
        CurrentViewModel.OnNavigatedTo(); // 调用导航到新视图模型时的处理逻辑
    }

    /// <summary>
    /// 导航回上一个视图模型
    /// </summary>
    /// <param name="caller">调用者视图模型</param>
    public void NavigateBack(NavigateViewModelBase caller)
    {
        // 在UI线程上调用OnNavigateBack
        Dispatcher.UIThread.InvokeAsync(() => OnNavigateBack(caller));
    }

    private void OnNavigateBack(NavigateViewModelBase caller)
    {
        TryRemoveCurrentViewModel(caller, notifyRevealedViewModel: true);
    }

    private bool TryRemoveCurrentViewModel(NavigateViewModelBase caller, bool notifyRevealedViewModel)
    {
        if (_navigationStack.Count <= 1 || _navigationStack.Peek() != caller)
        {
            return false;
        }

        ViewModelBase popped = _navigationStack.Pop();
        if (popped is IDisposable disposable)
        {
            disposable.Dispose();
        }

        CurrentViewModel = _navigationStack.Peek();
        UpdatePlatformDisplayState();
        if (notifyRevealedViewModel)
        {
            CurrentViewModel.OnNavigatedTo();
        }

        return true;
    }

    /// <summary>
    /// 完成启动导航，并允许处理启动期间收到的外部工程请求。
    /// </summary>
    public void CompleteStartup(HomeViewModel homeViewModel)
    {
        void Complete()
        {
            OnNavigate(homeViewModel);
            _externalProjectOpeningReady = true;
            RequestExternalProjectDrain();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Complete();
        }
        else
        {
            Dispatcher.UIThread.Post(Complete);
        }
    }

    private void RequestExternalProjectDrain()
    {
        Dispatcher.UIThread.Post(() => _ = DrainExternalProjectItemsAsync());
    }

    private async Task DrainExternalProjectItemsAsync()
    {
        if (!_externalProjectOpeningReady || _externalProjectDrainActive)
        {
            return;
        }

        _externalProjectDrainActive = true;
        try
        {
            while (ExternalProjectOpenService.TryDequeue(out ExternalProjectOpenItem? item))
            {
                try
                {
                    switch (item)
                    {
                        case ExternalProjectOpenRequest request:
                            await OpenExternalProjectAsync(request);
                            break;
                        case ExternalProjectOpenFailure failure:
                            ShowExternalProjectError(failure.Exception);
                            break;
                    }
                }
                catch (Exception exception)
                {
                    Log.Error(exception, "Failed to handle external project request");
                    ShowExternalProjectError(exception);
                }
            }
        }
        finally
        {
            _externalProjectDrainActive = false;
            if (_externalProjectOpeningReady && ExternalProjectOpenService.HasPendingItems)
            {
                RequestExternalProjectDrain();
            }
        }
    }

    private async Task OpenExternalProjectAsync(ExternalProjectOpenRequest request)
    {
        bool sourceTransferred = false;
        try
        {
            if (CurrentViewModel is EditorViewModel currentEditor)
            {
                bool canExit = await currentEditor.ConfirmExitAsync();
                if (!canExit || CurrentViewModel != currentEditor)
                {
                    return;
                }

                // 替换编辑器时不激活栈中页面，避免它在下一帧启动后台工作。
                if (!TryRemoveCurrentViewModel(currentEditor, notifyRevealedViewModel: false))
                {
                    return;
                }
            }

            EditorViewModel editor = new(this, new ProjectOpenOptions(
                request.LocalPath,
                ProjectOpenKind.ExternalCopy,
                request.DeleteSourceAfterRead));
            OnNavigate(editor);
            sourceTransferred = true;
            await editor.ProjectLoadCompletion;
        }
        finally
        {
            if (!sourceTransferred)
            {
                DeleteExternalProjectCache(request);
            }
        }
    }

    private static void DeleteExternalProjectCache(ExternalProjectOpenRequest request)
    {
        if (!request.DeleteSourceAfterRead || string.IsNullOrEmpty(request.LocalPath))
        {
            return;
        }

        try
        {
            File.Delete(request.LocalPath);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to delete unused external project cache {Path}", request.LocalPath);
        }
    }

    private static void ShowExternalProjectError(Exception exception)
    {
        ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(exception)));
    }

    /// <summary>将当前页面类型同步给平台显示服务。</summary>
    private void UpdatePlatformDisplayState()
    {
        ServiceHub.PlatformDisplayService?.SetEditorActive(CurrentViewModel is EditorViewModel);
    }

    public void OnBackRequested()
    {
        if (_navigationStack.Count > 1)
        {
            CurrentViewModel.OnBackRequested(); // 调用当前视图模型的返回事件处理逻辑
        }
        else // 没有上一页了，退出应用
        {
            _ = AppService.ExitApplication();
        }
    }

    /// <summary>
    /// 确认桌面窗口是否可以关闭。
    /// </summary>
    /// <returns>当前工程允许退出时返回 true。</returns>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (CurrentViewModel is EditorViewModel editorViewModel)
        {
            return await editorViewModel.ConfirmExitAsync();
        }

        return true;
    }
}
