using System.ComponentModel;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Browser.Services;

/// <summary>将退出检查状态同步到浏览器，供页面卸载时直接读取。</summary>
internal sealed partial class BrowserPageLeaveGuard : ICmdSubscriber
{
    private readonly MainViewModel main;
    private NavigateViewModelBase? currentPage;
    private EditorViewModel? currentEditor;
    private bool refreshQueued;
    private bool lastConfirmation;

    internal BrowserPageLeaveGuard(MainViewModel main)
    {
        this.main = main;
        main.PropertyChanged += OnNavigationChanged;
        DocManager.Inst.AddSubscriber(this);
        UpdateWatchedPages();
    }

    /// <summary>编辑、加载、历史提交或保存完成后重新读取工程状态。</summary>
    public void OnNext(UCommand command, bool isUndo)
    {
        if (command is not UNotification or LoadProjectNotification or SaveProjectNotification or PreRenderNotification)
            QueueRefresh();
    }

    private void OnNavigationChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.ActiveEditor)) UpdateWatchedPages();
    }

    private void UpdateWatchedPages()
    {
        if (currentPage != null) currentPage.PropertyChanged -= OnPageStateChanged;
        if (currentEditor != null) currentEditor.PropertyChanged -= OnPageStateChanged;
        currentPage = main.CurrentViewModel is ISingerInstallationPage or SandboxFileManagerViewModel
            ? main.CurrentViewModel : null;
        currentEditor = main.ActiveEditor;
        if (currentPage != null) currentPage.PropertyChanged += OnPageStateChanged;
        if (currentEditor != null) currentEditor.PropertyChanged += OnPageStateChanged;
        QueueRefresh();
    }

    private void OnPageStateChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ISingerInstallationPage.IsBusy) or
            nameof(SandboxFileManagerViewModel.NeedsPersistence) or nameof(EditorViewModel.IsSavingProject))
            QueueRefresh();
    }

    /// <summary>待当前编辑事务结束，再读取已提交的撤销和保存状态。</summary>
    private void QueueRefresh()
    {
        if (refreshQueued) return;
        refreshQueued = true;
        Dispatcher.UIThread.Post(Refresh);
    }

    private async void Refresh()
    {
        refreshQueued = false;
        bool required = main.RequiresCloseConfirmation;
        if (required == lastConfirmation) return;
        lastConfirmation = required;
        await SetPageLeaveConfirmationAsync(required);
    }

    [JSImport("setPageLeaveConfirmation", "OpenUtauMobileFileSystem")]
    private static partial Task SetPageLeaveConfirmationAsync(bool required);
}
