using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels;

public enum FilePickerMode
{
    OpenFile,
    OpenFolder,
    SaveFile,
}

/// <summary>
/// 文件/文件夹选择器弹窗的公共基类。
/// 包含目录浏览、异步加载、空状态/错误状态等所有通用逻辑。
/// 子类仅需实现 <see cref="SelectItem"/> 和 <see cref="Mode"/>。
/// </summary>
public abstract class FilePickerBaseViewModel : PopupViewModelBase
{
    [Reactive] public string Title { get; set; }

    [Reactive] public string CurrentPath { get; set; }

    /// <summary>正在加载目录内容</summary>
    [Reactive]
    public bool IsLoading { get; set; }

    /// <summary>后台检查确认当前目录存在，供选择和保存命令使用。</summary>
    [Reactive]
    public bool IsDirectoryAvailable { get; private set; }

    /// <summary>是否显示空状态面板（无内容或异常时）</summary>
    [Reactive]
    public bool ShowEmptyState { get; set; }

    /// <summary>空状态提示消息（绑定到 UI 标签）</summary>
    [Reactive]
    public string EmptyStateMessage { get; set; } = string.Empty;

    /// <summary>是否因权限错误导致空状态（驱动锁图标 vs 空文件夹图标）</summary>
    [Reactive]
    public bool IsAccessDenied { get; set; }

    /// <summary>当前选择器模式，供 View 绑定以切换底部工具条。</summary>
    public abstract FilePickerMode Mode { get; }

    public virtual bool IsMultiSelect => false;
    public bool HasFooter => IsMultiSelect || Mode != FilePickerMode.OpenFile;
    public bool SupportsCreateFolder => Mode is FilePickerMode.OpenFolder or FilePickerMode.SaveFile;
    public virtual string SelectionSummary => string.Empty;
    public virtual ReactiveCommand<Unit, Unit>? ConfirmCommand => null;

    /// <summary>后台准备并一次性发布的当前目录文件行。</summary>
    [Reactive]
    public IReadOnlyList<FilePickerEntry> Entries { get; private set; } = [];

    public ReactiveCommand<FileSystemInfo, Unit> SelectItemCommand { get; }
    public ReactiveCommand<Unit, Unit> GoUpCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    /// <summary>在当前目录新建文件夹。</summary>
    public ReactiveCommand<Unit, Unit> CreateFolderCommand { get; }

    /// <summary>目录已加载且没有正在处理的保存或新建操作。</summary>
    protected IObservable<bool> DirectoryActionsAvailable { get; }

    [Reactive]
    private bool IsHandlingDirectoryAction { get; set; }

    protected readonly string[] Filters;
    private CancellationTokenSource _cts = new();
    private bool _initialPathPending = true;
    private bool _publishingPath;

    /// <summary>选择器关闭后不再接收晚到的磁盘查询结果。</summary>
    protected bool IsClosed { get; private set; }

    /// <summary>
    /// 仅供设计器使用
    /// </summary>
    protected FilePickerBaseViewModel() : this("文件浏览器", []) { }

    protected FilePickerBaseViewModel(string title, string[] filters, string initialPath = "")
    {
        Title = title;
        Filters = [.. filters];

        if (!string.IsNullOrEmpty(initialPath))
            CurrentPath = initialPath;
        else
            CurrentPath = DefaultDirectory;

        // 构造时立即显示加载状态，目录 I/O 由下方异步请求在后台执行。
        IsLoading = true;

        this.WhenAnyValue(x => x.IsLoading)
            .Subscribe(loading =>
            {
                if (loading) ShowEmptyState = false;
            });

        this.WhenAnyValue(x => x.CurrentPath)
            .Subscribe(path =>
            {
                if (!_publishingPath) _ = RefreshItemsAsync();
            });

        SelectItemCommand = ReactiveCommand.Create<FileSystemInfo>(SelectItem);
        GoUpCommand = ReactiveCommand.Create(() => { TryGoUp(); });
        CloseCommand = ReactiveCommand.Create(() => ClosePicker(null));
        DirectoryActionsAvailable = this.WhenAnyValue(x => x.IsDirectoryAvailable, x => x.IsLoading,
            x => x.IsHandlingDirectoryAction, (available, loading, handling) => available && !loading && !handling);
        CreateFolderCommand = ReactiveCommand.CreateFromTask(
            () => RunDirectoryActionAsync(CreateFolderAsync), DirectoryActionsAvailable);
    }

    /// <summary>同一选择器内的保存与新建依次处理，确保子弹窗只响应各自的确认。</summary>
    protected async Task RunDirectoryActionAsync(Func<Task> action)
    {
        if (IsClosed || IsHandlingDirectoryAction) return;
        IsHandlingDirectoryAction = true;
        try { await action(); }
        finally { IsHandlingDirectoryAction = false; }
    }

