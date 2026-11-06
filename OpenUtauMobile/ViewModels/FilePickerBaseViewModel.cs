using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using DynamicData.Binding;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
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

    /// <summary>当前路径下经过过滤的文件和文件夹</summary>
    [Reactive]
    public ObservableCollectionExtended<FileSystemInfo> AllItems { get; set; } = [];

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
    public virtual string SelectionSummary => string.Empty;
    public virtual ReactiveCommand<Unit, Unit>? ConfirmCommand => null;

    /// <summary>单选与多选共用的文件行，多选子类附加勾选状态。</summary>
    public ObservableCollection<FilePickerEntry> Entries { get; } = [];

    public ReactiveCommand<FileSystemInfo, Unit> SelectItemCommand { get; }
    public ReactiveCommand<Unit, Unit> GoUpCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    protected readonly string[] Filters;
    private CancellationTokenSource _cts = new();
    private Dictionary<FileSystemInfo, long> _fileSizes = [];
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
        AllItems.CollectionChanged += (_, _) =>
        {
            Entries.Clear();
            foreach (FileSystemInfo item in AllItems)
            {
                FilePickerEntry entry = CreateEntry(item);
                entry.Size = _fileSizes.TryGetValue(item, out long size) ? size : null;
                Entries.Add(entry);
            }
        };

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
        GoUpCommand = ReactiveCommand.Create(GoUp);
        CloseCommand = ReactiveCommand.Create(RequestBack);
    }

    /// <summary>在后台完成路径检查、枚举和文件元数据读取，UI 仅发布当前请求。</summary>
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
            LocalizedComparer comparer = new(CultureInfo.GetCultureInfo(sortingOrder));
            DirectorySnapshot snapshot = await Task.Run(
                () => ReadDirectory(requestedPath, initialPath, showFiles, comparer, token), token);
            if (token.IsCancellationRequested || IsClosed) return;
            _publishingPath = true;
            try { CurrentPath = snapshot.Path; }
            finally { _publishingPath = false; }
            IsDirectoryAvailable = snapshot.Exists;
            _fileSizes = snapshot.Sizes;
            AllItems.Load(snapshot.Items);

            if (snapshot.Items.Length == 0)
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
            AllItems.Clear();
            EmptyStateMessage = L.S("FilePicker.AccessDenied");
            IsAccessDenied = true;
            ShowEmptyState = true;
            Log.Warning("无权访问目录：{Path}", CurrentPath);
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested || IsClosed) return;
            AllItems.Clear();
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

    /// <summary>后台读取目录快照，避免行绑定再次访问文件系统。</summary>
    private DirectorySnapshot ReadDirectory(string path, bool initialPath, bool showFiles,
        IComparer<string> comparer, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (FilePickerWorkspace.IsSandboxed) path = FilePickerWorkspace.NormalizeDirectory(path);
        if (initialPath && !Directory.Exists(path)) path = DefaultDirectory;
        bool exists = Directory.Exists(path);
        if (!exists) return new(path, false, [], [], false);

        DirectoryInfo directory = new(path);
        List<DirectoryInfo> directories = [];
        foreach (DirectoryInfo child in directory.EnumerateDirectories())
        {
            token.ThrowIfCancellationRequested();
            directories.Add(child);
        }
        List<FileInfo> files = [];
        Dictionary<FileSystemInfo, long> sizes = [];
        bool hasAnyContent = directories.Count > 0;
        foreach (FileInfo file in directory.EnumerateFiles())
        {
            token.ThrowIfCancellationRequested();
            hasAnyContent = true;
            if (!showFiles) break;
            if (!MatchesFilter(file.Name)) continue;
            files.Add(file);
            try { sizes[file] = file.Length; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        token.ThrowIfCancellationRequested();
        return new(path, true,
            [.. directories.OrderBy(item => item.Name, comparer), .. files.OrderBy(item => item.Name, comparer)],
            sizes, hasAnyContent);
    }

    /// <summary>默认浏览目录仅做路径计算，不在 UI 检查磁盘。</summary>
    private static string DefaultDirectory => FilePickerWorkspace.IsSandboxed ? FilePickerWorkspace.Root
        : OperatingSystem.IsAndroid() ? "/sdcard"
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>目录内容及预读的文件大小，交给 UI 时不再依赖文件句柄。</summary>
    private sealed record DirectorySnapshot(string Path, bool Exists, FileSystemInfo[] Items,
        Dictionary<FileSystemInfo, long> Sizes, bool HasAnyContent);

    /// <summary>关闭选择器并停止发布正在读取的目录结果。</summary>
    protected void ClosePicker(object? result)
    {
        if (IsClosed) return;
        IsClosed = true;
        _cts.Cancel();
        RaiseClose(result);
    }

    public override void RequestBack() => ClosePicker(null);

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

    /// <summary>SaveFile 模式：覆盖同名文件时显示的警告消息。</summary>
    public virtual string WarningMessage { get; set; } = string.Empty;

    /// <summary>SaveFile 模式：是否显示覆盖警告。</summary>
    public virtual bool IsShowingWarningMessage { get; set; }

    // ── 点击条目时的确认逻辑，由子类实现 ────────────────────────────────
    /// <summary>点击条目时的确认逻辑，由子类实现。</summary>
    protected abstract void SelectItem(FileSystemInfo item);

    protected virtual FilePickerEntry CreateEntry(FileSystemInfo item) => new(item);


    private void GoUp()
    {
        if (FilePickerWorkspace.IsSandboxed && CurrentPath == FilePickerWorkspace.Root) return;
        DirectoryInfo? parentDir = Directory.GetParent(CurrentPath);
        if (parentDir != null)
            CurrentPath = parentDir.FullName;
    }
}
