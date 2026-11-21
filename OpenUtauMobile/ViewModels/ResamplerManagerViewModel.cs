using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

/// <summary>自定义重采样器管理页：添加、移除，并在变更后刷新引擎注册表。</summary>
public sealed class ResamplerManagerViewModel : NavigateViewModelBase, IDisposable
{
    public ObservableCollectionExtended<ResamplerCardViewModel> Resamplers { get; } = [];
    private bool disposed;
    [Reactive] public bool IsBusy { get; private set; }
    [Reactive] public bool IsEmpty { get; private set; }
    [Reactive] public bool HasError { get; private set; }
    [Reactive] public string ErrorMessage { get; private set; } = string.Empty;
    /// <summary>当前设备支持的文件类型提示；Wine 路径可能在设置中变化，因此每次刷新时重新生成。</summary>
    [Reactive] public string Hint { get; private set; } = string.Empty;

    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> AddCommand { get; }
    public ReactiveCommand<ResamplerCardViewModel, Unit> RemoveCommand { get; }

    public ResamplerManagerViewModel(MainViewModel navigator) : base(navigator)
    {
        IObservable<bool> canOperate = this.WhenAnyValue(vm => vm.IsBusy, busy => !busy);
        BackCommand = ReactiveCommand.Create(() => Navigator.NavigateBack(this), canOperate);
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync, canOperate);
        AddCommand = ReactiveCommand.CreateFromTask(AddAsync, canOperate);
        RemoveCommand = ReactiveCommand.CreateFromTask<ResamplerCardViewModel>(RemoveAsync, canOperate);
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
        Hint = string.Format(L.S("ResamplerManager.Hint"), ResamplerManagementService.DescribeSupportedTypes());
        try
        {
            IReadOnlyList<ResamplerFileEntry> entries = await Task.Run(ResamplerManagementService.ListFiles);
            Resamplers.Load([.. entries.Select(entry => new ResamplerCardViewModel(entry))]);
            IsEmpty = entries.Count == 0;
            HasError = false;
            ErrorMessage = string.Empty;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to list resamplers");
            IsEmpty = false;
            HasError = true;
            ErrorMessage = string.Format(L.S("ResamplerManager.RefreshFailed"), exception.Message);
        }
    }

    /// <summary>引擎没有全局的渲染空闲边界，工程打开期间不修改重采样器注册表。</summary>
    private bool EnsureNoOpenProject()
    {
        if (Navigator.ActiveEditor == null) return true;
        ToastService.Enqueue(L.S("ResamplerManager.CloseProjectFirst"));
        return false;
    }

    private async Task AddAsync()
    {
        if (IsBusy || !EnsureNoOpenProject()) return;
        IsBusy = true;
        try
        {
            bool installed = false;
            try
            {
                string path = await FilePicker.PickSingleFileAsync(
                    L.S("ResamplerManager.SelectFile"), ResamplerManagementService.PickerFilters);
                if (string.IsNullOrEmpty(path)) return;
                ResamplerManagementService.ValidateForInstall(path);
                string fileName = Path.GetFileName(path);

                // 重采样器是由应用直接执行的外部程序，添加前需要用户确认信任来源。
                string? answer = await OptionConfirmPopupService.ShowAsync(
                    L.S("ResamplerManager.TrustTitle"),
                    string.Format(L.S("ResamplerManager.TrustQuestion"), fileName)
                        + Environment.NewLine + Environment.NewLine + L.S("ResamplerManager.TrustMessage"),
                    [
                        new OptionConfirmOption(L.S("Common.Cancel"), "cancel", isDefault: true),
                        new OptionConfirmOption(L.S("ResamplerManager.Continue"), "continue", isDestructive: true),
                    ]);
                if (answer != "continue") return;

                if (ResamplerManagementService.WillReplace(path))
                {
                    answer = await OptionConfirmPopupService.ShowAsync(
                        L.S("ResamplerManager.ReplaceTitle"),
                        string.Format(L.S("ResamplerManager.ReplaceMessage"), fileName),
                        [
                            new OptionConfirmOption(L.S("Common.Cancel"), "cancel", isDefault: true),
                            new OptionConfirmOption(L.S("ResamplerManager.Replace"), "replace", isDestructive: true),
                        ]);
                    if (answer != "replace") return;
                }

                await LoadingPopupService.RunAsync(L.S("ResamplerManager.Installing"), 0, async loading =>
                {
                    await ResamplerManagementService.InstallAsync(path, loading);
                    await ResamplerManagementService.RescanAsync();
                    await ServiceHub.FlushFileSystemAsync();
                    loading.UpdateProgress(100, L.S("ResamplerManager.Completed"));
                });
                installed = true;
            }
            catch (Exception exception)
            {
                ShowError("ResamplerManager.InstallFailed", exception);
            }
            finally
            {
                await RefreshEntriesAsync();
            }
            if (installed) ToastService.Enqueue(L.S("ResamplerManager.Installed"));
        }
        finally { IsBusy = false; }
    }

    private async Task RemoveAsync(ResamplerCardViewModel card)
    {
        if (IsBusy || !EnsureNoOpenProject()) return;
        IsBusy = true;
        try
        {
            bool removed = false;
            try
            {
                string? answer = await OptionConfirmPopupService.ShowAsync(
                    L.S("ResamplerManager.DeleteTitle"),
                    string.Format(L.S("ResamplerManager.DeleteMessage"), card.FileName),
                    [
                        new OptionConfirmOption(L.S("Common.Cancel"), "cancel", isDefault: true),
                        new OptionConfirmOption(L.S("Common.Delete"), "delete", isDestructive: true),
                    ]);
                if (answer != "delete") return;

                await LoadingPopupService.RunAsync(L.S("ResamplerManager.Removing"), async _ =>
                {
                    await Task.Run(() => ResamplerManagementService.Uninstall(card.RelativePath));
                    await ResamplerManagementService.RescanAsync();
                    await ServiceHub.FlushFileSystemAsync();
                });
                removed = true;
            }
            catch (Exception exception)
            {
                ShowError("ResamplerManager.DeleteFailed", exception);
            }
            finally
            {
                await RefreshEntriesAsync();
            }
            if (removed) ToastService.Enqueue(L.S("ResamplerManager.Removed"));
        }
        finally { IsBusy = false; }
    }

    private static void ShowError(string key, Exception exception)
    {
        Log.Error(exception, "Resampler management operation failed: {Operation}", key);
        ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(L.S(key), exception)));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        BackCommand.Dispose(); RefreshCommand.Dispose(); AddCommand.Dispose();
        RemoveCommand.Dispose();
    }
}
