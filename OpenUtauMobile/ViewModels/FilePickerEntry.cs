using System;
using System.IO;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>共用文件行的数据及可选的多选状态。</summary>
public sealed class FilePickerEntry : ReactiveObject
{
    private readonly Func<string, bool>? getSelection;
    private readonly Action<string, bool>? selectionChanged;

    public FileSystemInfo Item { get; }
    public string Name => Item.Name;
    /// <summary>目录读取阶段预取的大小；文件消失或无法读取时为空。</summary>
    public long? Size { get; }
    public bool CanSelect => selectionChanged != null;

    /// <summary>绑定时读取当前勾选状态，使回收后的行仍与跨目录选择集合一致。</summary>
    public bool IsSelected
    {
        get => getSelection?.Invoke(Item.FullName) ?? false;
        set
        {
            if (selectionChanged == null || IsSelected == value)
            {
                return;
            }
            selectionChanged(Item.FullName, value);
            this.RaisePropertyChanged();
        }
    }

    /// <summary>保存后台取得的元数据和 UI 可用的勾选回调。</summary>
    public FilePickerEntry(FileSystemInfo item, long? size, Func<string, bool>? getSelection = null,
        Action<string, bool>? selectionChanged = null)
    {
        Item = item;
        Size = size;
        this.getSelection = getSelection;
        this.selectionChanged = selectionChanged;
    }
}
