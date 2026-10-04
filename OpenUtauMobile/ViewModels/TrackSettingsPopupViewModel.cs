using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using Avalonia;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using CoreRenderers = OpenUtau.Core.Render.Renderers;

namespace OpenUtauMobile.ViewModels;

public enum TrackSettingsAction
{
    Cancel,
    PickSinger,
    PickPhonemizer,
    PickRenderer,
    EditExpressions,
    Apply,
}

public sealed record TrackSettingsToolOption(string Name, bool IsAvailable);

public sealed record TrackExpressionSettingsDraft(UExpressionDescriptor[] Descriptors);

public sealed class TrackSettingsPopupViewModel : PopupViewModelBase, IDisposable
{
    private readonly UProject project;
    private readonly UTrack track;
    private readonly URenderSettings originalRenderSettings;
    private readonly USinger? originalSinger;
    private readonly Phonemizer originalPhonemizer;
    private readonly UExpressionDescriptor[] originalTrackExpressions;
    private Phonemizer draftPhonemizer;
    private URenderSettings draftRenderSettings;
    private bool changingSelection;
    private bool classicDraftInitialized;

    [Reactive] public USinger? DraftSinger { get; set; }
    [Reactive] public string DraftSingerName { get; set; } = string.Empty;
    [Reactive] public string DraftPhonemizerName { get; set; } = string.Empty;
    [Reactive] public string DraftRenderer { get; set; } = string.Empty;
    [Reactive] public string DraftResampler { get; set; } = string.Empty;
    [Reactive] public string DraftWavtool { get; set; } = string.Empty;
    [Reactive] public string Error { get; set; } = string.Empty;
    [Reactive] public Vector ScrollOffset { get; set; }
    [Reactive] public bool IsRendererEnabled { get; set; }
    [Reactive] public bool HasSinger { get; private set; }
    [Reactive] public bool HasNoSupportedRenderers { get; set; }
    [Reactive] public bool IsClassic { get; set; }
    [Reactive] public bool HasCompatibleToolPair { get; set; } = true;
    [Reactive] public bool IsRenderSettingsDirty { get; private set; }
    [Reactive] public TrackSettingsToolOption? SelectedResampler { get; set; }
    [Reactive] public TrackSettingsToolOption? SelectedWavtool { get; set; }

    public ObservableCollection<string> Renderers { get; } = [];
    public ObservableCollection<TrackSettingsToolOption> Resamplers { get; } = [];
    public ObservableCollection<TrackSettingsToolOption> Wavtools { get; } = [];
    public string TrackName => track.TrackName;
    public string ApplyLabel => L.S("Common.Apply");
    public ReactiveCommand<Unit, Unit> PickSingerCommand { get; }
    public ReactiveCommand<Unit, Unit> PickPhonemizerCommand { get; }
    public ReactiveCommand<Unit, Unit> PickRendererCommand { get; }
    public ReactiveCommand<Unit, Unit> EditExpressionsCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public UProject Project => project;
    public UTrack Track => track;
    public Type OriginalPhonemizerType => originalPhonemizer.GetType();
    public string DraftPhonemizerIdentifier => draftPhonemizer.Name;
    public IReadOnlyList<UExpressionDescriptor> DraftTrackExpressions { get; private set; }
    public bool IsStale => !ReferenceEquals(DocManager.Inst.Project, project) || !project.tracks.Contains(track) ||
        !ReferenceEquals(track.Singer, originalSinger) || track.Phonemizer?.GetType() != originalPhonemizer.GetType() ||
        !SameRenderSettings(track.RendererSettings, originalRenderSettings) || !SameExpressions(track.TrackExpressions, originalTrackExpressions);

