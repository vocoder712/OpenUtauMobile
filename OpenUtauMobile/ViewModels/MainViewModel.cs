using System;
using System.Collections.Generic;
using Avalonia.Threading;
using OpenUtauMobile.Messages;
using OpenUtauMobile.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public class MainViewModel : ViewModelBase
{
    [Reactive] public NavigateViewModelBase CurrentViewModel { get; set; }
    private readonly Stack<NavigateViewModelBase> _navigationStack = []; // 导航栈

    public MainViewModel()
    {
        MessageBus.Current.Listen<OpenFileMessage>().Subscribe(message =>
        {
            ToastService.Enqueue($"TODO: {message.FilePath}");
        });
        CurrentViewModel = new SplashScreenViewModel(this);
        _navigationStack.Push(CurrentViewModel);
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
        if (_navigationStack.Count > 1)
        {
            if (_navigationStack.Peek() != caller) // 确保调用者是当前视图模型
            {
                return;
            }

            // 弹出当前视图模型
            ViewModelBase popped = _navigationStack.Pop();
            // 释放资源
            if (popped is IDisposable disposable)
            {
                disposable.Dispose();
            }

            // 设置上一个视图模型为当前视图模型
            CurrentViewModel = _navigationStack.Peek();
            CurrentViewModel.OnNavigatedTo(); // 调用导航到新视图模型时的处理逻辑
        }
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
}