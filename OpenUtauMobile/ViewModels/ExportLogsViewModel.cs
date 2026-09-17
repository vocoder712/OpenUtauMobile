using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels;

/// <summary>列出应用日志并将用户选择的文件导出为压缩包。</summary>
public sealed class ExportLogsViewModel : NavigateViewModelBase
{
    public ObservableCollection<LogFileItemViewModel> Logs { get; } = [];

    [Reactive] public bool IsLoading { get; private set; }
    [Reactive] public bool IsExporting { get; private set; }
    [Reactive] public bool HasError { get; private set; }
    [Reactive] public string ErrorMessage { get; private set; } = string.Empty;

    public bool HasLogs => Logs.Count > 0;
    public bool HasSelection => SelectedCount > 0;
    public int SelectedCount => Logs.Count(item => item.IsSelected);
    public string SelectionSummary => string.Format(
        L.S("ExportLogs.SelectionSummary"), SelectedCount, Logs.Count);

    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectNoneCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportCommand { get; }

    public ExportLogsViewModel(MainViewModel navigator) : base(navigator)
    {
        BackCommand = ReactiveCommand.Create(() => Navigator.NavigateBack(this));
        RefreshCommand = ReactiveCommand.CreateFromTask(LoadLogsAsync,
            this.WhenAnyValue(model => model.IsLoading, model => model.IsExporting,
                (loading, exporting) => !loading && !exporting));
        SelectAllCommand = ReactiveCommand.Create(() => SetAllSelected(true));
        SelectNoneCommand = ReactiveCommand.Create(() => SetAllSelected(false));
        ExportCommand = ReactiveCommand.CreateFromTask(ExportAsync,
            this.WhenAnyValue(model => model.HasSelection, model => model.IsExporting,
                (hasSelection, exporting) => hasSelection && !exporting));
    }

    public override void OnNavigatedTo()
    {
        _ = LoadLogsAsync();
    }

    private async Task LoadLogsAsync()
    {
        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;
        Logs.Clear();
        RefreshSelection();
        try
        {
            LogFileInfo[] files = await Task.Run(() =>
            {
                string directory = PathManager.Inst.LogsPath;
                if (!Directory.Exists(directory)) return [];
                return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .Where(file => file.Exists &&
                        file.Name.StartsWith("log", StringComparison.OrdinalIgnoreCase) &&
                        file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Select(file => new LogFileInfo(
                        file.FullName, file.Name, file.Length, file.LastWriteTime))
                    .ToArray();
            });

            Logs.Clear();
            foreach (LogFileInfo file in files)
            {
                Logs.Add(new LogFileItemViewModel(
                    file.Path, file.Name, file.Size, file.ModifiedAt, RefreshSelection));
            }
            RefreshSelection();
        }
        catch (Exception exception)
        {
            HasError = true;
            ErrorMessage = L.S("ExportLogs.LoadFailed");
            Log.Error(exception, "Failed to enumerate application logs");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void SetAllSelected(bool selected)
    {
        foreach (LogFileItemViewModel item in Logs)
        {
            item.IsSelected = selected;
        }
        RefreshSelection();
    }

    private async Task ExportAsync()
    {
        LogFileItemViewModel[] selected = Logs.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0) return;

        try
        {
            string defaultName = $"OpenUtau-logs-{DateTime.Now:yyyyMMdd-HHmmss}";
            string destination = await FilePicker.SaveFileAsync(
                L.S("ExportLogs.SaveDialogTitle"), ".zip", defaultName);
            if (string.IsNullOrEmpty(destination)) return;

            IsExporting = true;
            await Task.Run(() => CreateArchive(destination, selected));
            ToastService.Enqueue(string.Format(L.S("ExportLogs.ExportSuccess"), selected.Length));
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to export {LogCount} application logs", selected.Length);
            ToastService.Enqueue(L.S("ExportLogs.ExportFailed"));
        }
        finally
        {
            IsExporting = false;
        }
    }

    private static void CreateArchive(string destination, LogFileItemViewModel[] selected)
    {
        using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(output, ZipArchiveMode.Create);
        foreach (LogFileItemViewModel item in selected)
        {
            ZipArchiveEntry entry = archive.CreateEntry(item.Name, CompressionLevel.Optimal);
            using Stream entryStream = entry.Open();
            using FileStream source = new(item.FilePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            source.CopyTo(entryStream);
        }
    }

    private void RefreshSelection()
    {
        this.RaisePropertyChanged(nameof(HasLogs));
        this.RaisePropertyChanged(nameof(HasSelection));
        this.RaisePropertyChanged(nameof(SelectedCount));
        this.RaisePropertyChanged(nameof(SelectionSummary));
    }

    private sealed record LogFileInfo(string Path, string Name, long Size, DateTime ModifiedAt);
}

public sealed class LogFileItemViewModel : ReactiveObject
{
    private bool isSelected;
    private readonly Action selectionChanged;

    internal string FilePath { get; }
    public string Name { get; }
    public string ModifiedAt { get; }
    public string Size { get; }

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            this.RaiseAndSetIfChanged(ref isSelected, value);
            selectionChanged();
        }
    }

    internal LogFileItemViewModel(string path, string name, long size, DateTime modifiedAt,
        Action selectionChanged)
    {
        FilePath = path;
        Name = name;
        ModifiedAt = modifiedAt.ToString("g", CultureInfo.CurrentCulture);
        Size = FormatFileSize(size);
        this.selectionChanged = selectionChanged;
    }

    private static string FormatFileSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        string format = unit == 0 ? "0" : "0.#";
        return $"{size.ToString(format, CultureInfo.CurrentCulture)} {units[unit]}";
    }
}