    public TrackSettingsPopupViewModel(UProject project, UTrack track)
    {
        this.project = project;
        this.track = track;
        originalSinger = track.Singer;
        originalPhonemizer = track.Phonemizer!;
        originalRenderSettings = track.RendererSettings.Clone();
        classicDraftInitialized = originalRenderSettings.renderer == CoreRenderers.CLASSIC;
        originalTrackExpressions = track.TrackExpressions.Select(expression => expression.Clone()).ToArray();
        DraftTrackExpressions = originalTrackExpressions.Select(expression => expression.Clone()).ToArray();
        draftPhonemizer = originalPhonemizer;
        draftRenderSettings = originalRenderSettings.Clone();
        DraftSinger = originalSinger;
        HasSinger = originalSinger is { Found: true };
        DraftSingerName = originalSinger?.Name ?? L.S("TrackSettings.NoSinger");
        DraftPhonemizerName = originalPhonemizer?.Tag ?? string.Empty;
        DraftRenderer = draftRenderSettings.renderer ?? string.Empty;
        DraftResampler = draftRenderSettings.resampler ?? string.Empty;
        DraftWavtool = draftRenderSettings.wavtool ?? string.Empty;
        PickSingerCommand = ReactiveCommand.Create(() => RaiseClose(TrackSettingsAction.PickSinger));
        PickPhonemizerCommand = ReactiveCommand.Create(() => RaiseClose(TrackSettingsAction.PickPhonemizer));
        PickRendererCommand = ReactiveCommand.Create(() => RaiseClose(TrackSettingsAction.PickRenderer),
            this.WhenAnyValue(viewModel => viewModel.IsRendererEnabled));
        EditExpressionsCommand = ReactiveCommand.Create(() => RaiseClose(TrackSettingsAction.EditExpressions));
        ApplyCommand = ReactiveCommand.Create(() => RaiseClose(TrackSettingsAction.Apply));
        CancelCommand = ReactiveCommand.Create(() => RaiseClose(TrackSettingsAction.Cancel));
        RebuildRendererChoices(false);
    }

    public void SetSinger(USinger singer)
    {
        if (ReferenceEquals(DraftSinger, singer)) return;
        changingSelection = true;
        try
        {
            DraftSinger = singer;
            HasSinger = singer.Found;
            DraftSingerName = singer.Name;
            if (ReferenceEquals(singer, originalSinger))
            {
                draftRenderSettings = originalRenderSettings.Clone();
                classicDraftInitialized = originalRenderSettings.renderer == CoreRenderers.CLASSIC;
                DraftRenderer = draftRenderSettings.renderer ?? string.Empty;
                DraftResampler = draftRenderSettings.resampler ?? string.Empty;
                DraftWavtool = draftRenderSettings.wavtool ?? string.Empty;
                draftPhonemizer = originalPhonemizer;
                DraftPhonemizerName = draftPhonemizer.Tag;
            }
            else
            {
                SelectSingerPhonemizer(singer);
            }
            UpdateDirtyState();
            RebuildRendererChoices(true);
        }
        finally
        {
            changingSelection = false;
            UpdateDirtyState();
        }
    }

    public void SetPhonemizer(Phonemizer phonemizer)
    {
        draftPhonemizer = phonemizer;
        DraftPhonemizerName = phonemizer.Tag;
        UpdateDirtyState();
    }

    public void SetExpressions(TrackExpressionSettingsDraft draft)
    {
        DraftTrackExpressions = draft.Descriptors.Select(expression => expression.Clone()).ToArray();
        Error = string.Empty;
    }

    public void SetRenderer(string renderer)
    {
        if (DraftRenderer == renderer) return;
        bool enteringClassic = renderer == CoreRenderers.CLASSIC && DraftRenderer != CoreRenderers.CLASSIC;
        DraftRenderer = renderer;
        draftRenderSettings.renderer = renderer;
        UpdateDirtyState();
        if (enteringClassic) SelectClassicDefaults();
        RebuildToolChoices(false);
        UpdateDirtyState();
    }

    public void SetResampler(string name)
    {
        if (DraftResampler == name) return;
        DraftResampler = name;
        draftRenderSettings.resampler = name;
        RebuildToolChoices(true);
        UpdateDirtyState();
    }

    public void SetWavtool(string name)
    {
        if (DraftWavtool == name) return;
        DraftWavtool = name;
        draftRenderSettings.wavtool = name;
        UpdateDirtyState();
        RebuildToolChoices(false);
        UpdateDirtyState();
    }

