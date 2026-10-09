using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DynamicData.Binding;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.ExpressionGraph;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Tracks;
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
    private readonly string? _originalGraph;
    private readonly Dictionary<string, string?> _graphDrafts = [];
    private string? _graphSlot;
    private string? _graphRenderer;
    private readonly CompositeDisposable _disposables = [];
    private bool _refreshing;
    private bool _disposed;

    public string TrackName => _track.TrackName;
    public string SingerName => _singer?.Name ?? string.Empty;
    public string OriginalRenderer => _original.renderer ?? string.Empty;
    public ObservableCollectionExtended<string> Renderers { get; } = [];
    [Reactive] public IReadOnlyList<string> Resamplers { get; private set; } = [];
    public ObservableCollectionExtended<string> Wavtools { get; } = [];
    [Reactive] public IReadOnlyList<RendererGraphOption> Graphs { get; private set; } = [];
    [Reactive] public RendererGraphOption? SelectedGraph { get; set; }
    [Reactive] public string GraphDescription { get; private set; } = string.Empty;
    [Reactive] public string GraphWarning { get; private set; } = string.Empty;
    [Reactive] public bool HasNoCompatibleGraphs { get; private set; }
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
        _originalGraph = track.ExpressionGraph;
        _graphRenderer = _original.renderer;
        if (_original.renderer != null)
            _graphDrafts[RendererRegistry.GetExpressionGraphSlot(_original.renderer)] = _originalGraph;
        if (_singer is { Found: true }) Renderers.Load(RendererRegistry.GetSupportedRenderers(_singer.SingerType));
        SelectedRenderer = _original.renderer != null && Renderers.Contains(_original.renderer) ? _original.renderer : null; // 初始选择原始渲染器，如果不再支持则置空。
        SelectedResampler = _original.resampler;
        SelectedWavtool = _original.wavtool;
        LoadTools();
        if (_original.renderer != RendererRegistry.CLASSIC && _original.wavtool == null) InitializeDefaultTools();
        ApplyCommand = ReactiveCommand.Create(Apply, this.WhenAnyValue(vm => vm.CanApply)).DisposeWith(_disposables);
        CancelCommand = ReactiveCommand.Create(() => RaiseClose(null)).DisposeWith(_disposables);
        this.WhenAnyValue(vm => vm.SelectedRenderer, vm => vm.SelectedResampler, vm => vm.SelectedWavtool)
            .Subscribe(_ => Refresh()).DisposeWith(_disposables);
        this.WhenAnyValue(vm => vm.SelectedGraph).Subscribe(_ => QueueRefresh()).DisposeWith(_disposables);
        if (Application.Current != null)
            Application.Current.GetResourceObservable("RendererSettings.Graph.Default")
                .Subscribe(_ => Dispatcher.UIThread.Post(Refresh)).DisposeWith(_disposables);
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
            RefreshGraphs();
            IWavtool? wavtool = ToolsManager.Inst.Wavtools.FirstOrDefault(tool => tool.ToString() == SelectedWavtool);
            string[] compatible = RendererRegistry.GetSupportedResamplers(wavtool).Select(tool => tool.ToString()!).ToArray();
            // 列表变化时保留仍然兼容的选择；失效的组合由用户重新选择。
            if (!Resamplers.SequenceEqual(compatible))
            {
                string? resampler = SelectedResampler;
                Resamplers = compatible;
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
            bool changed = SelectedGraph?.Id != _originalGraph || SelectedRenderer != _original.renderer || (IsClassic &&
                (SelectedResampler != _original.resampler || SelectedWavtool != _original.wavtool));
            CanApply = changed && SelectedGraph != null && Error.Length == 0 && ResamplerError.Length == 0 && WavtoolError.Length == 0
                && RendererGraphOption.CanKeep(_project, SelectedRenderer, SelectedGraph.Id, _original.renderer, _originalGraph);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshGraphs()
    {
        string? slot = SelectedRenderer == null ? null : RendererRegistry.GetExpressionGraphSlot(SelectedRenderer);
        if (_graphSlot != null && SelectedGraph != null) _graphDrafts[_graphSlot] = SelectedGraph.Id;
        string? id = slot == _graphSlot ? SelectedGraph?.Id
            : slot != null && _graphDrafts.TryGetValue(slot, out string? saved) ? saved : SelectedGraph?.Id;
        _graphSlot = slot;
        if (SelectedRenderer != _graphRenderer)
            id = TrackExpressionGraphSelection.Resolve(_project, SelectedRenderer, id);
        _graphRenderer = SelectedRenderer;

        UExpressionGraph[] compatible = _project.expressionGraphs?
            .Where(graph => RendererGraphOption.IsCompatible(graph, SelectedRenderer)).ToArray() ?? [];
        HasNoCompatibleGraphs = compatible.Length == 0;
        List<RendererGraphOption> options =
        [
            new(null, L.S("RendererSettings.Graph.Default")),
            new(string.Empty, L.S("RendererSettings.Graph.Off")),
        ];
        foreach (UExpressionGraph graph in compatible)
        {
            string name = RendererGraphOption.Name(graph);
            if (compatible.Count(other => RendererGraphOption.Name(other) == name) > 1) name = $"{name} ({graph.id})";
            options.Add(Graphs.FirstOrDefault(option => option.Id == graph.id && option.Label == name)
                ?? new RendererGraphOption(graph.id, name));
        }
        if (!options.Any(option => option.Id == id))
            options.Add(new RendererGraphOption(id, string.Format(L.S("RendererSettings.Graph.Unavailable"), id)));
        // 替换完整候选快照，避免选择回写期间对同一个集合做 Clear/Add。
        if (!Graphs.SequenceEqual(options)) Graphs = options;
        SelectedGraph = Graphs.First(option => option.Id == id);

        string? effectiveId = id;
        if (id == null && slot != null) _project.defaultExpressionGraphs?.TryGetValue(slot, out effectiveId);
        GraphWarning = RendererGraphOption.Problem(_project, SelectedRenderer, effectiveId);
        UExpressionGraph? effective = _project.expressionGraphs?.FirstOrDefault(graph => graph.id == effectiveId);
        GraphDescription = id == string.Empty ? L.S("RendererSettings.Graph.Disabled")
            : effective == null || GraphWarning.Length > 0 ? L.S("RendererSettings.Graph.NoEffective")
            : string.Format(L.S(id == null ? "RendererSettings.Graph.DefaultEffective" : "RendererSettings.Graph.Effective"),
                RendererGraphOption.Name(effective));
    }

    private string GetContextError()
    {
        if (!ReferenceEquals(DocManager.Inst.Project, _project) || !_project.tracks.Contains(_track))
            return L.S("RendererSettings.Error.Stale");
        if (!ReferenceEquals(_track.Singer, _singer) || _track.RendererSettings.renderer != _original.renderer
            || _track.RendererSettings.resampler != _original.resampler || _track.RendererSettings.wavtool != _original.wavtool
            || _track.ExpressionGraph != _originalGraph)
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
        RaiseClose(new RendererSettingsSelection(settings, SelectedGraph!.Id));
    }

    public override void RequestBack()
    {
        if (CloseDropDown?.Invoke() != true) RaiseClose(null);
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
        QueueRefresh();
    }

    private bool _refreshPending;
    private void QueueRefresh()
    {
        if (_refreshing || _disposed || _refreshPending) return;
        _refreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _refreshPending = false;
            Refresh();
        });
    }

    public void Dispose()
    {
        _disposed = true;
        CloseDropDown = null;
        DocManager.Inst.RemoveSubscriber(this);
        _disposables.Dispose();
    }
}