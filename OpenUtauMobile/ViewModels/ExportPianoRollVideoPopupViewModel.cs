using System;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Storage;
using OpenUtauMobile.Services.VideoExport;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>钢琴卷帘视频导出请求。</summary>
/// <param name="ExportPath">目标 mp4 路径。</param>
/// <param name="Options">导出参数。</param>
public sealed record ExportPianoRollVideoRequest(string ExportPath, PianoRollVideoOptions Options);

/// <summary>钢琴卷帘视频导出弹窗：选择保存位置、画幅（横屏 / 竖屏 / 自定义）、帧率、立绘与音频。</summary>
public class ExportPianoRollVideoPopupViewModel : PopupViewModelBase
{
    private readonly Func<ExportPianoRollVideoRequest, IProgress<double>, CancellationToken, Task<bool>> _exportAsync;
    private readonly string _defaultFileName;
    private string _initialDirectory;
    private bool _updatingSelection;
    private CancellationTokenSource? _cts;

    [Reactive] public string ExportPath { get; set; } = string.Empty;
    [Reactive] public PianoRollVideoAspect SelectedAspect { get; private set; } = PianoRollVideoAspect.Portrait;
    [Reactive] public bool IsLandscapeSelected { get; set; }
    [Reactive] public bool IsPortraitSelected { get; set; } = true;
    [Reactive] public bool IsCustomSelected { get; set; }
    [Reactive] public decimal? CustomWidth { get; set; } = 1080;
    [Reactive] public decimal? CustomHeight { get; set; } = 1920;
    [Reactive] public bool Use60Fps { get; set; }
    [Reactive] public bool ShowPortrait { get; set; }
    [Reactive] public bool IncludeAudio { get; set; } = true;
    [Reactive] public double Progress { get; private set; }
    [Reactive] public string ProgressText { get; private set; } = string.Empty;
    [Reactive] public bool IsExporting { get; private set; }

    /// <summary>当前平台是否有可用的视频编码器。</summary>
    public bool IsEncoderAvailable { get; }

    public ReactiveCommand<Unit, Unit> PickPathCommand { get; }
    public ReactiveCommand<Unit, Unit> StartCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ExportPianoRollVideoPopupViewModel(
        string defaultFileName,
        string initialDirectory,
        Func<ExportPianoRollVideoRequest, IProgress<double>, CancellationToken, Task<bool>> exportAsync)
    {
        _defaultFileName = string.IsNullOrWhiteSpace(defaultFileName) ? "piano_roll" : defaultFileName;
        _initialDirectory = initialDirectory;
        _exportAsync = exportAsync;
        IsEncoderAvailable = PianoRollVideoExporter.IsAvailable;

        this.WhenAnyValue(x => x.IsLandscapeSelected).Skip(1).Where(v => v)
            .Subscribe(_ => SetAspect(PianoRollVideoAspect.Landscape));
        this.WhenAnyValue(x => x.IsPortraitSelected).Skip(1).Where(v => v)
            .Subscribe(_ => SetAspect(PianoRollVideoAspect.Portrait));
        this.WhenAnyValue(x => x.IsCustomSelected).Skip(1).Where(v => v)
            .Subscribe(_ => SetAspect(PianoRollVideoAspect.Custom));

        IObservable<bool> canStart = this.WhenAnyValue(
            x => x.ExportPath,
            x => x.IsExporting,
            (path, exporting) => !string.IsNullOrWhiteSpace(path) && !exporting && IsEncoderAvailable);

        PickPathCommand = ReactiveCommand.CreateFromTask(PickPathAsync);
        StartCommand = ReactiveCommand.CreateFromTask(StartExportAsync, canStart);
        CancelCommand = ReactiveCommand.Create(Cancel);
    }

    public override void RequestBack() => Cancel();

    private void SetAspect(PianoRollVideoAspect aspect)
    {
        if (_updatingSelection) return;
        _updatingSelection = true;
        try
        {
            SelectedAspect = aspect;
            IsLandscapeSelected = aspect == PianoRollVideoAspect.Landscape;
            IsPortraitSelected = aspect == PianoRollVideoAspect.Portrait;
            IsCustomSelected = aspect == PianoRollVideoAspect.Custom;
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
            L.S("ExportVideoPopup.SaveDialogTitle"),
            "mp4",
            _defaultFileName,
            initialDirectory);
        if (string.IsNullOrWhiteSpace(filePath)) return;

        ExportPath = filePath;
        _initialDirectory = Path.GetDirectoryName(filePath) ?? _initialDirectory;
    }

    private async Task StartExportAsync()
    {
        if (string.IsNullOrWhiteSpace(ExportPath))
        {
            ToastService.Enqueue(L.S("ExportVideoPopup.PathRequired"));
            return;
        }

        PianoRollVideoOptions options = new(
            SelectedAspect,
            (int)(CustomWidth ?? 1080),
            (int)(CustomHeight ?? 1920),
            Use60Fps ? 60 : 30,
            Use60Fps ? 12000 : 8000,
            ShowPortrait,
            IncludeAudio);

        _cts = new CancellationTokenSource();
        Progress = 0;
        ProgressText = "0%";
        IsExporting = true;
        bool success;
        try
        {
            // Progress<T> 在 UI 线程创建，回调会回到 UI 线程更新绑定。
            Progress<double> progress = new(value =>
            {
                Progress = value;
                ProgressText = $"{value:P0}";
            });
            success = await _exportAsync(new ExportPianoRollVideoRequest(ExportPath, options), progress, _cts.Token);
        }
        finally
        {
            IsExporting = false;
            _cts.Dispose();
            _cts = null;
        }

        if (success) RaiseClose(true);
    }

    /// <summary>导出中取消渲染并停留在弹窗；空闲时关闭弹窗。</summary>
    private void Cancel()
    {
        if (IsExporting)
        {
            _cts?.Cancel();
            return;
        }

        RaiseClose(false);
    }
}