    public bool TryPrepareApply(out List<UCommand> commands, out bool singerChanged, out string error)
    {
        commands = [];
        error = string.Empty;
        singerChanged = !ReferenceEquals(DraftSinger, originalSinger);
        if (IsStale)
        {
            error = L.S("TrackSettings.Error.Stale");
            return false;
        }
        if (DocManager.Inst.HasOpenUndoGroup)
        {
            error = L.S("TrackSettings.Error.Busy");
            return false;
        }
        USinger? singer = DraftSinger;
        if (singerChanged && singer is { Found: true } && !SingerManager.Inst.Singers.Values.Any(candidate =>
                ReferenceEquals(candidate, singer) || !string.IsNullOrEmpty(candidate.Id) && candidate.Id == singer.Id))
        {
            error = L.S("TrackSettings.Error.SingerUnavailable");
            return false;
        }
        if (draftPhonemizer.GetType() != originalPhonemizer.GetType() &&
            PhonemizerFactory.Get(draftPhonemizer.GetType().FullName ?? string.Empty) == null)
        {
            error = L.S("TrackSettings.Error.PhonemizerUnavailable");
            return false;
        }
        if (singer is not { Found: true })
        {
            if (singerChanged || IsRenderSettingsDirty)
            {
                error = L.S("TrackSettings.Error.MissingSinger");
                return false;
            }
        }
        else if (singerChanged || IsRenderSettingsDirty)
        {
            string[] supportedRenderers = CoreRenderers.GetSupportedRenderers(singer.SingerType);
            if (supportedRenderers.Length == 0 || !supportedRenderers.Contains(DraftRenderer, StringComparer.Ordinal))
            {
                error = L.S("TrackSettings.Error.Renderer");
                return false;
            }
        }
        if (IsRenderSettingsDirty && DraftRenderer == CoreRenderers.CLASSIC && !HasRegisteredCompatibleToolPair())
        {
            error = L.S("TrackSettings.Error.Tools");
            return false;
        }

        URenderSettings renderSettings = draftRenderSettings.Clone();
        if (singer is { Found: true } && IsRenderSettingsDirty)
        {
            try
            {
                renderSettings.Validate(new UTrack { Singer = singer });
            }
            catch (Exception exception)
            {
                error = string.IsNullOrWhiteSpace(exception.Message) ? L.S("TrackSettings.Error.Apply") : exception.Message;
                return false;
            }
        }

        if (singerChanged) commands.Add(new TrackChangeSingerCommand(project, track, DraftSinger!));
        if (draftPhonemizer.GetType() != originalPhonemizer.GetType())
            commands.Add(new TrackChangePhonemizerCommand(project, track, draftPhonemizer));
        bool renderChanged = !SameRenderSettings(renderSettings, originalRenderSettings);
        if (renderChanged && singer is { Found: true })
            commands.Add(new TrackChangeRenderSettingCommand(project, track, renderSettings));
        if (!SameExpressions(DraftTrackExpressions, originalTrackExpressions))
        {
            commands.Add(new ConfigureExpressionsCommand(project, project.expressions.Values.ToArray(), track,
                DraftTrackExpressions.Select(expression => expression.Clone()).ToArray()));
        }
        return true;
    }

    private void SelectSingerPhonemizer(USinger singer)
    {
        Phonemizer? selected = null;
        if (!string.IsNullOrEmpty(singer.Id) && Preferences.Default.SingerPhonemizers.TryGetValue(singer.Id, out string? remembered))
            selected = CreatePhonemizer(remembered);
        selected ??= CreatePhonemizer(singer.DefaultPhonemizer);
        if (selected != null)
        {
            draftPhonemizer = selected;
            DraftPhonemizerName = selected.Tag;
        }
    }

    private static Phonemizer? CreatePhonemizer(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try { return PhonemizerFactory.Get(name)?.Create(); }
        catch { return null; }
    }

