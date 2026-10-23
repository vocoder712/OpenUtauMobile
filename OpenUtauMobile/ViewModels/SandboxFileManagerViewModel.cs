using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Avalonia;
using Avalonia.Controls;
using IconPacks.Avalonia.PhosphorIcons;

namespace OpenUtauMobile.ViewModels
{
    /// <summary>应用沙箱管理页面，串行处理操作并区分文件修改和持久化结果。</summary>
    public sealed class SandboxFileManagerViewModel : NavigateViewModelBase, IDisposable
    {
        private readonly SandboxFileService _files;
        private readonly ISandboxFileTransferService _transfer;
        private readonly IDisposable? _languageSubscription;
        private CancellationTokenSource? _operation;
        private SandboxPreparedExport? _preparedExport;
        private bool _disposed;
        private bool _leaveAfterCancellation;
        private SandboxConflictAction? _batchConflictAction;
        private string? _listedPath;
        private bool _updatingSelection;

        public ObservableCollection<SandboxFileItemViewModel> Entries { get; } = [];
        [Reactive] public string CurrentPath { get; private set; }
        [Reactive] public bool IsBusy { get; private set; }
        /// <summary>目录加载单独显示进度，避免展示上一层目录的条目。</summary>
        [Reactive] public bool IsLoading { get; private set; }
        [Reactive] public bool IsCreationMenuOpen { get; set; }
        [Reactive] public bool CanCancel { get; private set; }
        [Reactive] public string ProgressText { get; private set; } = "";
        [Reactive] public string ErrorMessage { get; private set; } = "";
        [Reactive] public string StatusMessage { get; private set; } = "";
        [Reactive] public bool NeedsPersistence { get; private set; }
        [Reactive] public bool HasPreparedExport { get; private set; }
        public string PreparedExportName => _preparedExport?.Name ?? "";
        public bool ShowPersistenceWarning => NeedsPersistence && !IsBusy;
        public bool ShowOperationProgress => IsBusy && !IsLoading;
        public bool CanModify => !IsBusy && !NeedsPersistence;
        /// <summary>底栏根据选择状态导出所选项目或整个当前目录。</summary>
        public string ExportActionLabel => L.S(HasSelection ? "Sandbox.Export" : "Sandbox.ExportCurrent");
        /// <summary>复用表情图库的 FAB 菜单，所有新建和导入操作集中在同一入口。</summary>
        public IReadOnlyList<FabMenuAction> CreationActions =>
        [
            new("newFolder", L.S("Sandbox.NewFolder"), PackIconPhosphorIconsKind.FolderPlus),
            new("importFiles", L.S("Sandbox.ImportFiles"), PackIconPhosphorIconsKind.FileArrowUp),
            new("importFolder", L.S("Sandbox.ImportFolder"), PackIconPhosphorIconsKind.FolderOpen),
            new("importZip", L.S("Sandbox.ImportZip"), PackIconPhosphorIconsKind.Archive),
        ];
        /// <summary>显示工作区相对路径，不暴露沙箱外的设备目录。</summary>
        public string RelativePath => CurrentPath == _files.Root ? L.S("Sandbox.Root") :
            L.S("Sandbox.Root") + " / " + Path.GetRelativePath(_files.Root, CurrentPath).Replace('\\', '/');
        public bool CanGoUp => CurrentPath != _files.Root && !IsBusy;
        public bool CanImportFolder => _transfer.CanImportFolder;
        public bool IsEmpty => Entries.Count == 0 && !IsBusy && string.IsNullOrEmpty(ErrorMessage);
        public bool HasSelection => Entries.Any(item => item.IsSelected);
        public string SelectionSummary => string.Format(L.S("Sandbox.SelectionSummary"), Entries.Count(item => item.IsSelected), Entries.Count);
        public string ImportHint => OperatingSystem.IsBrowser() ? L.S("Sandbox.BrowserHint") : L.S("Sandbox.Hint");

