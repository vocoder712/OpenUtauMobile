using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using OpenUtauMobile.Helpers;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>在共用文件选择器中保留跨目录勾选状态。</summary>
public sealed class MultiFilePickerViewModel : FilePickerBaseViewModel
{
    private readonly HashSet<string> selected = new(OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public override FilePickerMode Mode => FilePickerMode.OpenFile;
    public override bool IsMultiSelect => true;
    public override string SelectionSummary => string.Format(L.S("ImportTracks.FilesSelected"), selected.Count);
    public bool CanConfirm => selected.Count > 0;
    public override ReactiveCommand<Unit, Unit> ConfirmCommand { get; }

    public MultiFilePickerViewModel(string title, string[] filters) : base(title, filters)
    {
        ConfirmCommand = ReactiveCommand.Create(() => RaiseClose(selected.ToArray()),
            this.WhenAnyValue(model => model.CanConfirm));
    }

    protected override FilePickerEntry CreateEntry(FileSystemInfo item) =>
        new(item, selected.Contains(item.FullName), Toggle);

    private void Toggle(string path, bool isSelected)
    {
        if (isSelected)
        {
            selected.Add(path);
        }
        else
        {
            selected.Remove(path);
        }
        this.RaisePropertyChanged(nameof(SelectionSummary));
        this.RaisePropertyChanged(nameof(CanConfirm));
    }

    protected override void SelectItem(FileSystemInfo item)
    {
        if (item is DirectoryInfo directory)
        {
            CurrentPath = directory.FullName;
        }
        else
        {
            FilePickerEntry? entry = Entries.FirstOrDefault(candidate => candidate.Item == item);
            if (entry != null)
            {
                entry.IsSelected = !entry.IsSelected;
            }
        }
    }
}