    private void RebuildRendererChoices(bool chooseCompatibleDefault)
    {
        Renderers.Clear();
        string[] supported = DraftSinger is { Found: true } singer
            ? CoreRenderers.GetSupportedRenderers(singer.SingerType)
            : [];
        HasNoSupportedRenderers = DraftSinger is { Found: true } && supported.Length == 0;
        IsRendererEnabled = DraftSinger is { Found: true } && supported.Length > 0;
        if (!IsRendererEnabled)
        {
            if (!string.IsNullOrEmpty(DraftRenderer)) Renderers.Add(DraftRenderer);
            IsClassic = false;
            RebuildToolChoices(false);
            return;
        }
        foreach (string renderer in supported) Renderers.Add(renderer);
        if (supported.Contains(DraftRenderer, StringComparer.Ordinal))
        {
            RebuildToolChoices(false);
            return;
        }
        if (!chooseCompatibleDefault && !string.IsNullOrEmpty(DraftRenderer))
        {
            Renderers.Add(DraftRenderer);
            RebuildToolChoices(false);
            return;
        }
        string preferred;
        try { preferred = CoreRenderers.GetDefaultRenderer(DraftSinger!.SingerType); }
        catch { preferred = string.Empty; }
        string fallback = supported.Contains(preferred, StringComparer.Ordinal) ? preferred : supported[0];
        DraftRenderer = fallback;
        draftRenderSettings.renderer = fallback;
        if (chooseCompatibleDefault) SelectClassicDefaults();
        RebuildToolChoices(false);
    }

    private void SelectClassicDefaults()
    {
        if (DraftRenderer != CoreRenderers.CLASSIC) return;
        if (classicDraftInitialized) return;

        string preferredResampler = Preferences.Default.DefaultResamplers.GetValueOrDefault(CoreRenderers.CLASSIC) ?? string.Empty;
        string preferredWavtool = Preferences.Default.DefaultWavtools.GetValueOrDefault(CoreRenderers.CLASSIC) ?? string.Empty;
        if (IsRegisteredCompatiblePair(preferredResampler, preferredWavtool))
        {
            SetClassicToolNames(preferredResampler, preferredWavtool);
            classicDraftInitialized = true;
            return;
        }

        try
        {
            URenderSettings defaults = new() { renderer = CoreRenderers.CLASSIC };
            defaults.Validate(new UTrack { Singer = DraftSinger! });
            if (IsRegisteredCompatiblePair(defaults.resampler, defaults.wavtool))
            {
                SetClassicToolNames(defaults.resampler ?? string.Empty, defaults.wavtool ?? string.Empty);
                classicDraftInitialized = true;
                return;
            }
        }
        catch { }

        foreach (IWavtool wavtool in ToolsManager.Inst.Wavtools)
        {
            string wavtoolName = wavtool.ToString() ?? string.Empty;
            string? resamplerName = CoreRenderers.GetSupportedResamplers(wavtool)
                .Select(tool => tool.ToString())
                .FirstOrDefault(name => IsRegisteredCompatiblePair(name, wavtoolName));
            if (resamplerName == null) continue;
            SetClassicToolNames(resamplerName, wavtoolName);
            break;
        }
        classicDraftInitialized = true;
    }

    private void SetClassicToolNames(string resampler, string wavtool)
    {
        DraftWavtool = wavtool;
        DraftResampler = resampler;
        draftRenderSettings.wavtool = wavtool;
        draftRenderSettings.resampler = resampler;
    }

    private bool HasRegisteredCompatibleToolPair() => IsRegisteredCompatiblePair(DraftResampler, DraftWavtool);

    private static bool IsRegisteredCompatiblePair(string? resamplerName, string? wavtoolName)
    {
        IResampler? resampler = ToolsManager.Inst.Resamplers.FirstOrDefault(tool => tool.ToString() == resamplerName);
        IWavtool? wavtool = ToolsManager.Inst.Wavtools.FirstOrDefault(tool => tool.ToString() == wavtoolName);
        return resampler != null && wavtool != null &&
            CoreRenderers.GetSupportedResamplers(wavtool).Any(tool => tool.ToString() == resamplerName) &&
            CoreRenderers.GetSupportedWavtools(resampler).Any(tool => tool.ToString() == wavtoolName);
    }

