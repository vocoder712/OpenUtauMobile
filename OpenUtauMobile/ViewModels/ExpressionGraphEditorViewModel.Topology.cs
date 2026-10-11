using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia;
using OpenUtau.Core;
using OpenUtau.Core.ExpressionGraph;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Services.ExpressionGraph;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>一个输入的来源汇总；损坏或重复的连接仍可显示并明确断开。</summary>
public sealed record GraphInputConnection(int NodeId, string Port, string Label, string Source, bool HasConnection, bool IsEditable, string? TargetKey = null, GraphParameterEdit? Fallback = null);

/// <summary>拓扑修改只发生在确认后，复用 Core 校验与既有图命令。</summary>
public sealed partial class ExpressionGraphEditorViewModel
{
    [Reactive] public IReadOnlyList<GraphInputConnection> Inputs { get; private set; } = [];
    [Reactive] public bool CanDeleteNode { get; private set; }
    [Reactive] public string TopologyError { get; private set; } = string.Empty;
    public Func<Point>? GetInsertionPoint { get; set; }
    public ReactiveCommand<Unit, Unit> AddNodeCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> DeleteNodeCommand { get; private set; } = null!;
    public ReactiveCommand<GraphInputConnection, Unit> ChangeSourceCommand { get; private set; } = null!;
    public ReactiveCommand<GraphInputConnection, Unit> DisconnectCommand { get; private set; } = null!;

    private void InitializeTopologyCommands()
    {
        AddNodeCommand = ReactiveCommand.CreateFromTask(AddNodeAsync).DisposeWith(_subscriptions);
        DeleteNodeCommand = ReactiveCommand.CreateFromTask(DeleteNodeAsync).DisposeWith(_subscriptions);
        ChangeSourceCommand = ReactiveCommand.CreateFromTask<GraphInputConnection>(ChangeSourceAsync).DisposeWith(_subscriptions);
        DisconnectCommand = ReactiveCommand.Create<GraphInputConnection>(Disconnect).DisposeWith(_subscriptions);
    }

    /// <summary>每次选择或文档修改后汇总原始来源，不能把不可绘制的连接误当作未连接。</summary>
    private void UpdateTopologySelection()
    {
        GraphParameterEdit[] ordinary = Parameters.Where(parameter => SelectedNode?.Inputs.Contains(parameter.Name) != true).ToArray();
        if (!NonInputParameters.SequenceEqual(ordinary)) NonInputParameters = ordinary;
        UExpressionGraph? graph = CurrentGraph();
        GraphVisualNode? selected = SelectedNode;
        CanDeleteNode = selected != null && graph?.nodes?.Count(node => node != null && node.id == selected.Data.id) == 1;
        if (selected?.Type == null || graph == null) { Inputs = []; return; }
        Dictionary<int, UGraphNode[]> nodes = (graph.nodes ?? []).Where(node => node != null).GroupBy(node => node.id)
            .ToDictionary(group => group.Key, group => group.ToArray());
        Dictionary<string, UGraphLink[]> incoming = (graph.links ?? []).Where(link => link != null && link.to == selected.Data.id && link.toPort != null)
            .GroupBy(link => link.toPort!).ToDictionary(group => group.Key, group => group.ToArray());
        GraphInputConnection[] inputs = selected.Inputs.Select(port =>
        {
            UGraphLink[] links = incoming.GetValueOrDefault(port) ?? [];
            string[] sources = links.Select(link =>
            {
                UGraphNode? source = nodes.TryGetValue(link.from, out UGraphNode[]? matches) && matches.Length == 1 ? matches[0] : null;
                string title = source != null ? $"{ExpressionGraphScene.Humanize(source.type ?? "?")} #{source.id}" : $"#{link.from} ({L.S("GraphViewer.InvalidLink")})";
                string output = link.fromPort ?? (GraphNodeTypes.TryGet(source?.type, out GraphNodeType? type) ? type.Outputs.FirstOrDefault() : null) ?? "?";
                return $"{title} · {ExpressionGraphScene.Humanize(output)}";
            }).ToArray();
            return new GraphInputConnection(selected.Data.id, port, $"{L.S("GraphViewer.Input")} · {ExpressionGraphScene.Humanize(port)}",
                sources.Length == 0 ? L.S("GraphViewer.Unconnected") : string.Join("\n", sources), links.Length > 0, CanDeleteNode,
                links.Length == 1 ? Scene?.Incoming[selected.Key].FirstOrDefault(link => link.ToPort == port)?.From.Key : null, Parameters.FirstOrDefault(field => field.Name == port));
        }).ToArray();
        if (!Inputs.SequenceEqual(inputs)) Inputs = inputs;
    }

