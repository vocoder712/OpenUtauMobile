using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 文件保存选择器（SaveFile 模式）。
/// 用户浏览目录以选择目标文件夹，底部输入文件名（后缀固定）；
/// 点击"保存"返回 Path.Combine(CurrentPath, FileName + Extension)。
/// </summary>
public class FileSavePickerViewModel : FilePickerBaseViewModel
{
    public override FilePickerMode Mode => FilePickerMode.SaveFile;

    /// <summary>用户输入的文件名（不含后缀）。</summary>
    [Reactive]
    public override string FileName { get; set; }

    /// <summary>强制后缀（含点，如 ".ustx"），由调用方传入，用户不可更改。</summary>
    public override string Extension { get; }

    [Reactive]
    public override bool HasInvalidFileName { get; protected set; }

    /// <summary>保存命令，仅在 FileName 非空时可用。绑定到底部"保存"按钮。</summary>
    public override ReactiveCommand<Unit, Unit> SaveCommand { get; }

    /// <param name="title">对话框标题</param>
    /// <param name="extension">强制后缀，如 ".ustx" 或 "ustx"（自动补点）</param>
    /// <param name="initialPath">初始目录</param>
    /// <param name="defaultFileName">预填文件名（不含后缀）</param>
    public FileSavePickerViewModel(
        string title, string extension,
        string initialPath = "", string defaultFileName = "")
        : base(title, filters: [], initialPath)
    {
        Extension = extension.StartsWith('.') ? extension : "." + extension;
        // ReSharper disable once VirtualMemberCallInConstructor
        FileName = defaultFileName;

        IObservable<string> fileNames = this.WhenAnyValue(x => x.FileName);
        fileNames.Subscribe(name => HasInvalidFileName = !string.IsNullOrEmpty(name) && !IsValidFileName(name));
        IObservable<bool> canSave = DirectoryActionsAvailable.CombineLatest(fileNames,
            (available, name) => available && IsValidFileName(name));
        SaveCommand = ReactiveCommand.CreateFromTask(() => RunDirectoryActionAsync(SaveAsync), canSave);
    }

    protected override void SelectItem(FileSystemInfo item)
    {
        if (item is DirectoryInfo dir)
            CurrentPath = dir.FullName;
        else if (item is FileInfo file)
            // 点击已有文件：自动填入文件名（不含后缀），方便用户覆写同名文件
            FileName = Path.GetFileNameWithoutExtension(file.Name);
    }

    /// <summary>提交时检查目标路径，同名文件需确认后才返回保存路径。</summary>
    private async Task SaveAsync()
    {
        string directory = CurrentPath;
        string name = FileName;
        string nameWithExtension = name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? name : name + Extension;
        string path = Path.Combine(directory, nameWithExtension);
        var destination = await Task.Run(() => (
            ParentExists: Directory.Exists(directory),
            FileExists: File.Exists(path),
            DirectoryExists: Directory.Exists(path)));
        if (!destination.ParentExists || IsClosed || CurrentPath != directory || FileName != name) return;

        if (destination.DirectoryExists)
        {
            await OptionConfirmPopupService.ShowAsync(L.S("FilePicker.Save"), L.S("Sandbox.AlreadyExists"),
                [new OptionConfirmOption(L.S("Common.Confirm"), "ok", isPrimary: true, isDefault: true)]);
            return;
        }

        if (destination.FileExists)
        {
            string? choice = await OptionConfirmPopupService.ShowAsync(L.S("FilePicker.Overwrite"),
                string.Format(L.S("FilePicker.OverwriteWarning"), nameWithExtension),
                [new OptionConfirmOption(L.S("Common.Cancel"), "cancel", isDefault: true),
                    new(L.S("FilePicker.Overwrite"), "overwrite", isDestructive: true)]);
            if (choice != "overwrite") return;
        }

        if (CurrentPath == directory && FileName == name) ClosePicker(path);
    }

    private static bool IsValidFileName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name is not "." and not ".." &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        !name.Contains('/') && !name.Contains('\\');
}
