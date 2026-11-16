using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Services.SingerPlugins;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels;

/// <summary>本地化后的插件详情条目。</summary>
public sealed record PluginDetailItem(string Label, string Value);

/// <summary>插件详情导航页，串行处理启用、配置、数据清除和卸载操作。</summary>
public sealed class PluginDetailsViewModel : NavigateViewModelBase, IDisposable
{
    private readonly string relativePath;
    private readonly CancellationTokenSource lifetime = new();
    private RegisteredSingerPlugin? plugin;
    private PluginInformation? information;
    private bool enabled;
    private bool disposed;

    [Reactive] public string Title { get; private set; }
    [Reactive] public bool IsBusy { get; private set; }
    [Reactive] public string ErrorMessage { get; private set; } = "";
    /// <summary>默认仅展示版本、身份、作者和签名状态，其余元数据由详情展开区承载。</summary>
    [Reactive] public IReadOnlyList<PluginDetailItem> SummaryMetadata { get; private set; } = [];
    /// <summary>展开后接在关键项之后展示的其余信息，不重复摘要内容。</summary>
    [Reactive] public IReadOnlyList<PluginDetailItem> AdditionalMetadata { get; private set; } = [];
    /// <summary>卡片底部的箭头控制其余信息是否可见。</summary>
    [Reactive] public bool IsMetadataExpanded { get; set; }
    [Reactive] public IReadOnlyList<PluginDetailItem> Capabilities { get; private set; } = [];
    [Reactive] public bool HasWebSettings { get; private set; }
    [Reactive] public string SettingsDescription { get; private set; } = "";
    [Reactive] public string Status { get; private set; } = "";
    [Reactive] public string DataDirectory { get; private set; } = "";
    [Reactive] public bool IsPendingDeletion { get; private set; }
    [Reactive] public bool IsPendingDataClear { get; private set; }
    [Reactive] public bool HasRegistration { get; private set; }

