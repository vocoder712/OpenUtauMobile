using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using Avalonia.Threading;
using DynamicData.Binding;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using RendererRegistry = OpenUtau.Core.Render.Renderers;

namespace OpenUtauMobile.ViewModels;

/// <summary>轨道渲染设置草稿；应用前不修改轨道或触发渲染。</summary>
public class RendererPickerViewModel : PopupViewModelBase, ICmdSubscriber, IDisposable
{
    private readonly UProject _project;
    private readonly UTrack _track;
    private readonly USinger? _singer;
    private readonly URenderSettings _original;
    private readonly CompositeDisposable _disposables = [];
    private bool _refreshing;
    private bool _disposed;

    public string TrackName => _track.TrackName;
    public string SingerName => _singer?.Name ?? string.Empty;
    public string OriginalRenderer => _original.renderer ?? string.Empty;
    public ObservableCollectionExtended<string> Renderers { get; } = [];
    public ObservableCollectionExtended<string> Resamplers { get; } = [];
    public ObservableCollectionExtended<string> Wavtools { get; } = [];
    [Reactive] public string? SelectedRenderer { get; set; }
    [Reactive] public string? SelectedResampler { get; set; }
    [Reactive] public string? SelectedWavtool { get; set; }
    [Reactive] public bool IsClassic { get; private set; }
    [Reactive] public bool ShowHifiSamplerWarning { get; private set; }
    [Reactive] public bool ShowWorldlineR2Warning { get; private set; }
    [Reactive] public bool CanApply { get; private set; }
    [Reactive] public string Error { get; private set; } = string.Empty;
    [Reactive] public string ResamplerError { get; private set; } = string.Empty;
    [Reactive] public string WavtoolError { get; private set; } = string.Empty;
    public Func<bool>? CloseDropDown { get; set; }
    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public RendererPickerViewModel(UProject project, UTrack track)
    {
        _project = project;
        _track = track;
        _singer = track.Singer;
        _original = track.RendererSettings.Clone();
        if (_singer is { Found: true }) Renderers.Load(RendererRegistry.GetSupportedRenderers(_singer.SingerType));
        SelectedRenderer = Renderers.Contains(_original.renderer) ? _original.renderer : null; // 初始选择原始渲染器，如果不再支持则置空。
        SelectedResampler = _original.resampler;
        SelectedWavtool = _original.wavtool;
        LoadTools();
        if (_original.renderer != RendererRegistry.CLASSIC && _original.wavtool == null) InitializeDefaultTools();
        ApplyCommand = ReactiveCommand.Create(Apply, this.WhenAnyValue(vm => vm.CanApply)).DisposeWith(_disposables);
        CancelCommand = ReactiveCommand.Create(() => RaiseClose(null)).DisposeWith(_disposables);
        this.WhenAnyValue(vm => vm.SelectedRenderer, vm => vm.SelectedResampler, vm => vm.SelectedWavtool)
            .Subscribe(_ => Refresh()).DisposeWith(_disposables);
        DocManager.Inst.AddSubscriber(this);
    }

    private void LoadTools()
    {
        _refreshing = true;
        string? wavtool = SelectedWavtool;
        Wavtools.Load(ToolsManager.Inst.Wavtools.Select(tool => tool.ToString()!));
        SelectedWavtool = Wavtools.Contains(wavtool!) ? wavtool : null;
        _refreshing = false;
        Refresh();
    }

    private void InitializeDefaultTools()
    {
        _refreshing = true;
        Preferences.Default.DefaultWavtools.TryGetValue(RendererRegistry.CLASSIC, out string? wavtool);
        Preferences.Default.DefaultResamplers.TryGetValue(RendererRegistry.CLASSIC, out string? resampler);
        SelectedWavtool = Wavtools.Contains(wavtool!) ? wavtool : Wavtools.FirstOrDefault(tool => tool == SharpWavtool.nameConvergence);
        SelectedResampler = ToolsManager.Inst.Resamplers.Any(tool => tool.ToString() == resampler)
            ? resampler : ToolsManager.Inst.Resamplers.FirstOrDefault(tool => tool.ToString() == "worldline")?.ToString();
        _refreshing = false;
        Refresh();
    }

    private void Refresh()
    {
        if (_refreshing || _disposed) return;
        _refreshing = true;
        try
        {
            IsClassic = SelectedRenderer == RendererRegistry.CLASSIC;
            ShowHifiSamplerWarning = SelectedRenderer == "HIFISAMPLER";
            ShowWorldlineR2Warning = SelectedRenderer == RendererRegistry.WORLDLINE_R2;
            IWavtool? wavtool = ToolsManager.Inst.Wavtools.FirstOrDefault(tool => tool.ToString() == SelectedWavtool);
            string[] compatible = RendererRegistry.GetSupportedResamplers(wavtool).Select(tool => tool.ToString()!).ToArray();
            // 列表变化时保留仍然兼容的选择；失效的组合由用户重新选择。
            if (!Resamplers.SequenceEqual(compatible))
            {
                string? resampler = SelectedResampler;
                Resamplers.Load(compatible);
                SelectedResampler = compatible.Contains(resampler) ? resampler : null;
            }
            if (!Resamplers.Contains(SelectedResampler!)) SelectedResampler = null;
            Error = GetContextError();
            if (Error.Length == 0 && (SelectedRenderer == null || !Renderers.Contains(SelectedRenderer)))
                Error = L.S("RendererSettings.Error.Renderer");
            ResamplerError = IsClassic && SelectedResampler == null
                ? L.S(Resamplers.Count == 0 ? "RendererSettings.Error.NoResamplers" : "RendererSettings.Error.Resampler") : string.Empty;
            WavtoolError = IsClassic && SelectedWavtool == null
                ? L.S(Wavtools.Count == 0 ? "RendererSettings.Error.NoWavtools" : "RendererSettings.Error.Wavtool") : string.Empty;
            bool changed = SelectedRenderer != _original.renderer || (IsClassic &&
                (SelectedResampler != _original.resampler || SelectedWavtool != _original.wavtool));
            CanApply = changed && Error.Length == 0 && ResamplerError.Length == 0 && WavtoolError.Length == 0;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private string GetContextError()
    {
        if (!ReferenceEquals(DocManager.Inst.Project, _project) || !_project.tracks.Contains(_track))
            return L.S("RendererSettings.Error.Stale");
        if (!ReferenceEquals(_track.Singer, _singer) || _track.RendererSettings.renderer != _original.renderer
            || _track.RendererSettings.resampler != _original.resampler || _track.RendererSettings.wavtool != _original.wavtool)
            return L.S("RendererSettings.Error.Stale");
        if (_singer == null) return L.S("RendererSettings.Error.NoSinger");
        if (!_singer.Found) return L.S("RendererSettings.Error.MissingSinger");
        return string.Empty;
    }

    private void Apply()
    {
        Refresh();
        if (!CanApply) return;
        URenderSettings settings = _original.Clone();
        settings.renderer = SelectedRenderer!;
        if (IsClassic)
        {
            settings.resampler = SelectedResampler!;
            settings.wavtool = SelectedWavtool!;
        }
        else settings.wavtool = null!;
        RaiseClose(settings);
    }

    public override void RequestBack()
    {
        if (CloseDropDown?.Invoke() != true) RaiseClose(null);
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    public void Dispose()
    {
        _disposed = true;
        CloseDropDown = null;
        DocManager.Inst.RemoveSubscriber(this);
        _disposables.Dispose();
    }
}
