using System;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels;

public class PluginManagerViewModel : NavigateViewModelBase, IDisposable
{
    public ObservableCollectionExtended<PluginCardViewModel> Plugins { get; } = [];
    private bool disposed;
    [Reactive] public bool IsBusy { get; private set; }
    [Reactive] public bool IsEmpty { get; private set; }
    [Reactive] public bool HasError { get; private set; }
    [Reactive] public string ErrorMessage { get; private set; } = string.Empty;

    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> InstallCommand { get; }
    public ReactiveCommand<PluginCardViewModel, Unit> OpenDetailsCommand { get; }

    public PluginManagerViewModel(MainViewModel navigator) : base(navigator)
    {
        IObservable<bool> canOperate = this.WhenAnyValue(vm => vm.IsBusy, busy => !busy);
        BackCommand = ReactiveCommand.Create(() => Navigator.NavigateBack(this), canOperate);
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync, canOperate);
        InstallCommand = ReactiveCommand.CreateFromTask(InstallAsync, canOperate);
        OpenDetailsCommand = ReactiveCommand.Create<PluginCardViewModel>(
            card => Navigator.Navigate(new PluginDetailsViewModel(Navigator, card.RelativePath)), canOperate);
    }

    public override void OnNavigatedTo() => _ = RefreshAsync();

    public override void OnBackRequested()
    {
        if (!IsBusy) base.OnBackRequested();
    }

    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try { await RefreshEntriesAsync(); }
        finally { IsBusy = false; }
    }

    private async Task RefreshEntriesAsync()
    {
        try
        {
            IReadOnlyList<PluginFileEntry> entries = await Task.Run(PluginManagementService.ListFiles);
            Plugins.Load([.. System.Linq.Enumerable.Select(entries, entry => new PluginCardViewModel(entry))]);
            IsEmpty = entries.Count == 0;
            HasError = false;
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to list plugin files");
            IsEmpty = false;
            HasError = true;
            ErrorMessage = string.Format(L.S("PluginManager.RefreshFailed"), exception.Message);
        }
    }

    private async Task InstallAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        bool installed = false;
        try
        {
            try
            {
                string path = await FilePicker.PickSingleFileAsync(L.S("PluginManager.SelectFile"), ["*.dll"]);
                if (string.IsNullOrEmpty(path)) return;
                await LoadingPopupService.RunAsync(L.S("PluginManager.Checking"),
                    _ => Task.Run(() => PluginManagementService.ValidateForInstall(path)));
                string? answer = await OptionConfirmPopupService.ShowAsync(
                    L.S("PluginManager.TrustTitle"),
                    string.Format(L.S("PluginManager.TrustQuestion"), Path.GetFileName(path))
                        + Environment.NewLine + Environment.NewLine + L.S("PluginManager.TrustMessage"),
                    [
                        new OptionConfirmOption(L.S("Common.Cancel"), "cancel", isDefault: true),
                        new OptionConfirmOption(L.S("PluginManager.Continue"), "continue", isDestructive: true),
                    ]);
                if (answer != "continue") return;
                await LoadingPopupService.RunAsync(L.S("PluginManager.Checking"), 0, async loading =>
                {
                    await Task.Run(() => PluginManagementService.InstallAsync(path, loading));
                    await ServiceHub.FlushFileSystemAsync();
                    loading.UpdateProgress(100, L.S("PluginManager.Completed"));
                });
                installed = true;
            }
            catch (Exception exception)
            {
                ShowError("PluginManager.InstallFailed", exception);
            }
            finally
            {
                await RefreshEntriesAsync();
            }
            if (installed)
            {
                await OptionConfirmPopupService.ShowAsync(
                    L.S("PluginManager.InstallSucceeded"), L.S("PluginManager.RestartToActivate"),
                    [new OptionConfirmOption(L.S("Common.Confirm"), "ok", isPrimary: true, isDefault: true)]);
            }
        }
        finally { IsBusy = false; }
    }

    private static void ShowError(string key, Exception exception)
    {
        Log.Error(exception, "Plugin management operation failed: {Operation}", key);
        ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(L.S(key), exception)));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        BackCommand.Dispose(); RefreshCommand.Dispose(); InstallCommand.Dispose();
        OpenDetailsCommand.Dispose();
    }
}