        public ReactiveCommand<Unit, Unit> BackCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
        public ReactiveCommand<Unit, Unit> GoUpCommand { get; }
        public ReactiveCommand<SandboxFileItemViewModel, Unit> OpenCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportFilesCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportFolderCommand { get; }
        public ReactiveCommand<Unit, Unit> ImportZipCommand { get; }
        public ReactiveCommand<Unit, Unit> NewFolderCommand { get; }
        public ReactiveCommand<SandboxFileItemViewModel, Unit> RenameCommand { get; }
        public ReactiveCommand<SandboxFileItemViewModel, Unit> DeleteItemCommand { get; }
        public ReactiveCommand<Unit, Unit> DeleteSelectedCommand { get; }
        public ReactiveCommand<SandboxFileItemViewModel, Unit> ExportItemCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportSelectedCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportCurrentCommand { get; }
        public ReactiveCommand<Unit, Unit> SaveExportCommand { get; }
        public ReactiveCommand<Unit, Unit> DismissExportCommand { get; }
        public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
        public ReactiveCommand<Unit, Unit> InvertSelectionCommand { get; }
        public ReactiveCommand<Unit, Unit> ClearSelectionCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportCommand { get; }
        public ReactiveCommand<string, Unit> CreationCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }
        public ReactiveCommand<Unit, Unit> RetryPersistenceCommand { get; }

