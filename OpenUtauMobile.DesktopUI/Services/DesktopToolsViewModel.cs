using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using System.Reactive.Linq;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Storage;
using OpenUtauMobile.ViewModels;
using ReactiveUI;
using Serilog;

namespace OpenUtauMobile.DesktopUI.Services
{
    public enum DesktopToolStatusKind { Info, Success, Warning, Error }

    public sealed class DesktopToolsViewModel : NavigateViewModelBase, IDisposable
    {
        private readonly MainViewModel _main;
        private CancellationTokenSource _cancellation = new();
        private bool _disposeRequested;
        private int _loadRevision;
        public ObservableCollection<DesktopTool> Resamplers { get; } = [];
        public ObservableCollection<DesktopTool> Wavtools { get; } = [];
        public string WinePath { get; set; } = Preferences.Default.WinePath;
        public DesktopToolKind Kind { get; set; }
        private DesktopTool? _selected;
        public DesktopTool? Selected { get => _selected; set => this.RaiseAndSetIfChanged(ref _selected, value); }
        public bool CanUninstall => !Busy && Selected is { Builtin: false, FilePath: not null } && System.IO.File.Exists(Selected.FilePath);
        private bool _busy;
        public bool Busy { get => _busy; private set => this.RaiseAndSetIfChanged(ref _busy, value); }
        private string _status = string.Empty;
        public string Status { get => _status; private set => this.RaiseAndSetIfChanged(ref _status, value); }
        private DesktopToolStatusKind _statusKind;
        public DesktopToolStatusKind StatusKind { get => _statusKind; private set => this.RaiseAndSetIfChanged(ref _statusKind, value); }
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallFileCommand { get; }
        public ReactiveCommand<Unit, Unit> InstallFolderCommand { get; }
        public ReactiveCommand<Unit, Unit> UninstallCommand { get; }
        public ReactiveCommand<Unit, Unit> WineCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        public DesktopToolsViewModel(MainViewModel main) : base(main)
        {
            _main = main;
            RefreshCommand = ReactiveCommand.CreateFromTask(() => ApplyAsync(_ => Task.FromResult(true)));
            InstallFileCommand = ReactiveCommand.CreateFromTask(() => InstallAsync(false));
            InstallFolderCommand = ReactiveCommand.CreateFromTask(() => InstallAsync(true));
            UninstallCommand = ReactiveCommand.CreateFromTask(UninstallAsync);
            WineCommand = ReactiveCommand.CreateFromTask(() =>
            {
                string path = WinePath;
                return ApplyAsync(_ =>
                {
                    if (!string.IsNullOrEmpty(path) && !DesktopToolsService.IsExecutable(path)) throw new System.IO.FileNotFoundException(L.S("Desktop.Unavailable"), path);
                    Preferences.Default.WinePath = path;
                    Preferences.Save();
                    return Task.FromResult(true);
                });
            });
            CancelCommand = ReactiveCommand.Create(() => { if (Busy) _cancellation.Cancel(); else OnBackRequested(); });
            this.WhenAnyValue(x => x.Selected, x => x.Busy)
                .Subscribe(_ => this.RaisePropertyChanged(nameof(CanUninstall)));
            DesktopToolsService.Instance.Changed += OnChanged;
            _ = LoadAsync();
        }
        private void OnChanged()
        {
            if (DesktopToolsService.Instance.Pending) SetStatus(L.S("Desktop.ToolChangesQueued"), DesktopToolStatusKind.Info);
            else _ = LoadAsync();
        }
        private async Task LoadAsync()
        {
            int revision = Interlocked.Increment(ref _loadRevision);
            try
            {
                (DesktopTool[] resamplers, DesktopTool[] wavtools) = await Task.Run(() =>
                    (DesktopToolsService.GetTools(DesktopToolKind.Resampler).ToArray(), DesktopToolsService.GetTools(DesktopToolKind.Wavtool).ToArray()));
                if (_disposeRequested || revision != Volatile.Read(ref _loadRevision)) return;
                Resamplers.Clear(); Wavtools.Clear(); Selected = null;
                foreach (DesktopTool tool in resamplers) Resamplers.Add(tool);
                foreach (DesktopTool tool in wavtools) Wavtools.Add(tool);
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Failed to refresh desktop tools");
                if (!_disposeRequested && revision == Volatile.Read(ref _loadRevision)) SetStatus(exception.Message, DesktopToolStatusKind.Error);
            }
        }
        private async Task ApplyAsync(Func<CancellationToken, Task<bool>> action)
        {
            if (Busy) return;
            _cancellation.Dispose();
            _cancellation = new();
            Busy = true;
            try
            {
                bool changed = await DesktopToolsService.Instance.ApplyAsync(_main, action, _cancellation.Token);
                SetStatus(changed ? L.S("Desktop.ToolChangesApplied") : string.Empty,
                    changed ? DesktopToolStatusKind.Success : DesktopToolStatusKind.Info);
                if (changed && _main.CurrentViewModel != this) ToastService.Enqueue(L.S("Desktop.ToolChangesApplied"));
            }
            catch (OperationCanceledException) { SetStatus(string.Empty, DesktopToolStatusKind.Info); }
            catch (Exception e)
            {
                Log.Error(e, "Desktop tool operation failed");
                SetStatus(e.Message, DesktopToolStatusKind.Error);
                if (_main.CurrentViewModel != this) ToastService.Enqueue(e.Message);
            }
            finally { Busy = false; if (_disposeRequested) DisposeCommands(); }
        }
        public Task InstallDroppedFileAsync(string path, DesktopToolKind kind)
        {
            Kind = kind;
            return ApplyAsync(token => DesktopToolsService.InstallAsync(path, kind, false, token));
        }
        private async Task InstallAsync(bool folder)
        {
            if (Busy) return;
            DesktopToolKind kind = Kind;
            try
            {
                string source = folder ? await FilePicker.PickFolderAsync(L.S("Desktop.InstallFolder")) : await FilePicker.PickSingleFileAsync(L.S("Desktop.InstallFile"), ["*.exe", "*.bat", "*.sh", "*"]);
                if (!string.IsNullOrEmpty(source)) await ApplyAsync(token => DesktopToolsService.InstallAsync(source, kind, folder, token));
            }
            catch (Exception e) { Log.Error(e, "Failed to choose a desktop tool package"); SetStatus(e.Message, DesktopToolStatusKind.Error); ToastService.Enqueue(e.Message); }
        }
        private async Task UninstallAsync()
        {
            DesktopTool? tool = Selected;
            if (Busy || tool == null || tool.Builtin) return;
            if (DesktopToolsService.IsInUse(tool)) { SetStatus(L.S("Desktop.ToolInUse"), DesktopToolStatusKind.Warning); return; }
            string? choice = await OptionConfirmPopupService.ShowAsync(L.S("Common.Uninstall"), tool.Name,
                new[] { new OptionConfirmOption(L.S("Common.Cancel"), "cancel", isDefault: true), new OptionConfirmOption(L.S("Common.Uninstall"), "remove", isDestructive: true) });
            if (choice == "remove") await ApplyAsync(_ => { DesktopToolsService.Uninstall(tool); return Task.FromResult(true); });
        }
        public override void OnBackRequested()
        {
            _cancellation.Cancel();
            base.OnBackRequested();
        }
        private void SetStatus(string status, DesktopToolStatusKind kind)
        {
            Status = status;
            StatusKind = kind;
        }
        public void Dispose()
        {
            if (_disposeRequested) return;
            _disposeRequested = true;
            _cancellation.Cancel();
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
