using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.ExpressionGraph;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services.Tracks;

/// <summary>保留可用的指定图；没有指定图或指定图不可用时，始终跟随工程默认。</summary>
public static class TrackExpressionGraphSelection
{
    public static string? Resolve(UProject project, string? renderer, string? requested)
    {
        bool Usable(string? id) => !string.IsNullOrEmpty(id)
            && project.expressionGraphs?.FirstOrDefault(graph => graph.id == id) is { } graph
            && RendererGraphOption.IsCompatible(graph, renderer)
            && ExpressionGraphProgram.Compile(graph, out _) != null;
        if (Usable(requested)) return requested;
        return null;
    }

    public static void Revalidate(UProject project, UTrack track)
    {
        if (!ReferenceEquals(DocManager.Inst.Project, project) || !project.tracks.Contains(track)) return;
        string? graph = Resolve(project, track.RendererSettings.renderer, track.ExpressionGraph);
        if (graph == track.ExpressionGraph) return;
        bool ownGroup = !DocManager.Inst.HasOpenUndoGroup;
        if (ownGroup) DocManager.Inst.StartUndoGroup();
        try { SetOverride(project, track, graph); }
        finally { if (ownGroup) DocManager.Inst.EndUndoGroup(); }
    }

    public static void ApplyRenderer(UProject project, UTrack track, RendererSettingsSelection selection)
    {
        if (!ReferenceEquals(DocManager.Inst.Project, project) || !project.tracks.Contains(track)) return;
        URenderSettings settings = selection.Settings.Clone();
        settings.Validate(track);
        string? graph = Resolve(project, settings.renderer, selection.ExpressionGraph);
        URenderSettings original = track.RendererSettings;
        bool changed = settings.renderer != original.renderer || settings.resampler != original.resampler || settings.wavtool != original.wavtool;
        if (!changed && graph == track.ExpressionGraph) return;
        DocManager.Inst.StartUndoGroup();
        try
        {
            if (changed) DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(project, track, settings));
            SetOverride(project, track, graph);
        }
        finally { DocManager.Inst.EndUndoGroup(); }
    }

    private static void SetOverride(UProject project, UTrack track, string? graph)
    {
        int index = project.tracks.IndexOf(track);
        if (index < 0 || !ReferenceEquals(DocManager.Inst.Project, project) || track.ExpressionGraph == graph) return;
        ExpressionGraphEdits.Draft draft = new(project);
        draft.TrackOverrides[index] = graph;
        DocManager.Inst.ExecuteCmd(new SetExpressionGraphsCommand(project, draft.ToState()));
    }
}
