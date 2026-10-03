using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using DynamicData.Binding;
using OpenUtau.Core.Plugins;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels;

public class PluginEntryViewModel : ReactiveObject {
    public string FileName { get; }
    public string FullPath { get; }
    [Reactive] public string SizeText { get; private set; } = string.Empty;
    [Reactive] public string DetailsText { get; private set; } = string.Empty;
    [Reactive] public bool HasError { get; private set; }
    [Reactive] public bool IsLoaded { get; private set; }

    public ReactiveCommand<Unit, Unit> UninstallCommand { get; }

    public PluginEntryViewModel(InstalledPlugin info, Action<PluginEntryViewModel> onUninstall) {
        FileName = info.FileName;
        FullPath = info.FullPath;
        SizeText = FormatSize(info.FileSize);
        IsLoaded = info.IsLoaded;
        HasError = !info.IsLoaded && !string.IsNullOrEmpty(info.LoadError);

        if (info.IsLoaded) {
            DetailsText = info.PhonemizerCount > 0
                ? string.Format(L.S("Settings.File.Plugins.PhonemizerCount"), info.PhonemizerCount)
                : L.S("Settings.File.Plugins.NoRecognizedTypes");
        } else if (HasError) {
            DetailsText = info.LoadError ?? L.S("Settings.File.Plugins.LoadFailed");
        } else {
            DetailsText = L.S("Settings.File.Plugins.NotLoaded");
        }

        UninstallCommand = ReactiveCommand.Create(() => onUninstall(this));
    }

    private static string FormatSize(long bytes) {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        int i = 0;
        while (size >= 1024 && i < units.Length - 1) {
            size /= 1024;
            i++;
        }
        return $"{size:0.##} {units[i]}";
    }
}

public class PluginManagementViewModel : ReactiveObject, IDisposable {
    public ObservableCollectionExtended<PluginEntryViewModel> InstalledPlugins { get; } = [];

    public ReactiveCommand<Unit, Unit> ImportCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    [Reactive] public bool IsEmpty { get; private set; }

    public PluginManagementViewModel() {
        ImportCommand = ReactiveCommand.CreateFromTask(ImportAsync);
        RefreshCommand = ReactiveCommand.Create(Refresh);
        Refresh();
    }

    public void Refresh() {
        InstalledPlugins.Clear();
        try {
            List<InstalledPlugin> plugins = PluginManager.Inst.GetInstalledPlugins();
            foreach (InstalledPlugin p in plugins) {
                InstalledPlugins.Add(new PluginEntryViewModel(p, OnUninstall));
            }
        } catch (Exception e) {
            Log.Error(e, "Failed to enumerate plugins");
        }
        IsEmpty = InstalledPlugins.Count == 0;
    }

    private async Task ImportAsync() {
        try {
            string path = await FilePicker.PickSingleFileAsync(
                L.S("Settings.File.Plugins.Import"), new[] { "*.dll" });
            if (string.IsNullOrEmpty(path)) return;

            try {
                InstalledPlugin info = await Task.Run(() => PluginManager.Inst.Import(path));
                ToastService.Enqueue(string.Format(
                    L.S("Settings.File.Plugins.ImportSuccess"), info.FileName));
            } catch (Exception e) {
                Log.Error(e, "Failed to import plugin");
                ToastService.Enqueue(L.S("Settings.File.Plugins.ImportFailed"));
            }

            Refresh();
        } catch (Exception e) {
            Log.Error(e, "Import flow failed");
        }
    }

    private void OnUninstall(PluginEntryViewModel vm) {
        _ = OnUninstallAsync(vm);
    }

    private async Task OnUninstallAsync(PluginEntryViewModel vm) {
        try {
            List<OptionConfirmOption> options =
            [
                new(L.S("Common.Cancel"), "cancel", isDefault: true),
                new(L.S("Common.Uninstall"), "uninstall", isDestructive: true),
            ];
            string? result = await OptionConfirmPopupService.ShowAsync(
                L.S("Settings.File.Plugins.UninstallConfirmTitle"),
                string.Format(L.S("Settings.File.Plugins.UninstallConfirmMessage"), vm.FileName),
                options);
            if (result != "uninstall") return;

            await Task.Run(() => {
                PluginManager.Inst.Uninstall(new InstalledPlugin {
                    FileName = vm.FileName,
                    FullPath = vm.FullPath,
                });
            });

            ToastService.Enqueue(string.Format(
                L.S("Settings.File.Plugins.UninstallSuccess"), vm.FileName));
            Refresh();
        } catch (Exception e) {
            Log.Error(e, "Failed to uninstall plugin");
            ToastService.Enqueue(L.S("Settings.File.Plugins.UninstallFailed"));
        }
    }

    public void Dispose() {
        GC.SuppressFinalize(this);
    }
}