using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.ExpressionGraph;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public sealed record ExpressionGraphPitchOption(string? Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>图设置使用独立草稿；取消不会触及工程。</summary>
public sealed class ExpressionGraphSettingsViewModel : PopupViewModelBase, ICmdSubscriber, IDisposable
{
    private readonly UProject _project;
    private readonly UExpressionGraph _original;
    private readonly string _fingerprint;
    private readonly string? _slot;
    private readonly bool _wasDefault;
    private readonly CompositeDisposable _subscriptions = new();
    private bool _disposed;
    private bool _committing;
    private bool _finished;

    [Reactive] public string Name { get; set; }
    [Reactive] public bool IsDefault { get; set; }
    [Reactive] public ExpressionGraphPitchOption? SelectedPitch { get; set; }
    [Reactive] public string Error { get; private set; } = string.Empty;
    [Reactive] public string DefaultImpact { get; private set; } = string.Empty;
    public string Renderer { get; }
    public string Status { get; }
    public string Usage { get; }
    public bool CanSetDefault => _slot != null;
    public IReadOnlyList<ExpressionGraphPitchOption> PitchOptions { get; }
    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ExpressionGraphSettingsViewModel(UProject project, UExpressionGraph graph)
    {
        _project = project;
        _original = graph.Clone();
        _fingerprint = ExpressionGraphLibraryInfo.Fingerprint(project);
        _slot = graph.renderer == null ? null : Renderers.GetExpressionGraphSlot(graph.renderer);
        _wasDefault = _slot != null && project.defaultExpressionGraphs?.GetValueOrDefault(_slot) == graph.id;
        Name = RendererGraphOption.Name(graph);
        Renderer = graph.renderer ?? L.S("ExpressionGraph.NoRenderer");
        IsDefault = _wasDefault;
        Usage = ExpressionGraphLibraryInfo.Usage(project, graph);
        Status = ExpressionGraphProgram.Compile(graph, out string? problem) != null
            ? L.S("ExpressionGraph.Valid") : string.Format(L.S("ExpressionGraph.Invalid"), problem);
        List<ExpressionGraphPitchOption> options =
        [
            new(null, L.S("ExpressionGraph.PitchDeviation")),
            new(OpenUtau.Core.Format.Ustx.PITO, L.S("ExpressionGraph.PitchOverride")),
        ];
        if (graph.preferredPitchCurve != null && options.All(option => option.Value != graph.preferredPitchCurve))
            options.Add(new(graph.preferredPitchCurve, graph.preferredPitchCurve));
        PitchOptions = options;
        SelectedPitch = options.First(option => option.Value == graph.preferredPitchCurve);
        ApplyCommand = ReactiveCommand.Create(Apply).DisposeWith(_subscriptions);
        CancelCommand = ReactiveCommand.Create(() => Complete(false)).DisposeWith(_subscriptions);
        this.WhenAnyValue(vm => vm.IsDefault).Subscribe(_ => UpdateImpact()).DisposeWith(_subscriptions);
        DocManager.Inst.AddSubscriber(this);
    }

    private void UpdateImpact()
    {
        string[] affected = _project.tracks.Where(track => track.ExpressionGraph == null && track.RendererSettings?.renderer != null
            && Renderers.GetExpressionGraphSlot(track.RendererSettings.renderer) == _slot).Select(track => track.TrackName).ToArray();
        DefaultImpact = IsDefault != _wasDefault && affected.Length > 0
            ? string.Format(L.S("ExpressionGraph.DefaultImpact"), string.Join(", ", affected)) : string.Empty;
    }

    private bool IsStale() => _disposed || !ReferenceEquals(DocManager.Inst.Project, _project)
        || _fingerprint != ExpressionGraphLibraryInfo.Fingerprint(_project);

    private void Apply()
    {
        if (_disposed || _finished || _committing) return;
        if (IsStale()) { Error = L.S("ExpressionGraph.StaleSettings"); return; }
        if (DocManager.Inst.HasOpenUndoGroup) { Error = L.S("ExpressionGraph.Busy"); return; }
        if (string.IsNullOrWhiteSpace(Name)) { Error = L.S("ExpressionGraph.NameRequired"); return; }
        if (SelectedPitch == null || !PitchOptions.Contains(SelectedPitch)) return;
        string displayedName = RendererGraphOption.Name(_original);
        string? name = Name == displayedName || Name.Trim() == displayedName ? _original.name : Name.Trim();
        if (name == _original.name && SelectedPitch.Value == _original.preferredPitchCurve && IsDefault == _wasDefault)
        {
            Complete(false);
            return;
        }
        try
        {
            _committing = true;
            ExpressionGraphLibraryInfo.Apply(_project, draft =>
            {
                UExpressionGraph target = draft.Find(_original.id)!;
                target.name = name;
                target.preferredPitchCurve = SelectedPitch.Value;
                if (_slot != null && IsDefault != _wasDefault)
                {
                    if (IsDefault) draft.Defaults[_slot] = target.id;
                    else draft.Defaults.Remove(_slot);
                }
            });
            Complete(true);
        }
        catch (Exception exception) { Error = exception.Message; }
        finally { _committing = false; }
    }

    private void Complete(bool applied)
    {
        if (_disposed || _finished) return;
        _finished = true;
        RaiseClose(applied);
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
        if (_committing || _finished) return;
        if (cmd is LoadProjectNotification)
            Dispatcher.UIThread.Post(() => Complete(false));
        else if (cmd is not UNotification)
            Dispatcher.UIThread.Post(() => { if (!_disposed && !_finished && IsStale()) Error = L.S("ExpressionGraph.StaleSettings"); });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DocManager.Inst.RemoveSubscriber(this);
        _subscriptions.Dispose();
    }
}

public sealed class ExpressionGraphRendererPickerViewModel : PopupViewModelBase
{
    public IReadOnlyList<string> Renderers { get; }
    public ReactiveCommand<string, Unit> SelectCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ExpressionGraphRendererPickerViewModel(IReadOnlyList<string> renderers)
    {
        Renderers = renderers;
        SelectCommand = ReactiveCommand.Create<string>(renderer => { if (Renderers.Contains(renderer)) RaiseClose(renderer); });
        CancelCommand = ReactiveCommand.Create(() => RaiseClose(null));
    }
}