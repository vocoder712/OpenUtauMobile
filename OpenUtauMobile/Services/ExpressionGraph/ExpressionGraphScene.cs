using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using OpenUtau.Core.ExpressionGraph;
using OpenUtauMobile.Helpers;

namespace OpenUtauMobile.Services.ExpressionGraph;

/// <summary>只读节点快照；重复 ID 用出现序号区分，歧义连接不绑定到任意节点。</summary>
public sealed record GraphVisualNode(string Key, UGraphNode Data, GraphNodeType? Type, Rect Bounds,
    string Title, string Summary, string SearchText, string[] Inputs, string[] Outputs)
{
    public string[] InputLabels { get; } = Inputs.Select(ExpressionGraphScene.Humanize).ToArray();
    public string[] OutputLabels { get; } = Outputs.Select(ExpressionGraphScene.Humanize).ToArray();
    private double PortTop => Bounds.Top + 24 + (Summary.Length > 0 ? 20 : 0) + 10;
    /// <summary>输入端口在图坐标中的位置。</summary>
    public Point InputPoint(int index) => new(Bounds.Left, PortTop + index * 20);
    /// <summary>命名输出端口在图坐标中的位置。</summary>
    public Point OutputPoint(int index) => new(Bounds.Right, PortTop + index * 20);
}

/// <summary>有效连线的端点与包围盒；几何由画布缓存，查看不会写回 USTX。</summary>
public sealed record GraphVisualLink(int Index, GraphVisualNode From, GraphVisualNode To,
    string FromPort, string ToPort, Point Start, Point End, Rect Bounds);

/// <summary>排序一次后建立平衡包围盒树；查询复用调用方缓冲区，不扫描整个图。</summary>
public sealed class GraphSpatialIndex<T>
{
    private sealed record Branch(Rect Bounds, int Start, int Count, Branch? Left, Branch? Right);
    private readonly (T Value, Rect Bounds)[] _items;
    private readonly Branch? _root;

    /// <summary>静态排序与建树一次完成，后续视口变化仅查询。</summary>
    public GraphSpatialIndex(IEnumerable<T> items, Func<T, Rect> bounds)
    {
        _items = items.Select(item => (item, bounds(item))).OrderBy(item => item.Item2.Center.X).ThenBy(item => item.Item2.Center.Y).ToArray();
        if (_items.Length > 0) _root = Build(0, _items.Length);
    }

    private Branch Build(int start, int count)
    {
        if (count <= 8)
        {
            Rect bounds = _items[start].Bounds;
            for (int i = start + 1; i < start + count; i++) bounds = bounds.Union(_items[i].Bounds);
            return new(bounds, start, count, null, null);
        }
        int half = count / 2;
        Branch left = Build(start, half);
        Branch right = Build(start + half, count - half);
        return new(left.Bounds.Union(right.Bounds), start, count, left, right);
    }

    /// <summary>输出所有与区域相交的项；重叠严重的图最坏仍为线性复杂度。</summary>
    public void Query(Rect area, List<T> result)
    {
        result.Clear();
        if (_root != null) Query(_root, area, result);
    }

    private void Query(Branch branch, Rect area, List<T> result)
    {
        if (!branch.Bounds.Intersects(area)) return;
        if (branch.Left != null)
        {
            Query(branch.Left, area, result);
            Query(branch.Right!, area, result);
            return;
        }
        for (int i = branch.Start; i < branch.Start + branch.Count; i++)
            if (_items[i].Bounds.Intersects(area)) result.Add(_items[i].Value);
    }
}

/// <summary>图展示与邻接索引；构建 O((N+E) log(N+E))，只在文档图实例变化时更新。</summary>
public sealed class ExpressionGraphScene
{
    public IReadOnlyList<GraphVisualNode> Nodes { get; }
    public IReadOnlyList<GraphVisualLink> Links { get; }
    public IReadOnlyDictionary<string, GraphVisualNode> ByKey { get; }
    public IReadOnlyDictionary<string, List<GraphVisualLink>> Incoming { get; }
    public IReadOnlyDictionary<string, List<GraphVisualLink>> Outgoing { get; }
    public GraphSpatialIndex<GraphVisualNode> NodeIndex { get; }
    public GraphSpatialIndex<GraphVisualLink> LinkIndex { get; }
    public Rect Bounds { get; }
    public string Diagnostic { get; }

