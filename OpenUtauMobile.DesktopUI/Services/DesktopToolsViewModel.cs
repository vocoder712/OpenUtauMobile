using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Storage;
using OpenUtauMobile.ViewModels;
using ReactiveUI;

namespace OpenUtauMobile.DesktopUI.Services
{
    public sealed class DesktopToolsViewModel : NavigateViewModelBase, IDisposable
    {
        private readonly MainViewModel _main;
        private CancellationTokenSource _cancellation = new();
        private bool _disposeRequested;
        public ObservableCollection<DesktopTool> Resamplers { get; } = [];
        public ObservableCollection<DesktopTool> Wavtools { get; } = [];
        public string WinePath { get; set; } = Preferences.Default.WinePath;
        public DesktopToolKind Kind { get; set; }
        public DesktopTool? Selected { get; set; }
        private bool _busy;
        public bool Busy { get => _busy; private set => this.RaiseAndSetIfChanged(ref _busy, value); }
        private string _status = string.Empty;
        public string Status { get => _status; private set => this.RaiseAndSetIfChanged(ref _status, value); }
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallFileCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallFolderCommand { get; }
        public ReactiveCommand<Unit, Unit> UninstallCommand { get; }
        public ReactiveCommand<Unit, Unit> WineCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public DesktopToolsViewModel(MainViewModel main) : base(main)
        {
            _main = main;
            RefreshCommand = ReactiveCommand.CreateFromTask(() => ApplyAsync(() => Task.CompletedTask));
            InstallFileCommand = ReactiveCommand.CreateFromTask(() => InstallAsync(false));
            InstallFolderCommand = ReactiveCommand.CreateFromTask(() => InstallAsync(true));
            UninstallCommand = ReactiveCommand.CreateFromTask(UninstallAsync);
            WineCommand = ReactiveCommand.CreateFromTask(() =>
            {
                string path = WinePath;
                return ApplyAsync(() =>
                {
                    if (!string.IsNullOrEmpty(path) && !DesktopToolsService.IsExecutable(path)) throw new System.IO.FileNotFoundException(L.S("Desktop.Unavailable"), path);
                    Preferences.Default.WinePath = path;
                    Preferences.Save();
                    return Task.CompletedTask;
                });
            });
            CancelCommand = ReactiveCommand.Create(() => { if (Busy) _cancellation.Cancel(); else OnBackRequested(); });
            DesktopToolsService.Instance.Changed += OnChanged;
            Load();
        }
        private void OnChanged()
        {
            if (DesktopToolsService.Instance.Pending) Status = L.S("Desktop.ToolChangesQueued");
            else Load();
        }
        private void Load()
        {
            Resamplers.Clear(); Wavtools.Clear();
            foreach (DesktopTool tool in DesktopToolsService.GetTools(DesktopToolKind.Resampler)) Resamplers.Add(tool);
            foreach (DesktopTool tool in DesktopToolsService.GetTools(DesktopToolKind.Wavtool)) Wavtools.Add(tool);
        }
        private async Task ApplyAsync(Func<Task> action)
        {
            if (Busy) return;
            _cancellation.Dispose();
            _cancellation = new();
            Busy = true;
            try { await DesktopToolsService.Instance.ApplyAsync(_main, action, _cancellation.Token); Status = string.Empty; ToastService.Enqueue(L.S("Desktop.ToolChangesApplied")); }
            catch (OperationCanceledException) { Status = string.Empty; }
            catch (Exception e) { Status = e.ToString(); ToastService.Enqueue(e.Message); }
            finally { Busy = false; if (_disposeRequested) DisposeCommands(); }
        }
        public Task InstallDroppedFileAsync(string path, DesktopToolKind kind)
        {
            Kind = kind;
            return ApplyAsync(() => DesktopToolsService.InstallAsync(path, kind, false));
        }
        private async Task InstallAsync(bool folder)
        {
            if (Busy) return;
            DesktopToolKind kind = Kind;
            try
            {
                string source = folder ? await FilePicker.PickFolderAsync(L.S("Desktop.InstallFolder")) : await FilePicker.PickSingleFileAsync(L.S("Desktop.InstallFile"), ["*.exe", "*.bat", "*.sh", "*"]);
                if (!string.IsNullOrEmpty(source)) await ApplyAsync(() => DesktopToolsService.InstallAsync(source, kind, folder));
            }
            catch (Exception e) { Status = e.ToString(); ToastService.Enqueue(e.Message); }
        }
        private async Task UninstallAsync()
        {
            DesktopTool? tool = Selected;
            if (Busy || tool == null || tool.Builtin) return;
            if (DesktopToolsService.IsInUse(tool)) { Status = L.S("Desktop.ToolInUse"); return; }
            string? choice = await OptionConfirmPopupService.ShowAsync(L.S("Desktop.Uninstall"), tool.Name,
                new[] { new OptionConfirmOption(L.S("Common.Cancel"), "cancel", isDefault: true), new OptionConfirmOption(L.S("Desktop.Uninstall"), "remove", isDestructive: true) });
            if (choice == "remove") await ApplyAsync(() => { DesktopToolsService.Uninstall(tool); return Task.CompletedTask; });
        }
        public override void OnBackRequested()
        {
            _cancellation.Cancel();
            base.OnBackRequested();
        }
        public void Dispose()
        {
            if (_disposeRequested) return;
            _disposeRequested = true;
            DesktopToolsService.Instance.Changed -= OnChanged;
            if (!Busy) DisposeCommands();
        }
        private void DisposeCommands()
        {
            _cancellation.Dispose();
            RefreshCommand.Dispose(); InstallFileCommand.Dispose(); InstallFolderCommand.Dispose(); UninstallCommand.Dispose(); WineCommand.Dispose(); CancelCommand.Dispose();
        }
    }
}
