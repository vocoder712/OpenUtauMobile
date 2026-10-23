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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels
{
    /// <summary>应用沙箱管理页面，串行处理操作并区分文件修改和持久化结果。</summary>
    public sealed class SandboxFileManagerViewModel : NavigateViewModelBase, IDisposable
    {
        private readonly SandboxFileService _files;
        private readonly ISandboxFileTransferService _transfer;
        private readonly IDisposable? _languageSubscription;
        private CancellationTokenSource? _operation;
        private bool _disposed;
        private TaskCompletionSource? _operationCompletion;
        private Task<bool>? _leaveDecision;
        private PopupViewModelBase? _operationPopup;
        private bool _resultPending;
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
        /// <summary>导航器在移除页面前检查任务和弹窗是否仍归本页所有。</summary>
        public bool CanDetach => !IsBusy && !NeedsPersistence && _leaveDecision == null && _operationPopup == null;

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
            SelectAllCommand = ReactiveCommand.Create(() => UpdateSelection(_ => true), idle);
            InvertSelectionCommand = ReactiveCommand.Create(() => UpdateSelection(item => !item.IsSelected), idle);
            ClearSelectionCommand = ReactiveCommand.Create(() => UpdateSelection(_ => false), idle);
            CancelCommand = ReactiveCommand.Create(() => _operation?.Cancel(), this.WhenAnyValue(model => model.CanCancel));
            RetryPersistenceCommand = ReactiveCommand.CreateFromTask(RetryPersistenceAsync,
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
                    this.RaisePropertyChanged(nameof(CanModify));
                }
                if (args.PropertyName is nameof(IsBusy) or nameof(IsLoading))
                    this.RaisePropertyChanged(nameof(ShowOperationProgress));
            };
            _languageSubscription = Application.Current?.GetResourceObservable("Sandbox.Root")
                .Where(value => value is string).Select(value => (string)value!).DistinctUntilChanged().Subscribe(_ =>
                {
                    this.RaisePropertyChanged(nameof(RelativePath));
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

        /// <summary>返回先关闭 FAB；有任务时由通用选项弹窗确认，并等待任务收尾后离开。</summary>
        public override void OnBackRequested()
        {
            if (IsCreationMenuOpen)
            {
                IsCreationMenuOpen = false;
                return;
            }
            _ = LeaveAsync();
        }

        private async Task LeaveAsync()
        {
            if (await ConfirmLeaveAsync()) Navigator.NavigateBack(this);
        }

        /// <summary>所有导航出口共用一次离开决策；取消后等待流、暂存及持久化清理完成。</summary>
        public async Task<bool> ConfirmLeaveAsync()
        {
            if (_leaveDecision != null) return await _leaveDecision;
            if (_operationPopup != null) return false;
            TaskCompletionSource<bool> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _leaveDecision = decision.Task;
            bool leave = false;
            try
            {
                if (IsBusy)
                {
                    Task completion = _operationCompletion?.Task ?? Task.CompletedTask;
                    string? choice = await OptionConfirmPopupService.ShowAsync(L.S("Sandbox.StopTitle"), L.S("Sandbox.StopMessage"),
                        [new OptionConfirmOption(L.S("Sandbox.ContinueTask"), "continue", isDefault: true),
                         new OptionConfirmOption(L.S("Sandbox.StopAndLeave"), "stop", isDestructive: true)]);
                    if (choice != "stop") return false;
                    bool cancel = CanCancel;
                    CanCancel = false;
                    ProgressText = L.S("Sandbox.Stopping");
                    if (cancel) _operation?.Cancel();
                    await completion;
                }
                leave = !NeedsPersistence && string.IsNullOrEmpty(ErrorMessage);
                if (!leave) _resultPending = true;
                else _resultPending = false;
            }
            catch (Exception exception)
            {
                Log.Error(exception, "沙箱页面退出确认失败");
                ErrorMessage = string.Format(L.S("Sandbox.OperationFailed"), exception.Message);
                _resultPending = true;
            }
            finally
            {
                _leaveDecision = null;
                decision.TrySetResult(leave);
                if (!leave) await ShowPendingResultAsync();
            }
            return leave;
        }

        /// <summary>统一处理取消、部分失败、持久化和离开页面的时序。</summary>
        private async Task RunAsync(Func<CancellationToken, Task> action, bool mutation = false, bool cancellable = true)
        {
            if (IsBusy || _disposed || _leaveDecision != null || _operationPopup != null) return;
            TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _operationCompletion = completion;
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
                _resultPending = NeedsPersistence || !string.IsNullOrEmpty(ErrorMessage) || !string.IsNullOrEmpty(StatusMessage);
                completion.TrySetResult();
            }
            if (_leaveDecision == null && !_disposed) await ShowPendingResultAsync();
        }

        /// <summary>操作结果只显示一次；保存失败在同一通用弹窗内提供重试入口。</summary>
        private async Task ShowPendingResultAsync()
        {
            if (!_resultPending || _disposed || _operationPopup != null) return;
            _resultPending = false;
            string message = string.Join("\n\n", new[] { StatusMessage, ErrorMessage,
                NeedsPersistence ? L.S("Sandbox.PersistenceFailed") : "" }.Where(value => !string.IsNullOrEmpty(value)));
            OptionConfirmOption[] options = NeedsPersistence
                ? [new(L.S("Common.Close"), "close", isDefault: true), new(L.S("Sandbox.RetrySave"), "retry", isPrimary: true)]
                : [new(L.S("Common.Close"), "close", isPrimary: true, isDefault: true)];
            string? result = await ShowOperationPopupAsync<string>(new OptionConfirmPopup(),
                new OptionConfirmPopupViewModel(L.S("Sandbox.Title"), message, options), CancellationToken.None);
            // 结果已经由弹窗展示，不在页面或下一次返回中驻留。
            ErrorMessage = "";
            StatusMessage = "";
            if (result == "retry") await RetryPersistenceAsync();
        }

        /// <summary>重试只保存现有修改，不重复导入或删除，并通过同一弹窗报告结果。</summary>
        private Task RetryPersistenceAsync() => RunAsync(async _ =>
        {
            await FlushAsync();
            if (!NeedsPersistence) StatusMessage = L.S("Sandbox.Completed");
        }, cancellable: false);

        /// <summary>任务取消时关闭本任务持有的弹窗，并等待真实关闭而不遗留悬挂的等待任务。</summary>
        private async Task<T?> ShowOperationPopupAsync<T>(ContentControl view, PopupViewModelBase model, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            _operationPopup = model;
            try
            {
                Task<T?> shown = PopupService.Show<T>(view, model);
                using CancellationTokenRegistration registration = token.Register(() => Dispatcher.UIThread.Post(() =>
                {
                    if (_operationPopup == model) model.RequestBack();
                }));
                return await shown;
            }
            finally { _operationPopup = null; }
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
                SandboxFileItemViewModel[] items = await Task.Run(() => _files.List(path, cancellationToken).Select(item =>
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
            token.ThrowIfCancellationRequested();
            if (batch == null) { StatusMessage = L.S("Sandbox.Cancelled"); return; }
            _batchConflictAction = null;
            int count = await _files.ImportAsync(batch, CurrentPath, unzip, ResolveConflictAsync, CreateProgress(), token);
            StatusMessage = string.Format(L.S("Sandbox.Imported"), count);
        }, mutation: true);

        private async Task<SandboxConflictAction> ResolveConflictAsync(string name)
        {
            if (_batchConflictAction is { } cached) return cached;
            string? choice = await ShowOperationPopupAsync<string>(new OptionConfirmPopup(), new OptionConfirmPopupViewModel(
                L.S("Sandbox.ConflictTitle"), string.Format(L.S("Sandbox.ConflictMessage"), name),
                new OptionConfirmOption[][]
                {
                    [new(L.S("Sandbox.KeepBoth"), "keep", isPrimary: true), new(L.S("Sandbox.KeepAll"), "keepAll")],
                    [new(L.S("Sandbox.Skip"), "skip"), new(L.S("Sandbox.SkipAll"), "skipAll")],
                    [new(L.S("Common.Cancel"), "cancel", isDefault: true)],
                }), _operation?.Token ?? CancellationToken.None);
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

        private Task CreateFolderAsync() => RunAsync(async token =>
        {
            string? name = await PromptNameAsync(L.S("Sandbox.NewFolder"), "", token);
            token.ThrowIfCancellationRequested();
            if (name == null) { StatusMessage = L.S("Sandbox.Cancelled"); return; }
            _files.CreateDirectory(CurrentPath, name);
        }, mutation: true);

        private Task RenameAsync(SandboxFileItemViewModel item) => RunAsync(async token =>
        {
            string? name = await PromptNameAsync(L.S("Sandbox.Rename"), item.Name, token);
            token.ThrowIfCancellationRequested();
            if (name == null) { StatusMessage = L.S("Sandbox.Cancelled"); return; }
            string destination = _files.Rename(item.Path, name);
            item.IsSelected = false;
            await LoadAsync(token);
            SandboxFileItemViewModel? renamed = Entries.FirstOrDefault(entry => entry.Path == destination);
            if (renamed != null) renamed.IsSelected = true;
        }, mutation: true);

        private Task<string?> PromptNameAsync(string title, string value, CancellationToken token) => ShowOperationPopupAsync<string>(new TextInputPopup(),
            new TextInputPopupViewModel(title, L.S("Sandbox.NameHint"), L.S("Sandbox.Name"), value, name =>
            {
                string? error = SandboxFileService.ValidateName(name);
                if (error != null) return error;
                bool exists = Directory.EnumerateFileSystemEntries(CurrentPath).Any(path =>
                    Path.GetFileName(path) != value && string.Equals(Path.GetFileName(path), name,
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
                return name != value && exists
                    ? L.S("Sandbox.AlreadyExists") : null;
            }), token);

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
            string? result = await ShowOperationPopupAsync<string>(new OptionConfirmPopup(), new OptionConfirmPopupViewModel(L.S("Sandbox.DeleteTitle"),
                string.Format(L.S("Sandbox.DeleteMessage"), items.Count, names),
                [new OptionConfirmOption(L.S("Common.Cancel"), "cancel", isDefault: true),
                 new OptionConfirmOption(L.S("Sandbox.Delete"), "delete", isDestructive: true)]), token);
            token.ThrowIfCancellationRequested();
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
            await using SandboxPreparedExport prepared = await _files.PrepareExportAsync(paths, CreateProgress(), token);
            OptionConfirmPopupViewModel prompt = new(L.S("Sandbox.Export"), L.S("Sandbox.ExportReady") + "\n\n" + prepared.Name,
                [new OptionConfirmOption(L.S("Common.Cancel"), "cancel", isDefault: true),
                 new OptionConfirmOption(L.S("Sandbox.SaveToDevice"), "save", isPrimary: true)]);
            Task<bool>? download = null;
            // 浏览器下载从选项按钮的原始点击直接发起，保留用户手势授权。
            void StartDownload(object? sender, object? result)
            {
                if (!_transfer.StartsDownload || result is not "save" || token.IsCancellationRequested) return;
                try { download = _transfer.SaveAsync(prepared.FilePath, prepared.Name, token); }
                catch (Exception exception) { download = Task.FromException<bool>(exception); }
            }
            prompt.ClosingEvent += StartDownload;
            string? choice;
            try { choice = await ShowOperationPopupAsync<string>(new OptionConfirmPopup(), prompt, token); }
            finally { prompt.ClosingEvent -= StartDownload; }
            token.ThrowIfCancellationRequested();
            if (choice != "save") return;
            // 原生保存可能正在覆盖设备文件，进入该步骤后等待完成，避免留下半份外部文件。
            CanCancel = false;
            bool saved = await (download ?? _transfer.SaveAsync(prepared.FilePath, prepared.Name, token));
            if (saved) StatusMessage = L.S(_transfer.StartsDownload ? "Sandbox.DownloadStarted" : "Sandbox.Exported");
        });

        private IProgress<string> CreateProgress() => new Progress<string>(name =>
        {
            if (IsBusy && !_disposed) ProgressText = name;
        });

        /// <summary>离开导航栈时释放临时导出、取消令牌和命令订阅。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _operation?.Cancel();
            _languageSubscription?.Dispose();
            _operationPopup?.RequestBack();
            foreach (SandboxFileItemViewModel item in Entries) item.SelectionChanged -= RefreshSelection;
            BackCommand.Dispose(); RefreshCommand.Dispose(); GoUpCommand.Dispose(); OpenCommand.Dispose();
            ImportFilesCommand.Dispose(); ImportFolderCommand.Dispose(); ImportZipCommand.Dispose(); NewFolderCommand.Dispose();
            RenameCommand.Dispose(); DeleteItemCommand.Dispose(); DeleteSelectedCommand.Dispose(); ExportItemCommand.Dispose();
            ExportSelectedCommand.Dispose(); ExportCurrentCommand.Dispose();
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