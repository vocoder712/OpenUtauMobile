using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using OpenUtauMobile.Helpers;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>跨目录多选文件；原有单选选择器保持原样。</summary>
public sealed class MultiFilePickerViewModel : FilePickerBaseViewModel
{
    private readonly HashSet<string> selected = new(OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public override FilePickerMode Mode => FilePickerMode.OpenFile;
    public ObservableCollection<MultiFilePickerEntry> Entries { get; } = [];
    public string SelectionSummary => string.Format(L.S("ImportTracks.FilesSelected"), selected.Count);
    public bool CanConfirm => selected.Count > 0;
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }

    public MultiFilePickerViewModel(string title, string[] filters) : base(title, filters)
    {
        AllItems.CollectionChanged += (_, _) => RefreshEntries();
        RefreshEntries();
        ConfirmCommand = ReactiveCommand.Create(() => RaiseClose(selected.ToArray()),
            this.WhenAnyValue(model => model.CanConfirm));
    }

    private void RefreshEntries()
    {
        Entries.Clear();
        foreach (FileSystemInfo item in AllItems)
        {
            Entries.Add(new MultiFilePickerEntry(item, selected.Contains(item.FullName), SelectItem, Toggle));
        }
    }

    private void Toggle(string path, bool isSelected)
    {
        if (isSelected) selected.Add(path);
        else selected.Remove(path);
        this.RaisePropertyChanged(nameof(SelectionSummary));
        this.RaisePropertyChanged(nameof(CanConfirm));
    }

    protected override void SelectItem(FileSystemInfo item)
    {
        if (item is DirectoryInfo directory) CurrentPath = directory.FullName;
    }
}

public sealed class MultiFilePickerEntry : ReactiveObject
{
    private bool isSelected;
    private readonly Action<string, bool> selectionChanged;
    private readonly FileSystemInfo item;
    public string Name => item.Name;
    public bool IsDirectory => item is DirectoryInfo;
    public bool IsFile => !IsDirectory;
    public ReactiveCommand<Unit, Unit> OpenCommand { get; }
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            this.RaiseAndSetIfChanged(ref isSelected, value);
            selectionChanged(item.FullName, value);
        }
    }

    public MultiFilePickerEntry(FileSystemInfo item, bool selected, Action<FileSystemInfo> open,
        Action<string, bool> selectionChanged)
    {
        this.item = item;
        isSelected = selected;
        this.selectionChanged = selectionChanged;
        OpenCommand = ReactiveCommand.Create(() => open(item));
    }
}
