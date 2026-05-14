using System.IO;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 文件打开选择器（OpenFile 模式）。
/// 点击文件 → 返回完整路径；点击目录 → 导航进入。
/// 保留此名称以兼容现有调用方（<see cref="Storage.FilePicker"/>）。
/// </summary>
public class FilePickerPopupViewModel : FilePickerBaseViewModel
{
    public override FilePickerMode Mode => FilePickerMode.OpenFile;

    public FilePickerPopupViewModel(string title, string[] filters, string initialPath = "")
        : base(title, filters, initialPath)
    {
    }

    protected override void SelectItem(FileSystemInfo item)
    {
        if (item is DirectoryInfo dir)
            CurrentPath = dir.FullName;
        else if (item is FileInfo file)
            RaiseClose(file.FullName);
    }
}