    private bool CanStartTopology() => ContextIsCurrent() && !IsBusy && !DocManager.Inst.HasOpenUndoGroup && CurrentGraph() != null;
    private static string GraphFingerprint(UExpressionGraph graph) => Yaml.DefaultSerializer.Serialize(graph);

    /// <summary>先编辑并校验副本，确认有变化后才提交；确认期间的外部修改不能被覆盖。</summary>
    private string? CommitTopology(string expected, Func<UExpressionGraph, string?> edit)
    {
        if (!ContextIsCurrent() || DocManager.Inst.HasOpenUndoGroup || CurrentGraph() is not { } current)
            return L.S("ExpressionGraph.StaleProject");
        if (GraphFingerprint(current) != expected) return L.S("GraphTopology.Stale");
        try
        {
            UExpressionGraph copy = current.Clone();
            string? error = edit(copy);
            if (error != null) return error;
            if (GraphFingerprint(copy) == expected) return null;
            ExpressionGraphLibraryInfo.Apply(_project, draft =>
            {
                int index = draft.Graphs.FindIndex(graph => graph.id == _graphId);
                draft.Graphs[index] = copy;
            });
            Refresh();
            return null;
        }
        catch (Exception exception) { return exception.Message; }
    }

    /// <summary>在标题起笔时校验编辑会话；释放时只提交一次坐标变化，过期图不能被覆盖。</summary>
    public Action<Vector>? PrepareNodeMove(string key)
    {
        if (key != SelectedKey || !CanStartTopology() || !CanDeleteNode || !ResolveParameterEdits()
            || SelectedNode is not { } selected || CurrentGraph() is not { } graph) return null;
        int id = selected.Data.id;
        float x = selected.Data.x, y = selected.Data.y;
        if (!float.IsFinite(x) || !float.IsFinite(y)) return null;
        string expected = GraphFingerprint(graph);
        return delta =>
        {
            if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y)) return;
            float targetX = (float)(x + delta.X), targetY = (float)(y + delta.Y);
            if (!float.IsFinite(targetX) || !float.IsFinite(targetY)) return;
            TopologyError = CommitTopology(expected, copy =>
            {
                var node = copy.nodes.Single(node => node.id == id);
                node.x = targetX; node.y = targetY; return null;
            }) ?? string.Empty;
        };
    }

    private async Task AddNodeAsync()
    {
        if (!CanStartTopology() || !ResolveParameterEdits()) return;
        string expected = GraphFingerprint(CurrentGraph()!);
        Point center = GetInsertionPoint?.Invoke() ?? new Point(80, 36);
        TopologyError = string.Empty; IsBusy = true;
        try
        {
            GraphPickerItem[] items = GraphNodeTypes.Categories.SelectMany(category => category.types.Select(type => new GraphPickerItem(type,
                ExpressionGraphScene.Humanize(type), ExpressionGraphScene.Humanize(category.category)))).ToArray();
            using GraphItemPickerViewModel picker = new(L.S("GraphTopology.AddNode"), string.Empty, items, item =>
            {
                int? addedId = null;
                string? error = CommitTopology(expected, graph =>
                {
                    if (!GraphNodeTypes.TryGet(item.Key, out _)) return L.S("GraphViewer.UnknownNode");
                    float x = (float)(center.X - 80), y = (float)(center.Y - 36);
                    if (!float.IsFinite(x) || !float.IsFinite(y)) return L.S("GraphViewer.InvalidPosition");
                    HashSet<int> ids = graph.nodes.Select(node => node.id).ToHashSet();
                    int maximum = ids.Count == 0 ? 0 : Math.Max(0, ids.Max());
                    int id = maximum < int.MaxValue ? maximum + 1 : 1;
                    while (ids.Contains(id)) id = checked(id + 1);
                    UGraphNode node = new() { id = id, type = item.Key, x = x, y = y };
                    foreach (GraphNodeParameter parameter in GraphNodeTypes.ParametersOf(item.Key))
                        if (parameter.Default != null) node.Set(parameter.Name, parameter.Default);
                    graph.nodes.Add(node); addedId = id; return null;
                });
                if (error == null && addedId is { } id)
                {
                    SelectedKey = Scene!.Nodes.Single(node => node.Data.id == id).Key;
                    IsSearchOpen = false; RequestLocate?.Invoke(SelectedKey!);
                }
                return error;
            });
            await PopupService.Show<bool>(new GraphItemPickerPopup(), picker);
        }
        finally { IsBusy = false; Refresh(); }
    }

    private async Task DeleteNodeAsync()
    {
        if (!CanStartTopology() || !CanDeleteNode || SelectedNode == null || !ResolveParameterEdits()) return;
        int id = SelectedNode.Data.id;
        UExpressionGraph graph = CurrentGraph()!;
        string expected = GraphFingerprint(graph);
        int linkCount = (graph.links ?? []).Count(link => link != null && (link.from == id || link.to == id));
        string title = SelectedNode.Title;
        TopologyError = string.Empty; IsBusy = true;
        try
        {
            string? result = await OptionConfirmPopupService.ShowAsync(L.S("GraphTopology.DeleteNode"),
                string.Format(L.S("GraphTopology.DeleteMessage"), title, linkCount),
                new OptionConfirmOption[] { new(L.S("Common.Cancel"), "cancel", isDefault: true), new(L.S("Common.Delete"), "delete", isDestructive: true) });
            if (result == "delete") TopologyError = CommitTopology(expected, copy =>
            {
                if (copy.nodes.Count(node => node.id == id) != 1) return L.S("GraphTopology.Stale");
                ExpressionGraphEdits.RemoveNode(copy, id); return null;
            }) ?? string.Empty;
        }
        finally { IsBusy = false; Refresh(); }
    }

    private bool IsCurrentInput(GraphInputConnection input) => SelectedNode?.Data.id == input.NodeId
        && CurrentNode(input.NodeId) is { } node && GraphNodeTypes.TryGet(node.type, out GraphNodeType? type) && type.Ports.Contains(input.Port);

    private async Task ChangeSourceAsync(GraphInputConnection input)
    {
        if (!CanStartTopology() || !IsCurrentInput(input) || !ResolveParameterEdits()) return;
        UExpressionGraph graph = CurrentGraph()!;
        string expected = GraphFingerprint(graph);
        Dictionary<string, (int Node, string Port)> sources = [];
        List<GraphPickerItem> items = [];
        foreach (IGrouping<int, UGraphNode> group in (graph.nodes ?? []).Where(node => node != null).GroupBy(node => node.id))
        {
            if (group.Count() != 1) continue;
            UGraphNode node = group.First();
            if (!GraphNodeTypes.TryGet(node.type, out GraphNodeType? type)) continue;
            foreach (string port in type.Outputs)
            {
                string key = $"{node.id}:{port}";
                sources.Add(key, (node.id, port));
                string summary = string.Join(" · ", (node.parameters ?? []).Take(2).Select(pair => $"{pair.Key}: {pair.Value}"));
                items.Add(new(key, $"{ExpressionGraphScene.Humanize(node.type!)} #{node.id} · {ExpressionGraphScene.Humanize(port)}", summary));
            }
        }
        UGraphLink[] oldLinks = (graph.links ?? []).Where(link => link != null && link.to == input.NodeId && link.toPort == input.Port).ToArray();
        string? selectedKey = oldLinks.Length == 1 ? sources.FirstOrDefault(pair => pair.Value.Node == oldLinks[0].from
            && (pair.Value.Port == oldLinks[0].fromPort || oldLinks[0].fromPort == null)).Key : null;
        TopologyError = string.Empty; IsBusy = true;
        try
        {
            using GraphItemPickerViewModel picker = new(L.S("GraphTopology.ChangeSource"), string.Empty, items, item => CommitTopology(expected, copy =>
            {
                (int from, string port) = sources[item.Key];
                UGraphLink[] links = copy.links.Where(link => link.to == input.NodeId && link.toPort == input.Port).ToArray();
                if (links.Length == 1 && links[0].from == from && (links[0].fromPort ?? GraphNodeTypes.All[copy.nodes.Single(node => node.id == from).type!].Outputs[0]) == port)
                    return null;
                return ExpressionGraphEdits.TryLink(copy, from, input.NodeId, input.Port, port) ? null : L.S("GraphTopology.InvalidLink");
            }), selectedKey);
            await PopupService.Show<bool>(new GraphItemPickerPopup(), picker);
        }
        finally { IsBusy = false; Refresh(); }
    }

    private void Disconnect(GraphInputConnection input)
    {
        if (!CanStartTopology() || !IsCurrentInput(input) || !ResolveParameterEdits()) return;
        string expected = GraphFingerprint(CurrentGraph()!);
        TopologyError = CommitTopology(expected, copy =>
        {
            copy.links.RemoveAll(link => link.to == input.NodeId && link.toPort == input.Port);
            return null;
        }) ?? string.Empty;
    }
}