    /// <summary>开关只在名单持久化成功后发布新值，保存失败时恢复已提交状态。</summary>
    public bool IsPluginEnabled
    {
        get => enabled;
        set
        {
            if (value != enabled && CanChangeEnabled) _ = ChangeEnabledAsync(value);
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    /// <summary>持久化和待卸载期间不允许再次变更启用状态。</summary>
    public bool CanChangeEnabled => !IsBusy && !IsPendingDeletion;
    /// <summary>只有已加载并可用的配置能力能够打开，排队清除数据后暂停配置。</summary>
    public bool CanOpenSettings => !IsBusy && HasWebSettings && enabled && !IsPendingDeletion && !IsPendingDataClear;
    /// <summary>数据目录必须对应已成功登记的稳定身份。</summary>
    public bool CanManageData => !IsBusy && HasRegistration;
    public string DataClearLabel => L.S(IsPendingDataClear ? "PluginDetails.CancelDataClear" : "PluginDetails.ClearData");
    public string UninstallLabel => L.S(IsPendingDeletion ? "PluginManager.CancelDeletion" : "Common.Uninstall");
    public string DataDescription => L.S(IsPendingDataClear ? "PluginDetails.DataClearPending" : "PluginDetails.ClearDataDescription");

    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyDataDirectoryCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearDataCommand { get; }
    public ReactiveCommand<Unit, Unit> UninstallCommand { get; }

    /// <summary>按文件相对路径建立页面，不为打开详情加载插件程序集。</summary>
    public PluginDetailsViewModel(MainViewModel navigator, string relativePath) : base(navigator)
    {
        this.relativePath = relativePath;
        Title = Path.GetFileName(relativePath);
        IObservable<bool> idle = this.WhenAnyValue(model => model.IsBusy, busy => !busy);
        BackCommand = ReactiveCommand.Create(OnBackRequested, idle);
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync, idle);
        OpenSettingsCommand = ReactiveCommand.CreateFromTask(OpenSettingsAsync, this.WhenAnyValue(model => model.CanOpenSettings));
        CopyDataDirectoryCommand = ReactiveCommand.CreateFromTask(CopyDataDirectoryAsync, this.WhenAnyValue(model => model.CanManageData));
        ClearDataCommand = ReactiveCommand.CreateFromTask(ClearDataAsync, this.WhenAnyValue(model => model.CanManageData));
        UninstallCommand = ReactiveCommand.CreateFromTask(UninstallAsync, idle);
    }

    /// <summary>从配置页返回时重新查询动态能力和已提交状态。</summary>
    public override void OnNavigatedTo() => _ = RefreshAsync();

    /// <summary>持久化或弹窗进行期间阻止返回，避免操作失去页面归属。</summary>
    public override void OnBackRequested()
    {
        if (!IsBusy) base.OnBackRequested();
    }

    private Task RefreshAsync() => RunAsync(async () =>
    {
        information = await Task.Run(() => PluginInformationService.Read(relativePath), lifetime.Token);
        if (disposed) return;
        Title = information.Name;
        plugin = PluginWebSettingsService.FindPlugin(relativePath);
        HasRegistration = information.Registration != null;
        DataDirectory = information.Registration is { } registration
            ? PluginWebSettingsService.Storage(registration.Id).DataDirectory : "";
        List<PluginDetailItem> metadata = [.. information.Metadata.Select(item => new PluginDetailItem(L.S(item.LabelKey), item.Value))];
        if (information.Registration is { } identity)
            metadata.Insert(0, new(L.S("PluginDetails.Id"), identity.Id));
        string versionKey = information.Metadata.Any(item => item.LabelKey == "PluginDetails.ProductVersion")
            ? "PluginDetails.ProductVersion" : "PluginDetails.Version";
        List<PluginDetailItem> summary = [.. information.Metadata
            .Where(item => item.LabelKey == versionKey || item.LabelKey is "PluginDetails.Author" or "PluginDetails.StrongName")
            .Select(item => new PluginDetailItem(L.S(item.LabelKey), item.Value))];
        if (information.Registration is { } summaryIdentity)
            summary.Insert(0, new(L.S("PluginDetails.Id"), summaryIdentity.Id));
        SummaryMetadata = summary;
        AdditionalMetadata = [.. metadata.Except(summary)];
        Capabilities = [.. information.StaticCapabilities.Concat(information.Registration?.Capabilities ?? [])
            .Select(item => new PluginDetailItem(L.S(item.LabelKey), item.Value))];
        HasWebSettings = false;
        SettingsDescription = L.S("PluginDetails.SettingsUnavailable");
        if (plugin != null)
        {
            try
            {
                using CancellationTokenSource query = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                query.CancelAfter(TimeSpan.FromSeconds(5));
                var descriptor = await Task.Run(async () => await PluginWebSettingsService.QueryAsync(plugin, query.Token), query.Token)
                    .WaitAsync(query.Token);
                if (disposed) return;
                HasWebSettings = descriptor != null;
                SettingsDescription = descriptor?.Title ?? L.S("PluginDetails.SettingsUnavailable");
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { throw; }
            catch (Exception error) { Log.Debug(error, "Failed to query plugin settings {Plugin}", relativePath); }
        }
    });

    private Task ChangeEnabledAsync(bool value) => RunAsync(async () =>
    {
        await PluginActivationService.SetEnabledAsync(relativePath, value);
        await ServiceHub.FlushFileSystemAsync();
    });

    private Task OpenSettingsAsync() => RunAsync(async () =>
    {
        if (plugin == null || !CanUseSettings()) return;
        using CancellationTokenSource query = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        query.CancelAfter(TimeSpan.FromSeconds(5));
        var descriptor = await Task.Run(async () => await PluginWebSettingsService.QueryAsync(plugin, query.Token), query.Token)
            .WaitAsync(query.Token);
        if (disposed) return;
        if (descriptor == null)
        {
            HasWebSettings = false;
            SettingsDescription = L.S("PluginDetails.SettingsUnavailable");
            return;
        }
        Navigator.Navigate(new PluginWebSettingsViewModel(Navigator, plugin,
            string.IsNullOrWhiteSpace(descriptor.Title) ? Title : descriptor.Title));
    });

    private bool CanUseSettings() => enabled && !IsPendingDeletion && !IsPendingDataClear;

    private Task CopyDataDirectoryAsync() => RunAsync(async () =>
    {
        if (string.IsNullOrEmpty(DataDirectory)) return;
        if (!await ServiceHub.ClipboardService.SetTextAsync(DataDirectory))
            throw new IOException(L.S("ErrorDialog.CopyFailed"));
        ToastService.Enqueue(L.S("PluginDetails.PathCopied"));
    });

    private Task ClearDataAsync() => RunAsync(async () =>
    {
        if (information?.Registration is not { } registration) return;
        bool clear = !IsPendingDataClear;
        if (clear && !await ConfirmAsync("PluginDetails.ClearData", "PluginDetails.ClearDataQuestion")) return;
        await PluginDataService.SetPendingAsync(registration.Id, clear);
        await ServiceHub.FlushFileSystemAsync();
    });

    private Task UninstallAsync() => RunAsync(async () =>
    {
        if (IsPendingDeletion) await PluginManagementService.CancelDeletionAsync(relativePath);
        else
        {
            if (!await ConfirmAsync("Common.Uninstall", "PluginDetails.UninstallQuestion")) return;
            await PluginManagementService.ScheduleDeletionAsync(relativePath);
        }
        await ServiceHub.FlushFileSystemAsync();
    });

    private async Task<bool> ConfirmAsync(string titleKey, string messageKey)
    {
        string? result = await OptionConfirmPopupService.ShowAsync(L.S(titleKey), string.Format(L.S(messageKey), Title),
        [
            new OptionConfirmOption(L.S("Common.Cancel"), "cancel", isDefault: true),
            new OptionConfirmOption(L.S(titleKey), "confirm", isDestructive: true),
        ]);
        return result == "confirm";
    }

    /// <summary>统一操作归属和错误反馈；操作结束后从已提交偏好重建页面状态。</summary>
    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy || disposed) return;
        IsBusy = true;
        NotifyState();
        ErrorMessage = "";
        this.RaisePropertyChanged(nameof(HasError));
        try { await action(); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            Log.Error(error, "Plugin details operation failed {Plugin}", relativePath);
            if (!disposed) ErrorMessage = string.Format(L.S("PluginDetails.OperationFailed"), error.Message);
        }
        finally
        {
            if (!disposed)
            {
                UpdateState();
                IsBusy = false;
                NotifyState();
            }
        }
    }

