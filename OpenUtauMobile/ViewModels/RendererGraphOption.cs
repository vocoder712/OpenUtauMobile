using System.Linq;
using OpenUtau.Core.ExpressionGraph;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>表情图选项以 ID 标识；名称仅用于显示。</summary>
public sealed record RendererGraphOption(string? Id, string Label)
{
    public override string ToString() => Label;

    public static string Name(UExpressionGraph graph) => string.IsNullOrWhiteSpace(graph.name) ? graph.id : graph.name;

    public static bool IsCompatible(UExpressionGraph graph, string? renderer) =>
        renderer != null && graph.renderer != null &&
        Renderers.GetExpressionGraphSlot(graph.renderer) == Renderers.GetExpressionGraphSlot(renderer);

    public static string Problem(UProject project, string? renderer, string? id)
    {
        if (string.IsNullOrEmpty(id)) return string.Empty;
        UExpressionGraph? graph = project.expressionGraphs?.FirstOrDefault(graph => graph.id == id);
        if (graph == null) return string.Format(L.S("RendererSettings.Graph.Missing"), id);
        if (!IsCompatible(graph, renderer)) return string.Format(L.S("RendererSettings.Graph.Incompatible"), Name(graph));
        return ExpressionGraphProgram.Compile(graph, out string? error) == null
            ? string.Format(L.S("RendererSettings.Graph.Invalid"), Name(graph), error) : string.Empty;
    }

    public static bool CanKeep(UProject project, string? renderer, string? id, string? originalRenderer, string? originalId) =>
        (renderer == originalRenderer && id == originalId) || Problem(project, renderer, id).Length == 0;
}