    private void RebuildToolChoices(bool resamplerWasChanged)
    {
        IsClassic = DraftRenderer == CoreRenderers.CLASSIC && IsRendererEnabled;
        Resamplers.Clear();
        Wavtools.Clear();
        if (!IsClassic)
        {
            HasCompatibleToolPair = true;
            SelectedResampler = null;
            SelectedWavtool = null;
            return;
        }

        IWavtool? runtimeWavtool = ToolsManager.Inst.Wavtools.FirstOrDefault(tool => tool.ToString() == DraftWavtool);
        IResampler? runtimeResampler = ToolsManager.Inst.Resamplers.FirstOrDefault(tool => tool.ToString() == DraftResampler);
        if (resamplerWasChanged && !IsRegisteredCompatiblePair(DraftResampler, DraftWavtool))
        {
            string? counterpart = runtimeResampler == null ? null : CoreRenderers.GetSupportedWavtools(runtimeResampler)
                .Select(tool => tool.ToString())
                .FirstOrDefault(name => IsRegisteredCompatiblePair(DraftResampler, name));
            if (counterpart != null)
            {
                DraftWavtool = counterpart;
                draftRenderSettings.wavtool = counterpart;
            }
        }
        else if (!resamplerWasChanged && IsRenderSettingsDirty && !IsRegisteredCompatiblePair(DraftResampler, DraftWavtool))
        {
            string? counterpart = runtimeWavtool == null ? null : CoreRenderers.GetSupportedResamplers(runtimeWavtool)
                .Select(tool => tool.ToString())
                .FirstOrDefault(name => IsRegisteredCompatiblePair(name, DraftWavtool));
            if (counterpart != null)
            {
                DraftResampler = counterpart;
                draftRenderSettings.resampler = counterpart;
            }
        }

        runtimeWavtool = ToolsManager.Inst.Wavtools.FirstOrDefault(tool => tool.ToString() == DraftWavtool);
        runtimeResampler = ToolsManager.Inst.Resamplers.FirstOrDefault(tool => tool.ToString() == DraftResampler);
        string[] supportedResamplers = CoreRenderers.GetSupportedResamplers(runtimeWavtool).Select(tool => tool.ToString()!).ToArray();
        string[] supportedWavtools = CoreRenderers.GetSupportedWavtools(runtimeResampler).Select(tool => tool.ToString()!).ToArray();
        foreach (string item in supportedResamplers) Resamplers.Add(new(item, true));
        foreach (string item in supportedWavtools) Wavtools.Add(new(item, true));
        if (!string.IsNullOrEmpty(DraftResampler) && !Resamplers.Any(option => option.Name == DraftResampler))
            Resamplers.Add(new(DraftResampler, false));
        if (!string.IsNullOrEmpty(DraftWavtool) && !Wavtools.Any(option => option.Name == DraftWavtool))
            Wavtools.Add(new(DraftWavtool, false));
        SelectedResampler = Resamplers.FirstOrDefault(option => option.Name == DraftResampler);
        SelectedWavtool = Wavtools.FirstOrDefault(option => option.Name == DraftWavtool);
        HasCompatibleToolPair = HasRegisteredCompatibleToolPair();
    }

    private void UpdateDirtyState()
    {
        IsRenderSettingsDirty = !SameRenderSettings(draftRenderSettings, originalRenderSettings);
        if (!changingSelection) Error = string.Empty;
    }

    private static bool SameRenderSettings(URenderSettings first, URenderSettings second) =>
        first.renderer == second.renderer && first.resampler == second.resampler && first.wavtool == second.wavtool;

    private static bool SameExpressions(IReadOnlyList<UExpressionDescriptor> first, IReadOnlyList<UExpressionDescriptor> second) =>
        first.Count == second.Count && first.Zip(second).All(pair => pair.First.Equals(pair.Second));

    public void Dispose()
    {
        PickSingerCommand.Dispose();
        PickPhonemizerCommand.Dispose();
        PickRendererCommand.Dispose();
        EditExpressionsCommand.Dispose();
        ApplyCommand.Dispose();
        CancelCommand.Dispose();
    }
}
