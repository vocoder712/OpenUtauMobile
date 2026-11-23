using System;
using System.IO;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>共用文件行的数据及可选的多选状态。</summary>
public sealed class FilePickerEntry : ReactiveObject
{
    private bool isSelected;
    private readonly Action<string, bool>? selectionChanged;

    public FileSystemInfo Item { get; }
    public string Name => Item.Name;
    /// <summary>目录读取阶段预取的大小；文件消失或无法读取时为空。</summary>
    public long? Size { get; internal set; }
    public bool CanSelect => Item is FileInfo && selectionChanged != null;

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (!CanSelect || isSelected == value)
            {
                return;
            }
            this.RaiseAndSetIfChanged(ref isSelected, value);
            selectionChanged?.Invoke(Item.FullName, value);
        }
    }

    public FilePickerEntry(FileSystemInfo item, bool selected = false,
        Action<string, bool>? selectionChanged = null)
    {
        Item = item;
        isSelected = selected;
        this.selectionChanged = selectionChanged;
    }
}
