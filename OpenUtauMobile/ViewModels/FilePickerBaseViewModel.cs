using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using DynamicData.Binding;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
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

    public ReactiveCommand<FileSystemInfo, Unit> SelectItemCommand { get; }
    public ReactiveCommand<Unit, Unit> GoUpCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    protected readonly string[] Filters;
    private CancellationTokenSource _cts = new();

    /// <summary>
    /// 仅供设计器使用
    /// </summary>
    protected FilePickerBaseViewModel() : this("文件浏览器", []) { }

    protected FilePickerBaseViewModel(string title, string[] filters, string initialPath = "")
    {
        Title = title;
        Filters = filters;

        if (!Directory.Exists(initialPath))
            initialPath = string.Empty;

        if (!string.IsNullOrEmpty(initialPath))
            CurrentPath = initialPath;
        else
            CurrentPath = OperatingSystem.IsAndroid()
                ? "/sdcard"
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // 构造时先置为加载中，等 View 附加到视觉树后再真正开始 I/O
        IsLoading = true;

        this.WhenAnyValue(x => x.IsLoading)
            .Subscribe(loading =>
            {
                if (loading) ShowEmptyState = false;
            });

        this.WhenAnyValue(x => x.CurrentPath)
            .Subscribe(_ => RefreshItemsAsync().ConfigureAwait(false));

        SelectItemCommand = ReactiveCommand.Create<FileSystemInfo>(SelectItem);
        GoUpCommand = ReactiveCommand.Create(GoUp);
        CloseCommand = ReactiveCommand.Create(RequestBack);
    }

    public async Task RefreshItemsAsync()
    {
        if (!Directory.Exists(CurrentPath)) return;

        await _cts.CancelAsync();
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        IsLoading = true;
        IsAccessDenied = false;

        try
        {
            DirectoryInfo currentDir = new(CurrentPath);

            List<FileSystemInfo> items = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                IEnumerable<DirectoryInfo> dirs = currentDir.GetDirectories()
                    .LocalizedOrderBy(d => d.Name);
                IEnumerable<FileSystemInfo> result = dirs;
                if (ShowFiles)
                {
                    IEnumerable<FileInfo> files = currentDir.GetFiles()
                        .Where(f => MatchesFilter(f.Name))
                        .LocalizedOrderBy(f => f.Name);
                    result = result.Concat(files);
                }

                return result.ToList();
            }, token);

            if (token.IsCancellationRequested) return;

            AllItems.Load(items);

            if (items.Count == 0)
            {
                bool hasAnyContent = await Task.Run(() =>
                        currentDir.GetFiles().Length > 0 || currentDir.GetDirectories().Length > 0,
                    token);

                if (token.IsCancellationRequested) return;

                EmptyStateMessage = hasAnyContent
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
            if (token.IsCancellationRequested) return;
            AllItems.Clear();
            EmptyStateMessage = L.S("FilePicker.AccessDenied");
            IsAccessDenied = true;
            ShowEmptyState = true;
            Log.Warning("无权访问目录：{Path}", CurrentPath);
        }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            AllItems.Clear();
            EmptyStateMessage = L.S("FilePicker.AccessDenied");
            IsAccessDenied = true;
            ShowEmptyState = true;
            Log.Error(ex, "刷新文件列表时失败");
        }
        finally
        {
            if (!token.IsCancellationRequested)
                IsLoading = false;
        }
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

    /// <summary>SaveFile 模式：覆盖同名文件时显示的警告消息。</summary>
    public virtual string WarningMessage { get; set; } = string.Empty;

    /// <summary>SaveFile 模式：是否显示覆盖警告。</summary>
    public virtual bool IsShowingWarningMessage { get; set; }

    // ── 点击条目时的确认逻辑，由子类实现 ────────────────────────────────
    /// <summary>点击条目时的确认逻辑，由子类实现。</summary>
    protected abstract void SelectItem(FileSystemInfo item);


    private void GoUp()
    {
        DirectoryInfo? parentDir = Directory.GetParent(CurrentPath);
        if (parentDir != null)
            CurrentPath = parentDir.FullName;
    }
}