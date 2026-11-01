using System;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Services.SingerPlugins;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>插件下拉选项，空注册表示明确的“未选择”。</summary>
public sealed record SettingsPluginOption(RegisteredPluginSettings? Registration)
{
    public bool IsUnselected => Registration == null;
    public string DisplayName => Registration?.Descriptor.DisplayName ?? "";
}

/// <summary>插件设置由设置页持有，切换分类保留当前字段实例。</summary>
public partial class SettingsViewModel
{
    private SettingsPluginOption selectedPlugin = null!;
    private bool switchingPlugin;
    private bool leavingSettings;
    public ImmutableArray<SettingsPluginOption> PluginOptions { get; private set; } = [];
    [Reactive] public PluginSettingsViewModel? PluginSettings { get; private set; }
    public bool IsPluginTab => SelectedCategory == SettingsCategory.Plugins;
    public bool HasSelectedPlugin => PluginSettings != null;
    public bool CanSelectPlugin => !switchingPlugin && PluginSettings?.IsBusy != true && PluginSettings?.IsSaving != true;

    /// <summary>选项变更等待实时保存，操作忙碌时恢复原选择。</summary>
    public SettingsPluginOption SelectedPlugin
    {
        get => selectedPlugin;
        set
        {
            if (value != null && value != selectedPlugin) _ = SelectPluginAsync(value);
        }
    }

    private void InitializePluginSelection()
    {
        PluginOptions = [new(null), .. SingerPluginService.Registry.Snapshot.Settings.Values
            .OrderBy(item => item.Descriptor.DisplayName, StringComparer.CurrentCulture).Select(item => new SettingsPluginOption(item))];
        selectedPlugin = PluginOptions[0];
        _disposables.Add(this.WhenAnyValue(item => item.SelectedCategory).Subscribe(_ => this.RaisePropertyChanged(nameof(IsPluginTab))));
    }

    /// <summary>等待已排队的实时保存后切换实例，不询问保存或放弃。</summary>
    private async Task SelectPluginAsync(SettingsPluginOption option)
    {
        if (!CanSelectPlugin || disposed)
        {
            RestorePluginSelection();
            return;
        }
        switchingPlugin = true;
        this.RaisePropertyChanged(nameof(CanSelectPlugin));
        selectedPlugin = option;
        this.RaisePropertyChanged(nameof(SelectedPlugin));
        try
        {
            if (PluginSettings != null) await PluginSettings.FlushAsync();
            if (disposed) return;
            DisposePluginSelection();
            PluginSettings = option.Registration is { } registration ? new(registration) : null;
            this.RaisePropertyChanged(nameof(HasSelectedPlugin));
            if (PluginSettings != null)
            {
                PluginSettings.PropertyChanged += OnPluginSettingsChanged;
                await PluginSettings.InitializeAsync();
            }
        }
        finally
        {
            switchingPlugin = false;
            this.RaisePropertyChanged(nameof(CanSelectPlugin));
        }
    }

    private void OnPluginSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PluginSettingsViewModel.IsBusy) or nameof(PluginSettingsViewModel.IsSaving)) this.RaisePropertyChanged(nameof(CanSelectPlugin));
    }

    /// <summary>操作期间拒绝选择变更，保持下拉框与实际插件一致。</summary>
    private void RestorePluginSelection() => Dispatcher.UIThread.Post(() =>
    {
        if (!disposed) this.RaisePropertyChanged(nameof(SelectedPlugin));
    });

    public override void OnBackRequested() => _ = LeaveSettingsAsync();

    private async Task LeaveSettingsAsync()
    {
        if (leavingSettings || switchingPlugin || disposed) return;
        leavingSettings = true;
        try
        {
            if (PluginSettings?.IsBusy == true) { ToastService.Enqueue(L.S("PluginSettings.Busy")); return; }
            if (PluginSettings != null) await PluginSettings.FlushAsync();
            if (disposed) return;
            Navigator.NavigateBack(this);
        }
        finally { leavingSettings = false; }
    }

    private void DisposePluginSelection()
    {
        if (PluginSettings == null) return;
        PluginSettings.PropertyChanged -= OnPluginSettingsChanged;
        PluginSettings.Dispose();
    }
}