    /// <summary>接收单级目录名，在后台创建后刷新原目录。</summary>
    private async Task CreateFolderAsync()
    {
        string directory = CurrentPath;
        string? name = await TextInputPopupService.ShowAsync(L.S("Sandbox.NewFolder"), string.Empty,
            L.S("Sandbox.Name"), validate: SandboxFileService.ValidateName);
        if (name == null || IsClosed || CurrentPath != directory) return;

        string? error = null;
        try
        {
            if (!await Task.Run(() => TryCreateFolder(directory, name)))
                error = L.S("Sandbox.AlreadyExists");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
        }

        if (error != null)
        {
            if (!IsClosed)
                await OptionConfirmPopupService.ShowAsync(L.S("FilePicker.CreateFolderFailed"), error,
                    [new OptionConfirmOption(L.S("Common.Confirm"), "ok", isPrimary: true, isDefault: true)]);
            return;
        }

        ToastService.Enqueue(string.Format(L.S("FilePicker.FolderCreated"), name));
        if (!IsClosed && CurrentPath == directory) await RefreshItemsAsync();
    }

    /// <summary>检查文件和目录的同名冲突，再创建单级子目录。</summary>
    private static bool TryCreateFolder(string directory, string name)
    {
        if (FilePickerWorkspace.IsSandboxed) directory = new SandboxFileService(() => false).Resolve(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);

        string path = Path.Combine(directory, name);
        if (Path.Exists(path)) return false;
        try { Directory.CreateDirectory(path); }
        catch (IOException) when (Path.Exists(path)) { return false; }
        return true;
    }

    /// <summary>在后台准备排序后的目录行，UI 仅发布当前请求的结果。</summary>
    public async Task RefreshItemsAsync()
    {
        if (IsClosed) return;
        // 先取消旧目录请求，路径无效时也不能让旧列表重新出现。
        CancellationTokenSource previous = _cts;
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        string requestedPath = CurrentPath;
        bool initialPath = _initialPathPending;
        _initialPathPending = false;
        bool showFiles = ShowFiles;
        IsLoading = true;
        IsDirectoryAvailable = false;
        IsAccessDenied = false;
        await previous.CancelAsync();
        previous.Dispose();
        if (token.IsCancellationRequested) return;

        try
        {
            // 后台排序使用本次请求的文化快照，不读取随后可能变化的全局偏好。
            string sortingOrder = Preferences.Default.SortingOrder
                ?? LocalizationManager.ResolveLanguagePreference(Preferences.Default.Language);
            DirectorySnapshot snapshot = await Task.Run(
                () => ReadDirectory(requestedPath, initialPath, showFiles, sortingOrder, token), token);
            if (token.IsCancellationRequested || IsClosed) return;
            _publishingPath = true;
            try { CurrentPath = snapshot.Path; }
            finally { _publishingPath = false; }
            IsDirectoryAvailable = snapshot.Exists;
            Entries = snapshot.Entries;

            if (snapshot.Entries.Length == 0)
            {
                EmptyStateMessage = snapshot.HasAnyContent
                    ? L.S("FilePicker.NoMatch")
                    : L.S("FilePicker.Empty");
                ShowEmptyState = true;
            }
            else
            {
                ShowEmptyState = false;
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消，不处理
        }
        catch (UnauthorizedAccessException)
        {
            if (token.IsCancellationRequested || IsClosed) return;
            Entries = [];
            EmptyStateMessage = L.S("FilePicker.AccessDenied");
            IsAccessDenied = true;
            ShowEmptyState = true;
            Log.Warning("无权访问目录：{Path}", CurrentPath);
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || IsClosed) return;
            Entries = [];
            EmptyStateMessage = L.S("FilePicker.AccessDenied");
            IsAccessDenied = true;
            ShowEmptyState = true;
            Log.Error(ex, "刷新文件列表时失败");
        }
        finally
        {
            if (!token.IsCancellationRequested && !IsClosed)
                IsLoading = false;
        }
    }

