using System.IO;
using System.Reactive;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 文件夹选择器（OpenFolder 模式）。
/// 列表只显示子目录；底部工具条有"选择此文件夹"按钮，点击后返回 CurrentPath。
/// </summary>
public class FolderPickerViewModel : FilePickerBaseViewModel
{
    public override FilePickerMode Mode => FilePickerMode.OpenFolder;

    /// <summary>文件夹模式不显示文件条目。</summary>
    protected override bool ShowFiles => false;

    /// <summary>选择当前目录命令，绑定到底部"选择此文件夹"按钮。</summary>
    public override ReactiveCommand<Unit, Unit> SelectCurrentFolderCommand { get; }

    public FolderPickerViewModel(string title, string initialPath = "")
        : base(title, filters: [], initialPath)
    {
        SelectCurrentFolderCommand = ReactiveCommand.Create(SelectCurrentFolder);
    }

    protected override void SelectItem(FileSystemInfo item)
    {
        // 文件夹模式：列表中本就不显示文件；点击目录则导航进入
        if (item is DirectoryInfo dir)
            CurrentPath = dir.FullName;
    }

    private void SelectCurrentFolder()
        => RaiseClose(CurrentPath);
}