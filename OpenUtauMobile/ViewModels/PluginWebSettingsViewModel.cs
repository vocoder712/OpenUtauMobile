using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Plugin.Abstractions;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Platform;
using OpenUtauMobile.Services.SingerPlugins;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels;

/// <summary>独立网页设置页面，拥有一次插件会话和取消令牌。</summary>
public sealed class PluginWebSettingsViewModel : NavigateViewModelBase, IDisposable
{
    private readonly RegisteredSingerPlugin plugin;
    private readonly CancellationTokenSource lifetime = new();
    private IPluginWebSettingsSession? session;
    private Task opening = Task.CompletedTask;
    private Task? closing;
    private bool started;
    private bool disposed;
    public string Title { get; }
    [Reactive] public Uri? Address { get; private set; }
    [Reactive] public bool IsBusy { get; private set; } = true;
    [Reactive] public string Error { get; private set; } = "";
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenBrowserCommand { get; }

    public PluginWebSettingsViewModel(MainViewModel navigator, RegisteredSingerPlugin plugin, string title) : base(navigator)
    {
        this.plugin = plugin;
        Title = title;
        BackCommand = ReactiveCommand.Create(OnBackRequested);
        OpenBrowserCommand = ReactiveCommand.CreateFromTask(OpenBrowserAsync, this.WhenAnyValue(vm => vm.Address).Select(value => value != null));
    }

    public override void OnNavigatedTo()
    {
        if (!started) { started = true; opening = OpenAsync(); }
    }

    private async Task OpenAsync()
    {
        try
        {
            session = await PluginWebSettingsService.OpenAsync(plugin, lifetime.Token);
            if (!lifetime.IsCancellationRequested) Address = session.Address;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to open plugin web settings {PluginId}", plugin.PluginId);
            if (!disposed) Error = L.S("PluginWebSettings.OpenFailed");
        }
        finally { if (!disposed) IsBusy = false; }
    }

    private async Task OpenBrowserAsync()
    {
        if (Address == null) return;
        ExternalUrlLaunchResult result = await ExternalUrlService.OpenAsync(Address.AbsoluteUri);
        if (!result.Succeeded) Error = L.S("PluginWebSettings.BrowserFailed");
    }

    /// <summary>系统 WebView 不可用时保留外部浏览器入口。</summary>
    public void ReportWebViewFailure() => Error = L.S("PluginWebSettings.WebViewFailed");

    public override void OnBackRequested() => _ = LeaveAsync();

    private async Task LeaveAsync()
    {
        await CloseAsync();
        if (!disposed) Navigator.NavigateBack(this);
    }

    /// <summary>关闭任务只创建一次，等服务释放后再刷新资源。</summary>
    private Task CloseAsync() => closing ??= CloseCoreAsync();

    private async Task CloseCoreAsync()
    {
        lifetime.Cancel();
        await opening;
        IPluginWebSettingsSession? current = session;
        session = null;
        if (current == null) return;
        bool refresh = current.ReloadResourcesOnClose;
        try { await current.DisposeAsync(); }
        catch (Exception exception) { Log.Error(exception, "Failed to close plugin web settings {PluginId}", plugin.PluginId); }
        try
        {
            await ServiceHub.FlushFileSystemAsync();
            if (refresh) await SingerDiscoveryService.Instance.RefreshAsync();
        }
        catch (Exception exception) { Log.Error(exception, "Failed to refresh resources after plugin settings {PluginId}", plugin.PluginId); }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        _ = FinishDisposeAsync();
        BackCommand.Dispose();
        OpenBrowserCommand.Dispose();
    }

    private async Task FinishDisposeAsync()
    {
        try { await CloseAsync(); }
        finally { lifetime.Dispose(); }
    }
}
