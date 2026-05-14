using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public enum ExportAudioMode
{
    Mixdown,
    Tracks,
}

public sealed record ExportAudioRequest(ExportAudioMode Mode, string ExportPath);

public class ExportAudioPopupViewModel : PopupViewModelBase
{
    private readonly Func<ExportAudioRequest, Task<bool>> _exportAsync;
    private readonly string _defaultFileName;
    private string _initialDirectory;
    private bool _updatingSelection;

    [Reactive] public ExportAudioMode SelectedMode { get; private set; } = ExportAudioMode.Mixdown;
    [Reactive] public bool IsMixdownSelected { get; set; } = true;
    [Reactive] public bool IsTracksSelected { get; set; }
    [Reactive] public string ExportPath { get; set; } = string.Empty;
    [Reactive] public bool IsExporting { get; private set; }

    public ReactiveCommand<Unit, Unit> PickPathCommand { get; }
    public ReactiveCommand<Unit, Unit> StartCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ExportAudioPopupViewModel(
        string defaultFileName,
        string initialDirectory,
        Func<ExportAudioRequest, Task<bool>> exportAsync)
    {
        _defaultFileName = string.IsNullOrWhiteSpace(defaultFileName) ? "export" : defaultFileName;
        _initialDirectory = initialDirectory;
        _exportAsync = exportAsync;

        this.WhenAnyValue(x => x.IsMixdownSelected)
            .Skip(1)
            .Where(v => v)
            .Subscribe(_ => SetMode(ExportAudioMode.Mixdown));

        this.WhenAnyValue(x => x.IsTracksSelected)
            .Skip(1)
            .Where(v => v)
            .Subscribe(_ => SetMode(ExportAudioMode.Tracks));

        IObservable<bool> canStart = this.WhenAnyValue(
            x => x.ExportPath,
            x => x.IsExporting,
            (path, exporting) => !string.IsNullOrWhiteSpace(path) && !exporting);

        PickPathCommand = ReactiveCommand.CreateFromTask(PickPathAsync);
        StartCommand = ReactiveCommand.CreateFromTask(StartExportAsync, canStart);
        CancelCommand = ReactiveCommand.Create(Cancel);
    }

    public override void RequestBack()
    {
        if (IsExporting)
        {
            return;
        }

        Cancel();
    }

    private void SetMode(ExportAudioMode mode)
    {
        if (_updatingSelection)
        {
            return;
        }

        _updatingSelection = true;
        try
        {
            SelectedMode = mode;
            IsMixdownSelected = mode == ExportAudioMode.Mixdown;
            IsTracksSelected = mode == ExportAudioMode.Tracks;
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    private async Task PickPathAsync()
    {
        string initialDirectory = _initialDirectory;
        if (!string.IsNullOrWhiteSpace(ExportPath))
        {
            initialDirectory = Path.GetDirectoryName(ExportPath) ?? initialDirectory;
        }

        string filePath = await FilePicker.SaveFileAsync(
            L.S("ExportAudioPopup.SaveDialogTitle"),
            "wav",
            _defaultFileName,
            initialDirectory);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        ExportPath = filePath;
        _initialDirectory = Path.GetDirectoryName(filePath) ?? _initialDirectory;
    }

    private async Task StartExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportPath))
        {
            ToastService.Enqueue(L.S("ExportAudioPopup.PathRequired"));
            return;
        }

        IsExporting = true;
        bool success;
        try
        {
            success = await _exportAsync(new ExportAudioRequest(SelectedMode, ExportPath));
        }
        finally
        {
            IsExporting = false;
        }

        if (success)
        {
            RaiseClose(true);
        }
    }

    private void Cancel()
    {
        RaiseClose(false);
    }
}