        /// <summary>绑定平台传输能力和可观察的忙碌、选择及持久化状态。</summary>
        public SandboxFileManagerViewModel(MainViewModel navigator) : base(navigator)
        {
            _files = new SandboxFileService(() => Navigator.ActiveEditor != null);
            _transfer = ServiceHub.SandboxFileTransferService ?? throw new InvalidOperationException();
            CurrentPath = _files.Root;
            IObservable<bool> idle = this.WhenAnyValue(model => model.IsBusy, busy => !busy);
            IObservable<bool> writable = this.WhenAnyValue(model => model.IsBusy, model => model.NeedsPersistence,
                (busy, pending) => !busy && !pending);
            IObservable<bool> selected = this.WhenAnyValue(model => model.IsBusy, model => model.HasSelection,
                (busy, selection) => !busy && selection);
            BackCommand = ReactiveCommand.Create(OnBackRequested);
            RefreshCommand = ReactiveCommand.CreateFromTask(() => RunAsync(LoadAsync), idle);
            GoUpCommand = ReactiveCommand.CreateFromTask(() => RunAsync(async token =>
            {
                if (CurrentPath != _files.Root) CurrentPath = _files.Resolve(Path.GetDirectoryName(CurrentPath)!);
                await LoadAsync(token);
            }), this.WhenAnyValue(model => model.CanGoUp));
            OpenCommand = ReactiveCommand.CreateFromTask<SandboxFileItemViewModel>(item => RunAsync(async token =>
            {
                if (item.IsDirectory)
                {
                    CurrentPath = _files.Resolve(item.Path);
                    await LoadAsync(token);
                }
                else item.IsSelected = !item.IsSelected;
            }), idle);
            ImportFilesCommand = ReactiveCommand.CreateFromTask(() => ImportAsync(false, false), writable);
            ImportFolderCommand = ReactiveCommand.CreateFromTask(() => ImportAsync(true, false), writable);
            ImportZipCommand = ReactiveCommand.CreateFromTask(() => ImportAsync(false, true), writable);
            NewFolderCommand = ReactiveCommand.CreateFromTask(CreateFolderAsync, writable);
            RenameCommand = ReactiveCommand.CreateFromTask<SandboxFileItemViewModel>(RenameAsync, writable);
            DeleteItemCommand = ReactiveCommand.CreateFromTask<SandboxFileItemViewModel>(item => DeleteAsync([item]), writable);
            DeleteSelectedCommand = ReactiveCommand.CreateFromTask(() => DeleteAsync(Entries.Where(item => item.IsSelected).ToArray()),
                this.WhenAnyValue(model => model.IsBusy, model => model.NeedsPersistence, model => model.HasSelection,
                    (busy, pending, selection) => !busy && !pending && selection));
            ExportItemCommand = ReactiveCommand.CreateFromTask<SandboxFileItemViewModel>(item => ExportAsync([item.Path]), idle);
            ExportSelectedCommand = ReactiveCommand.CreateFromTask(() => ExportAsync(Entries.Where(item => item.IsSelected).Select(item => item.Path).ToArray()), selected);
            ExportCurrentCommand = ReactiveCommand.CreateFromTask(() => ExportAsync([CurrentPath]), idle);
            ExportCommand = ReactiveCommand.CreateFromTask(() => ExportAsync(HasSelection
                ? Entries.Where(item => item.IsSelected).Select(item => item.Path).ToArray()
                : [CurrentPath]), idle);
            CreationCommand = ReactiveCommand.CreateFromTask<string>(action => action switch
            {
                "newFolder" => CreateFolderAsync(),
                "importFiles" => ImportAsync(false, false),
                "importFolder" => ImportAsync(true, false),
                "importZip" => ImportAsync(false, true),
                _ => Task.CompletedTask,
            }, writable);
            SaveExportCommand = ReactiveCommand.CreateFromTask(SaveExportAsync,
                this.WhenAnyValue(model => model.IsBusy, model => model.HasPreparedExport, (busy, prepared) => !busy && prepared));
            DismissExportCommand = ReactiveCommand.Create(ClearExport, idle);
            SelectAllCommand = ReactiveCommand.Create(() => UpdateSelection(_ => true), idle);
            InvertSelectionCommand = ReactiveCommand.Create(() => UpdateSelection(item => !item.IsSelected), idle);
            ClearSelectionCommand = ReactiveCommand.Create(() => UpdateSelection(_ => false), idle);
            CancelCommand = ReactiveCommand.Create(() => _operation?.Cancel(), this.WhenAnyValue(model => model.CanCancel));
            RetryPersistenceCommand = ReactiveCommand.CreateFromTask(() => RunAsync(async _ => await FlushAsync()),
                this.WhenAnyValue(model => model.IsBusy, model => model.NeedsPersistence, (busy, pending) => !busy && pending));
            PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(CurrentPath) or nameof(IsBusy))
                {
                    this.RaisePropertyChanged(nameof(RelativePath));
                    this.RaisePropertyChanged(nameof(CanGoUp));
                    this.RaisePropertyChanged(nameof(IsEmpty));
                }
                if (args.PropertyName == nameof(ErrorMessage)) this.RaisePropertyChanged(nameof(IsEmpty));
                if (args.PropertyName is nameof(NeedsPersistence) or nameof(IsBusy))
                {
                    this.RaisePropertyChanged(nameof(ShowPersistenceWarning));
                    this.RaisePropertyChanged(nameof(CanModify));
                }
                if (args.PropertyName is nameof(IsBusy) or nameof(IsLoading))
                    this.RaisePropertyChanged(nameof(ShowOperationProgress));
            };
            _languageSubscription = Application.Current?.GetResourceObservable("Sandbox.Root")
                .Where(value => value is string).Select(value => (string)value!).DistinctUntilChanged().Subscribe(_ =>
                {
                    this.RaisePropertyChanged(nameof(RelativePath));
                    this.RaisePropertyChanged(nameof(ImportHint));
                    this.RaisePropertyChanged(nameof(SelectionSummary));
                    this.RaisePropertyChanged(nameof(ExportActionLabel));
                    this.RaisePropertyChanged(nameof(CreationActions));
                    foreach (SandboxFileItemViewModel entry in Entries) entry.RefreshLocalization();
                });
        }

        /// <summary>重新进入页面时刷新，传输中的返回由本页控制。</summary>
        public override void OnNavigatedTo()
        {
            if (!IsBusy) _ = RunAsync(LoadAsync);
        }

        /// <summary>可取消阶段先取消并清理；提交及持久化阶段等待完成。</summary>
        public override void OnBackRequested()
        {
            if (IsCreationMenuOpen)
            {
                IsCreationMenuOpen = false;
                return;
            }
            if (IsBusy)
            {
                _leaveAfterCancellation = true;
                if (CanCancel) _operation?.Cancel();
                return;
            }
            if (NeedsPersistence)
            {
                ToastService.Enqueue(L.S("Sandbox.PersistenceFailed"));
                return;
            }
            Navigator.NavigateBack(this);
        }

        /// <summary>统一处理取消、部分失败、持久化和离开页面的时序。</summary>
        private async Task RunAsync(Func<CancellationToken, Task> action, bool mutation = false, bool cancellable = true)
        {
            if (IsBusy || _disposed) return;
            IsCreationMenuOpen = false;
            IsBusy = true;
            CanCancel = cancellable;
            ErrorMessage = "";
            StatusMessage = "";
            ProgressText = L.S("Sandbox.Working");
            _operation = new CancellationTokenSource();
            bool completed = false;
            try
            {
                if (mutation) NeedsPersistence = true;
                await action(_operation.Token);
                completed = true;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = L.S("Sandbox.Cancelled");
            }
            catch (Exception exception)
            {
                Log.Error(exception, "沙箱文件操作失败");
                ErrorMessage = string.Format(L.S("Sandbox.OperationFailed"), exception.Message);
            }
            finally
            {
                CanCancel = false;
                if (mutation)
                {
                    await FlushAsync();
                    if (NeedsPersistence) StatusMessage = "";
                    try { await LoadAsync(CancellationToken.None); }
                    catch (Exception exception) { ErrorMessage = exception.Message; }
                    if (completed && !NeedsPersistence && string.IsNullOrEmpty(StatusMessage)) StatusMessage = L.S("Sandbox.Completed");
                }
                _operation.Dispose();
                _operation = null;
                IsBusy = false;
                ProgressText = "";
                if (_leaveAfterCancellation)
                {
                    _leaveAfterCancellation = false;
                    // 操作失败时留在页面展示结果，不自动丢失错误信息。
                    if (!NeedsPersistence && string.IsNullOrEmpty(ErrorMessage)) Navigator.NavigateBack(this);
                }
            }
        }

        /// <summary>后台读取目录及元数据；仅将仍然有效的结果提交到界面。</summary>
        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            string path = CurrentPath;
            bool hasActiveEditor = Navigator.ActiveEditor != null;
            HashSet<string> selection = _listedPath == path
                ? Entries.Where(item => item.IsSelected).Select(item => item.Path).ToHashSet() : [];
            IsLoading = true;
            foreach (SandboxFileItemViewModel item in Entries) item.SelectionChanged -= RefreshSelection;
            Entries.Clear();
            RefreshSelection();
            try
            {
                SandboxFileItemViewModel[] items = await Task.Run(() => _files.List(path).Select(item =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string? restriction;
                    try { restriction = _files.GetMutationRestriction(item.FullName, hasActiveEditor); }
                    catch (IOException exception) { restriction = exception.Message; }
                    return new SandboxFileItemViewModel(item, restriction);
                }).ToArray(), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (_disposed || CurrentPath != path) return;
                foreach (SandboxFileItemViewModel entry in items)
                {
                    entry.IsSelected = selection.Contains(entry.Path);
                    entry.SelectionChanged += RefreshSelection;
                    Entries.Add(entry);
                }
                _listedPath = path;
            }
            finally
            {
                IsLoading = false;
                RefreshSelection();
            }
        }

        /// <summary>批量选择只汇总一次，避免每个勾选都重新遍历整份列表。</summary>
        private void UpdateSelection(Func<SandboxFileItemViewModel, bool> select)
        {
            _updatingSelection = true;
            try
            {
                foreach (SandboxFileItemViewModel item in Entries) item.IsSelected = select(item);
            }
            finally
            {
                _updatingSelection = false;
                RefreshSelection();
            }
        }

        private void RefreshSelection()
        {
            if (_updatingSelection) return;
            this.RaisePropertyChanged(nameof(HasSelection));
            this.RaisePropertyChanged(nameof(SelectionSummary));
            this.RaisePropertyChanged(nameof(IsEmpty));
            this.RaisePropertyChanged(nameof(ExportActionLabel));
        }

        private async Task FlushAsync()
        {
            if (!NeedsPersistence) return;
            try
            {
                ProgressText = L.S("Sandbox.Saving");
                await ServiceHub.FlushFileSystemAsync();
                NeedsPersistence = false;
            }
            catch (Exception exception)
            {
                Log.Error(exception, "沙箱修改未能写入持久存储");
                NeedsPersistence = true;
            }
        }

        private Task ImportAsync(bool folder, bool unzip) => RunAsync(async token =>
        {
            // 选择器先从点击直接发起；繁忙状态覆盖系统选择器的整个生命周期。
            await using SandboxImportBatch? batch = await _transfer.PickAsync(folder);
            if (batch == null) { StatusMessage = L.S("Sandbox.Cancelled"); return; }
            _batchConflictAction = null;
            int count = await _files.ImportAsync(batch, CurrentPath, unzip, ResolveConflictAsync, CreateProgress(), token);
            StatusMessage = string.Format(L.S("Sandbox.Imported"), count);
        }, mutation: true);

        private async Task<SandboxConflictAction> ResolveConflictAsync(string name)
        {
            if (_batchConflictAction is { } cached) return cached;
            string? choice = await PopupService.Show<string>(new OptionConfirmPopup(), new OptionConfirmPopupViewModel(
                L.S("Sandbox.ConflictTitle"), string.Format(L.S("Sandbox.ConflictMessage"), name),
                new OptionConfirmOption[][]
                {
                    [new(L.S("Sandbox.KeepBoth"), "keep", isPrimary: true), new(L.S("Sandbox.KeepAll"), "keepAll")],
                    [new(L.S("Sandbox.Skip"), "skip"), new(L.S("Sandbox.SkipAll"), "skipAll")],
                    [new(L.S("Common.Cancel"), "cancel", isDefault: true)],
                }));
            if (_operation?.IsCancellationRequested == true) return SandboxConflictAction.Cancel;
            if (choice == "keepAll") _batchConflictAction = SandboxConflictAction.KeepBoth;
            if (choice == "skipAll") _batchConflictAction = SandboxConflictAction.Skip;
            return choice switch
            {
                "keep" or "keepAll" => SandboxConflictAction.KeepBoth,
                "skip" or "skipAll" => SandboxConflictAction.Skip,
                _ => SandboxConflictAction.Cancel,
            };
        }

        private Task CreateFolderAsync() => RunAsync(async _ =>
        {
            string? name = await PromptNameAsync(L.S("Sandbox.NewFolder"), "");
            if (name == null) { StatusMessage = L.S("Sandbox.Cancelled"); return; }
            _files.CreateDirectory(CurrentPath, name);
        }, mutation: true, cancellable: false);

        private Task RenameAsync(SandboxFileItemViewModel item) => RunAsync(async _ =>
        {
            string? name = await PromptNameAsync(L.S("Sandbox.Rename"), item.Name);
            if (name == null) { StatusMessage = L.S("Sandbox.Cancelled"); return; }
            string destination = _files.Rename(item.Path, name);
            item.IsSelected = false;
            await LoadAsync(CancellationToken.None);
            SandboxFileItemViewModel? renamed = Entries.FirstOrDefault(entry => entry.Path == destination);
            if (renamed != null) renamed.IsSelected = true;
        }, mutation: true, cancellable: false);

        private Task<string?> PromptNameAsync(string title, string value) => PopupService.Show<string>(new TextInputPopup(),
            new TextInputPopupViewModel(title, L.S("Sandbox.NameHint"), L.S("Sandbox.Name"), value, name =>
            {
                string? error = SandboxFileService.ValidateName(name);
                if (error != null) return error;
                bool exists = Directory.EnumerateFileSystemEntries(CurrentPath).Any(path =>
                    Path.GetFileName(path) != value && string.Equals(Path.GetFileName(path), name,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                return name != value && exists
                    ? L.S("Sandbox.AlreadyExists") : null;
            }));

        private Task DeleteAsync(IReadOnlyList<SandboxFileItemViewModel> items) => RunAsync(async token =>
        {
            if (items.Count == 0) return;
            foreach (SandboxFileItemViewModel item in items)
            {
                string? restriction = _files.GetMutationRestriction(item.Path);
                if (restriction != null) throw new IOException(restriction);
            }
            string names = string.Join("\n", items.Take(8).Select(item => item.Name));
            if (items.Count > 8) names += "\n…";
            string? result = await OptionConfirmPopupService.ShowAsync(L.S("Sandbox.DeleteTitle"),
                string.Format(L.S("Sandbox.DeleteMessage"), items.Count, names),
                [new OptionConfirmOption(L.S("Common.Cancel"), "cancel", isDefault: true),
                 new OptionConfirmOption(L.S("Sandbox.Delete"), "delete", isDestructive: true)]);
            if (result != "delete") { StatusMessage = L.S("Sandbox.Cancelled"); return; }
            int completed = 0;
            List<string> failures = [];
            foreach (SandboxFileItemViewModel item in items)
            {
                token.ThrowIfCancellationRequested();
                ProgressText = item.Name;
                try { await _files.DeleteAsync(item.Path, token); completed++; }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) { failures.Add(item.Name + ": " + exception.Message); }
                await Task.Yield();
            }
            StatusMessage = string.Format(L.S("Sandbox.Deleted"), completed);
            if (failures.Count > 0) ErrorMessage = string.Join("\n", failures);
        }, mutation: true);

        private Task ExportAsync(IReadOnlyList<string> paths) => RunAsync(async token =>
        {
            ClearExport();
            _preparedExport = await _files.PrepareExportAsync(paths, CreateProgress(), token);
            HasPreparedExport = true;
            this.RaisePropertyChanged(nameof(PreparedExportName));
        });

        private Task SaveExportAsync() => RunAsync(async token =>
        {
            if (_preparedExport == null) return;
            bool saved = await _transfer.SaveAsync(_preparedExport.FilePath, _preparedExport.Name, token);
            if (saved)
            {
                StatusMessage = L.S(_transfer.StartsDownload ? "Sandbox.DownloadStarted" : "Sandbox.Exported");
                ClearExport();
            }
        }, cancellable: false);

        private IProgress<string> CreateProgress() => new Progress<string>(name =>
        {
            if (IsBusy && !_disposed) ProgressText = name;
        });

        private void ClearExport()
        {
            _preparedExport?.Dispose();
            _preparedExport = null;
            HasPreparedExport = false;
            this.RaisePropertyChanged(nameof(PreparedExportName));
        }

        /// <summary>离开导航栈时释放临时导出、取消令牌和命令订阅。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _operation?.Cancel();
            _languageSubscription?.Dispose();
            ClearExport();
            foreach (SandboxFileItemViewModel item in Entries) item.SelectionChanged -= RefreshSelection;
            BackCommand.Dispose(); RefreshCommand.Dispose(); GoUpCommand.Dispose(); OpenCommand.Dispose();
            ImportFilesCommand.Dispose(); ImportFolderCommand.Dispose(); ImportZipCommand.Dispose(); NewFolderCommand.Dispose();
            RenameCommand.Dispose(); DeleteItemCommand.Dispose(); DeleteSelectedCommand.Dispose(); ExportItemCommand.Dispose();
            ExportSelectedCommand.Dispose(); ExportCurrentCommand.Dispose(); SaveExportCommand.Dispose(); DismissExportCommand.Dispose();
            SelectAllCommand.Dispose(); CancelCommand.Dispose(); RetryPersistenceCommand.Dispose();
            InvertSelectionCommand.Dispose(); ClearSelectionCommand.Dispose(); ExportCommand.Dispose(); CreationCommand.Dispose();
        }
    }

    /// <summary>沙箱列表项，使用独立勾选区同时支持目录导航和批量操作。</summary>
    public sealed class SandboxFileItemViewModel : ViewModelBase
    {
        public string Path { get; }
        public string Name { get; }
        public bool IsDirectory { get; }
        private readonly string _size;
        private readonly DateTime _modifiedAt;
        public string Details => (IsDirectory ? L.S("Sandbox.Folder") : _size) + " · " +
            _modifiedAt.ToString("g", CultureInfo.CurrentCulture);
        public string? Restriction { get; }
        public bool CanMutate => Restriction == null;
        [Reactive] public bool IsSelected { get; set; }
        public event Action? SelectionChanged;

        /// <summary>缓存文件元数据，权限原因只控制破坏性操作。</summary>
        public SandboxFileItemViewModel(FileSystemInfo item, string? restriction)
        {
            Path = item.FullName;
            Name = item.Name;
            IsDirectory = item is DirectoryInfo;
            Restriction = restriction;
            _size = item is FileInfo file ? FormatSize(file.Length) : "";
            _modifiedAt = item.LastWriteTime;
            PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(IsSelected)) SelectionChanged?.Invoke();
            };
        }

        /// <summary>语言资源变更时刷新文件夹类型文字。</summary>
        internal void RefreshLocalization() => this.RaisePropertyChanged(nameof(Details));

        private static string FormatSize(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB"];
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return $"{value:0.#} {units[unit]}";
        }
    }
}