    /// <summary>从已提交偏好和启动快照恢复开关、待执行任务及加载状态。</summary>
    private void UpdateState()
    {
        enabled = PluginActivationService.IsEnabled(relativePath);
        IsPendingDeletion = Preferences.Default.PendingPluginDeletions.Contains(relativePath, PluginActivationService.PathComparer);
        IsPendingDataClear = information?.Registration is { } registration && PluginDataService.IsPending(registration.Id);
        bool loaded = information != null && AppDomain.CurrentDomain.GetAssemblies()
            .Any(assembly => !assembly.IsDynamic && assembly.ManifestModule.ModuleVersionId == information.ModuleId);
        Status = L.S(IsPendingDeletion ? "PluginManager.PendingDeletion"
            : PluginActivationService.ShouldLoadFile(PluginManagementService.ResolvePluginPath(relativePath)) != enabled
                ? "PluginDetails.RestartRequired"
                : loaded ? "PluginDetails.Loaded" : enabled ? "PluginDetails.NotLoaded" : "PluginDetails.Disabled");
        this.RaisePropertyChanged(nameof(IsPluginEnabled));
    }

    /// <summary>显式刷新依赖业务条件的命令可用性和操作标签。</summary>
    private void NotifyState()
    {
        this.RaisePropertyChanged(nameof(HasError));
        this.RaisePropertyChanged(nameof(CanChangeEnabled));
        this.RaisePropertyChanged(nameof(CanOpenSettings));
        this.RaisePropertyChanged(nameof(CanManageData));
        this.RaisePropertyChanged(nameof(DataClearLabel));
        this.RaisePropertyChanged(nameof(DataDescription));
        this.RaisePropertyChanged(nameof(UninstallLabel));
    }

    /// <summary>取消离开页面后尚未完成的设置查询并释放命令。</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        BackCommand.Dispose(); RefreshCommand.Dispose(); OpenSettingsCommand.Dispose();
        CopyDataDirectoryCommand.Dispose(); ClearDataCommand.Dispose(); UninstallCommand.Dispose();
        lifetime.Dispose();
    }
}
