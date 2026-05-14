using System;
using System.IO;
using System.Reactive;
using OpenUtauMobile.Helpers;
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

    /// <summary>组合预览路径（CurrentPath + FileName + Extension），用于在 UI 中回显。</summary>
    [Reactive]
    public string PreviewFullPath { get; private set; } = string.Empty;
    [Reactive]
    public override string WarningMessage { get; set; } = string.Empty;
    [Reactive]
    public override bool IsShowingWarningMessage { get; set; }

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

        // 实时更新预览路径
        this.WhenAnyValue(x => x.CurrentPath, x => x.FileName)
            .Subscribe(_ => UpdatePreviewPath());

        IObservable<bool> canSave = this.WhenAnyValue(x => x.FileName,
            fn => !string.IsNullOrWhiteSpace(fn));
        SaveCommand = ReactiveCommand.Create(Save, canSave);
    }

    protected override void SelectItem(FileSystemInfo item)
    {
        if (item is DirectoryInfo dir)
            CurrentPath = dir.FullName;
        else if (item is FileInfo file)
            // 点击已有文件：自动填入文件名（不含后缀），方便用户覆写同名文件
            FileName = Path.GetFileNameWithoutExtension(file.Name);
    }

    private void UpdatePreviewPath()
    {
        if (string.IsNullOrWhiteSpace(FileName))
        {
            PreviewFullPath = string.Empty;
            return;
        }

        string nameWithExt = FileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
            ? FileName
            : FileName + Extension;
        PreviewFullPath = Path.Combine(CurrentPath, nameWithExt);
        if (File.Exists(PreviewFullPath))
        {
            WarningMessage = L.S("FilePicker.OverwriteWarning");
            IsShowingWarningMessage = true;
        }
        else
        {
            IsShowingWarningMessage = false;
        }
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(FileName)) return;
        RaiseClose(PreviewFullPath);
    }
}