    /// <summary>后台遍历目录并准备文件行，避免行绑定再次访问文件系统。</summary>
    private DirectorySnapshot ReadDirectory(string path, bool initialPath, bool showFiles,
        string sortingOrder, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (FilePickerWorkspace.IsSandboxed) path = FilePickerWorkspace.NormalizeDirectory(path);
        bool exists = Directory.Exists(path);
        if (initialPath && !exists)
        {
            path = DefaultDirectory;
            exists = Directory.Exists(path);
        }
        if (!exists) return new(path, false, [], false);

        List<FilePickerEntry> directories = [];
        List<FilePickerEntry> files = [];
        bool hasAnyContent = false;
        foreach (FileSystemInfo item in new DirectoryInfo(path).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            hasAnyContent = true;
            if (item is DirectoryInfo)
            {
                directories.Add(CreateEntry(item, null));
                continue;
            }
            if (!showFiles || item is not FileInfo file || !MatchesFilter(file.Name)) continue;

            long? size = null;
            try { size = file.Length; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            files.Add(CreateEntry(file, size));
        }
        token.ThrowIfCancellationRequested();
        LocalizedComparer comparer = new(CultureInfo.GetCultureInfo(sortingOrder));
        FilePickerEntry[] entries =
        [
            .. directories.OrderBy(entry => entry.Name, comparer),
            .. files.OrderBy(entry => entry.Name, comparer),
        ];
        return new(path, true, entries, hasAnyContent);
    }

    /// <summary>默认浏览目录仅做路径计算，不在 UI 检查磁盘。</summary>
    private static string DefaultDirectory => FilePickerWorkspace.IsSandboxed ? FilePickerWorkspace.Root
        : OperatingSystem.IsAndroid() ? "/sdcard"
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>准备完毕并可直接绑定的目录快照。</summary>
    private sealed record DirectorySnapshot(string Path, bool Exists, FilePickerEntry[] Entries, bool HasAnyContent);

    /// <summary>关闭选择器并停止发布正在读取的目录结果。</summary>
    protected void ClosePicker(object? result)
    {
        if (IsClosed) return;
        IsClosed = true;
        _cts.Cancel();
        RaiseClose(result);
    }

    /// <summary>Android 系统返回优先导航到上级目录。</summary>
    public override void RequestBack()
    {
        if (OperatingSystem.IsAndroid() && TryGoUp()) return;
        ClosePicker(null);
    }

    /// <summary>
    /// 子类可重写此属性以控制列表中是否显示文件。
    /// 文件夹模式设为 false，仅显示子目录。
    /// </summary>
    protected virtual bool ShowFiles => true;

    /// <summary>
    /// 判断文件名是否匹配任意一个 filter（如 "*.ustx"）。
    /// Filters 为空时视为"匹配一切"。
    /// </summary>
    protected bool MatchesFilter(string fileName)
    {
        if (Filters.Length == 0) return true;
        return Filters.Any(f =>
        {
            if (f.StartsWith("*.", StringComparison.Ordinal))
                return fileName.EndsWith(f[1..], StringComparison.OrdinalIgnoreCase);
            return string.Equals(fileName, f, StringComparison.OrdinalIgnoreCase);
        });
    }

    // ── 子类专属命令/属性的存根（供编译型 XAML 绑定解析；子类覆写实现） ─────────

    /// <summary>
    /// OpenFolder 模式：选择当前目录命令。
    /// <see cref="FolderPickerViewModel"/> 覆写；其余子类保持 null（按钮不可见）。
    /// </summary>
    public virtual ReactiveCommand<Unit, Unit>? SelectCurrentFolderCommand => null;

    /// <summary>
    /// SaveFile 模式：执行保存命令。
    /// <see cref="FileSavePickerViewModel"/> 覆写；其余子类保持 null。
    /// </summary>
    public virtual ReactiveCommand<Unit, Unit>? SaveCommand => null;

    /// <summary>SaveFile 模式：用户输入的文件名（不含后缀）。</summary>
    public virtual string FileName
    {
        get => string.Empty;
        set => _ = value; // stub — overridden by FileSavePickerViewModel
    }

    /// <summary>SaveFile 模式：强制后缀（含点）。</summary>
    public virtual string Extension => string.Empty;

    /// <summary>SaveFile 模式：是否提示文件名格式无效。</summary>
    public virtual bool HasInvalidFileName { get; protected set; }

    // ── 点击条目时的确认逻辑，由子类实现 ────────────────────────────────
    /// <summary>点击条目时的确认逻辑，由子类实现。</summary>
    protected abstract void SelectItem(FileSystemInfo item);

    /// <summary>在后台创建文件行。勾选状态只在 UI 绑定时读取。</summary>
    protected virtual FilePickerEntry CreateEntry(FileSystemInfo item, long? size) => new(item, size);


    /// <summary>导航到上级目录，并返回是否成功切换路径。</summary>
    private bool TryGoUp()
    {
        if (string.IsNullOrWhiteSpace(CurrentPath) ||
            (FilePickerWorkspace.IsSandboxed && CurrentPath == FilePickerWorkspace.Root)) return false;
        DirectoryInfo? parentDir = Directory.GetParent(CurrentPath);
        if (parentDir == null) return false;

        CurrentPath = parentDir.FullName;
        return true;
    }
}