    /// <summary>保留全部节点原始信息，为可识别连线建立邻接和空间索引。</summary>
    public ExpressionGraphScene(UExpressionGraph graph)
    {
        List<GraphVisualNode> nodes = [];
        Dictionary<int, List<GraphVisualNode>> ids = [];
        List<string> diagnostics = [];
        foreach (UGraphNode node in graph.nodes ?? [])
        {
            if (node == null) { diagnostics.Add(L.S("GraphViewer.InvalidNode")); continue; }
            UGraphNode copy = new()
            {
                id = node.id, type = node.type, x = node.x, y = node.y,
                parameters = node.parameters == null ? [] : new(node.parameters),
            };
            GraphNodeTypes.TryGet(copy.type, out GraphNodeType? type);
            if (!ids.TryGetValue(copy.id, out List<GraphVisualNode>? same)) ids[copy.id] = same = [];
            string key = $"{copy.id}:{same.Count}";
            string title = $"{Humanize(Short(copy.type ?? "?", 80))} #{copy.id}";
            string summary = string.Join(" · ", copy.parameters.Take(2).Select(pair => $"{Short(pair.Key, 48)}: {Short(pair.Value, 100)}"));
            if (summary.Length > 256) summary = summary[..256] + "…";
            double x = float.IsFinite(copy.x) ? copy.x : 0;
            double y = float.IsFinite(copy.y) ? copy.y : 0;
            if (!float.IsFinite(copy.x) || !float.IsFinite(copy.y)) diagnostics.Add($"#{copy.id}: {L.S("GraphViewer.InvalidPosition")}");
            string[] inputs = type?.Ports ?? [];
            string[] outputs = type?.Outputs ?? [];
            double width = type == null ? 180 : type.Name == GraphNodeTypes.MaskedCurveInput ? 220
                : type.Name == GraphNodeTypes.PitchInput ? 140 : type.IsOutput ? 150
                : type.Role == GraphNodeRole.Process && summary.Length == 0 ? 96 : 160;
            GraphVisualNode visual = new(key, copy, type,
                new Rect(x, y, width, 32 + (summary.Length > 0 ? 20 : 0) + Math.Max(inputs.Length, outputs.Length) * 20), title, summary,
                title + " " + copy.type + " " + string.Join(" ", copy.parameters.Select(pair => pair.Key + " " + pair.Value)), inputs, outputs);
            nodes.Add(visual); same.Add(visual);
            if (type == null) diagnostics.Add($"#{copy.id}: {L.S("GraphViewer.UnknownNode")} ({copy.type})");
        }
        foreach (KeyValuePair<int, List<GraphVisualNode>> pair in ids)
            if (pair.Value.Count > 1) diagnostics.Add($"#{pair.Key}: {L.S("GraphViewer.DuplicateId")}");

        Dictionary<string, List<GraphVisualLink>> incoming = nodes.ToDictionary(node => node.Key, _ => new List<GraphVisualLink>());
        Dictionary<string, List<GraphVisualLink>> outgoing = nodes.ToDictionary(node => node.Key, _ => new List<GraphVisualLink>());
        List<GraphVisualLink> links = [];
        int index = 0;
        foreach (UGraphLink link in graph.links ?? [])
        {
            int ordinal = index++;
            if (link == null || !ids.TryGetValue(link.from, out List<GraphVisualNode>? from) || from.Count != 1
                || !ids.TryGetValue(link.to, out List<GraphVisualNode>? to) || to.Count != 1)
            {
                diagnostics.Add(link == null ? $"{L.S("GraphViewer.InvalidLink")} #{ordinal}"
                    : $"#{link.from} [{link.fromPort ?? "out"}] → #{link.to} [{link.toPort}]: {L.S("GraphViewer.InvalidLink")}"); continue;
            }
            int output = from[0].Type?.OutputIndex(link.fromPort) ?? -1;
            int input = Array.IndexOf(to[0].Inputs, link.toPort);
            if (output < 0 || input < 0)
            {
                diagnostics.Add($"#{link.from} [{link.fromPort ?? "out"}] → #{link.to} [{link.toPort}]: {L.S("GraphViewer.InvalidLink")}"); continue;
            }
            Point start = from[0].OutputPoint(output);
            Point end = to[0].InputPoint(input);
            double bend = Math.Max(40, Math.Abs(end.X - start.X) * .5);
            Rect bounds = new Rect(new Point(Math.Min(start.X, end.X - bend), Math.Min(start.Y, end.Y)),
                new Point(Math.Max(end.X, start.X + bend), Math.Max(start.Y, end.Y))).Inflate(4);
            GraphVisualLink visual = new(ordinal, from[0], to[0], from[0].Outputs[output], to[0].Inputs[input], start, end, bounds);
            links.Add(visual); incoming[to[0].Key].Add(visual); outgoing[from[0].Key].Add(visual);
        }
        Nodes = nodes; Links = links; ByKey = nodes.ToDictionary(node => node.Key);
        Incoming = incoming; Outgoing = outgoing;
        NodeIndex = new(nodes, node => node.Bounds);
        LinkIndex = new(links, link => link.Bounds);
        Rect all = nodes.Count > 0 ? nodes[0].Bounds : new Rect(0, 0, 232, 120);
        foreach (GraphVisualNode node in nodes) all = all.Union(node.Bounds);
        foreach (GraphVisualLink link in links) all = all.Union(link.Bounds);
        Bounds = all;
        try
        {
            if (ExpressionGraphProgram.Compile(graph, out string? error) == null && error != null) diagnostics.Add(error);
        }
        catch (Exception exception) { diagnostics.Add(exception.Message); }
        Diagnostic = string.Join("\n", diagnostics);
    }

    /// <summary>未提供本地化名称的节点与端口使用可读的类型名称。</summary>
    public static string Humanize(string text) => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.Replace('_', ' '));

    private static string Short(string? text, int limit) => text == null ? string.Empty : text.Length <= limit ? text : text[..limit] + "…";
}

/// <summary>仅保存会话视口；导航、详情展开和窗口变化不写入节点坐标。</summary>
public sealed class GraphViewport
{
    public double Scale { get; set; } = 1;
    public Vector Offset { get; set; }
    public bool Initialized { get; set; }
}
