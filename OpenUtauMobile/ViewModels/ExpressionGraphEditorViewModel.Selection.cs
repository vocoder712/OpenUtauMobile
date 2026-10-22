using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia;
using OpenUtau.Core;
using OpenUtau.Core.ExpressionGraph;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Services.ExpressionGraph;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>图内集合选择与剪贴板操作；文档变更仍由原有图命令一次提交。</summary>
public sealed partial class ExpressionGraphEditorViewModel
{
    /// <summary>显式多选状态，退出时不保留普通单选。</summary>
    [Reactive] public bool IsMultiSelect { get; private set; }
    /// <summary>以独立集合快照通知画布，不能在通知后原地修改。</summary>
    [Reactive] public IReadOnlySet<int> SelectedNodeIds { get; private set; } = new HashSet<int>();
    /// <summary>同工程的其他编辑窗口复制节点后也立即更新。</summary>
    [Reactive] public bool HasClipboard { get; private set; }
    public bool HasSelectedNodes => SelectedNodeIds.Count > 0;
    public string SelectionSummary => string.Format(L.S("GraphSelection.Count"), SelectedNodeIds.Count);
    public ReactiveCommand<Unit, Unit> EnterMultiSelectCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> ExitMultiSelectCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SelectAllNodesCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> InvertNodeSelectionCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> ClearNodeSelectionCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> CopyNodesCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> PasteNodesCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> DeleteSelectedNodesCommand { get; private set; } = null!;
    public event Action<IReadOnlySet<int>>? RequestRevealNodes;
    private (Point Center, double Scale, int Count)? lastPaste;

    private void InitializeSelectionCommands()
    {
        EnterMultiSelectCommand = ReactiveCommand.Create(EnterMultiSelect).DisposeWith(_subscriptions);
        ExitMultiSelectCommand = ReactiveCommand.Create(ExitMultiSelect).DisposeWith(_subscriptions);
        SelectAllNodesCommand = ReactiveCommand.Create(() => SelectAllOrInvert(false)).DisposeWith(_subscriptions);
        InvertNodeSelectionCommand = ReactiveCommand.Create(() => SelectAllOrInvert(true)).DisposeWith(_subscriptions);
        ClearNodeSelectionCommand = ReactiveCommand.Create(() => { if (IsMultiSelect && CanStartTopology()) SetNodeSelection([]); }).DisposeWith(_subscriptions);
        CopyNodesCommand = ReactiveCommand.Create(CopySelectedNodes).DisposeWith(_subscriptions);
        PasteNodesCommand = ReactiveCommand.Create(PasteNodes).DisposeWith(_subscriptions);
        DeleteSelectedNodesCommand = ReactiveCommand.CreateFromTask(DeleteSelectedNodesAsync).DisposeWith(_subscriptions);
        GraphNodeClipboard.Changed += ClipboardChanged;
        ClipboardChanged();
    }

    private void ClipboardChanged()
    {
        HasClipboard = ContextIsCurrent() && GraphNodeClipboard.HasContent(_project);
        lastPaste = null;
    }

    /// <summary>普通点选保留原单选编辑；多选只切换成员，空白点击不清空。</summary>
    public void SelectCanvasNode(string? key)
    {
        if (!CanStartTopology() || key == null || Scene is not { } scene || !scene.ByKey.TryGetValue(key, out GraphVisualNode? node)) return;
        if (!IsMultiSelect) { SelectedKey = key; return; }
        if (!IsSelectable(node.Data.id)) { TopologyError = L.S("GraphSelection.InvalidNode"); return; }
        HashSet<int> ids = new(SelectedNodeIds);
        if (!ids.Add(node.Data.id)) ids.Remove(node.Data.id);
        SetNodeSelection(ids);
    }

    /// <summary>进入保留普通单选，先提交有效参数草稿；不隐式修改工程选择。</summary>
    private void EnterMultiSelect()
    {
        if (IsMultiSelect || !CanStartTopology() || !ResolveParameterEdits()) return;
        int[] ids = SelectedNode is { } node && IsSelectable(node.Data.id) ? [node.Data.id] : [];
        IsMultiSelect = true;
        IsSearchOpen = false;
        SelectedKey = null;
        SetNodeSelection(ids);
        this.RaisePropertyChanged(nameof(ShowInspector));
    }

    /// <summary>退出总是清空选区，不回退为原单选。</summary>
    private void ExitMultiSelect()
    {
        IsMultiSelect = false;
        IsSearchOpen = false;
        SelectedKey = null;
        SetNodeSelection([]);
        this.RaisePropertyChanged(nameof(ShowInspector));
    }

    private bool IsSelectable(int id) => CurrentNode(id) is { } node && GraphNodeTypes.TryGet(node.type, out _)
        && float.IsFinite(node.x) && float.IsFinite(node.y);

    /// <summary>集合操作只扫描一次当前图，避免逐成员重复扫描整个节点列表。</summary>
    private HashSet<int> GetSelectableIds() => (CurrentGraph()?.nodes ?? []).Where(node => node != null)
        .GroupBy(node => node.id).Where(group => group.Count() == 1)
        .Select(group => group.First()).Where(node => node.parameters != null && GraphNodeTypes.TryGet(node.type, out _)
            && float.IsFinite(node.x) && float.IsFinite(node.y)).Select(node => node.id).ToHashSet();

    private void SetNodeSelection(IEnumerable<int> ids)
    {
        HashSet<int> next = new(ids);
        if (!SelectedNodeIds.SetEquals(next)) SelectedNodeIds = next;
        this.RaisePropertyChanged(nameof(HasSelectedNodes));
        this.RaisePropertyChanged(nameof(SelectionSummary));
        UpdateSearch();
    }

    /// <summary>以最新图对齐唯一节点身份，删除或重复的成员不能沿用旧展示键。</summary>
    private void SynchronizeNodeSelection()
    {
        if (!IsAvailable)
        {
            IsMultiSelect = false;
            SetNodeSelection([]);
        }
        else if (IsMultiSelect)
        {
            HashSet<int> selectable = GetSelectableIds();
            SetNodeSelection(SelectedNodeIds.Where(selectable.Contains));
        }
        else SetNodeSelection(SelectedNode is { } node ? [node.Data.id] : []);
        HasClipboard = ContextIsCurrent() && GraphNodeClipboard.HasContent(_project);
    }

    private void SelectAllOrInvert(bool invert)
    {
        if (!IsMultiSelect || !CanStartTopology() || Scene == null) return;
        HashSet<int> selectable = GetSelectableIds();
        if (Scene.Nodes.Count != selectable.Count) { TopologyError = L.S("GraphSelection.InvalidNode"); return; }
        SetNodeSelection(Scene.Nodes.Select(node => node.Data.id).Where(id => !invert || !SelectedNodeIds.Contains(id)).ToArray());
        TopologyError = string.Empty;
    }

    private void CopySelectedNodes()
    {
        if (!CanStartTopology() || !HasSelectedNodes || !ResolveParameterEdits()) return;
        try
        {
            if (!SelectedNodeIds.IsSubsetOf(GetSelectableIds())) { TopologyError = L.S("GraphSelection.InvalidNode"); return; }
            UExpressionGraph graph = CurrentGraph()!;
            UExpressionGraph copied = ExpressionGraphEdits.CopyNodes(graph, SelectedNodeIds);
            if (ExpressionGraphProgram.Compile(copied, out string? error) == null) { TopologyError = error ?? L.S("GraphSelection.InvalidNode"); return; }
            GraphNodeClipboard.Set(_project, copied);
            int external = graph.links.Count(link => SelectedNodeIds.Contains(link.from) != SelectedNodeIds.Contains(link.to));
            ToastService.Enqueue(string.Format(L.S(external == 0 ? "GraphSelection.Copied" : "GraphSelection.CopiedWithoutExternal"), copied.nodes.Count));
            TopologyError = string.Empty;
        }
        catch (Exception exception) { TopologyError = exception.Message; }
    }

    private string? ValidatePaste(UExpressionGraph target, UExpressionGraph copied)
    {
        if (string.IsNullOrEmpty(target.renderer) || string.IsNullOrEmpty(copied.renderer)
            || Renderers.GetExpressionGraphSlot(target.renderer) != Renderers.GetExpressionGraphSlot(copied.renderer))
            return L.S("GraphSelection.IncompatibleRenderer");
        if (ExpressionGraphProgram.Compile(target, out string? problem) == null)
            return L.S("GraphSelection.InvalidTarget") + "\n" + problem;
        if (target.nodes.Count > 0 && (long)target.nodes.Max(node => node.id) + copied.nodes.Count > int.MaxValue)
            return L.S("GraphSelection.IdOverflow");
        foreach (UGraphNode node in copied.nodes)
        {
            if (!GraphNodeTypes.TryGet(node.type, out GraphNodeType? type)) return L.S("GraphSelection.InvalidNode");
            if (type.NeedsAbbr && !GraphNodeTypes.ExpressionChoices(_project, node.type, target.renderer).Any(exp => exp.abbr == node.GetString("abbr")))
                return string.Format(L.S("GraphSelection.MissingExpression"), node.GetString("abbr"));
            if (type.IsOutput && target.nodes.Any(other => GraphNodeTypes.TryGet(other.type, out GraphNodeType? otherType)
                && otherType.Role == type.Role && (type.Role == GraphNodeRole.PitchOutput || other.GetString("abbr") == node.GetString("abbr"))))
                return L.S("GraphSelection.OutputConflict");
        }
        return null;
    }

    /// <summary>在最新图副本中预检并一次粘贴；失败不改图、选区或剪贴板。</summary>
    private void PasteNodes()
    {
        if (!CanStartTopology() || !ResolveParameterEdits() || GraphNodeClipboard.Read(_project) is not { } copied || copied.nodes.Count == 0) return;
        try
        {
            UExpressionGraph current = CurrentGraph()!;
            string? error = ValidatePaste(current, copied);
            if (error != null) { TopologyError = error; return; }
            ExpressionGraphScene source = new(copied);
            Point center = GetInsertionPoint?.Invoke() ?? new Point(80, 36);
            double scale = Viewport.Scale;
            int repeat = lastPaste is { } last && last.Center == center && last.Scale == scale ? last.Count : 0;
            Vector offset = center - source.Bounds.Center + new Vector(24, 24) * repeat / scale;
            List<int>? added = null;
            error = CommitTopology(GraphFingerprint(current), draft =>
            {
                added = ExpressionGraphEdits.PasteNodes(draft, copied, (float)offset.X, (float)offset.Y);
                if (draft.nodes.Any(node => !float.IsFinite(node.x) || !float.IsFinite(node.y))) return L.S("GraphViewer.InvalidPosition");
                return ExpressionGraphProgram.Compile(draft, out string? problem) == null ? problem ?? L.S("GraphSelection.InvalidNode") : null;
            });
            TopologyError = error ?? string.Empty;
            if (error != null || added == null) return;
            IsSearchOpen = false;
            if (added.Count > 1 || IsMultiSelect)
            {
                IsMultiSelect = true; SelectedKey = null; SetNodeSelection(added);
                this.RaisePropertyChanged(nameof(ShowInspector));
            }
            else SelectedKey = Scene!.Nodes.Single(node => node.Data.id == added[0]).Key;
            RequestRevealNodes?.Invoke(SelectedNodeIds);
            lastPaste = (GetInsertionPoint?.Invoke() ?? center, Viewport.Scale, repeat + 1);
        }
        catch (Exception exception) { TopologyError = exception.Message; }
    }

    private async Task DeleteSelectedNodesAsync()
    {
        if (!IsMultiSelect || !CanStartTopology() || !HasSelectedNodes || !SelectedNodeIds.IsSubsetOf(GetSelectableIds())) return;
        HashSet<int> ids = new(SelectedNodeIds);
        UExpressionGraph graph = CurrentGraph()!;
        string expected = GraphFingerprint(graph);
        int links = graph.links.Count(link => ids.Contains(link.from) || ids.Contains(link.to));
        IsBusy = true;
        try
        {
            string? result = await OptionConfirmPopupService.ShowAsync(L.S("GraphSelection.Delete"),
                string.Format(L.S("GraphSelection.DeleteMessage"), ids.Count, links),
                new OptionConfirmOption[] { new(L.S("Common.Cancel"), "cancel", isDefault: true), new(L.S("Common.Delete"), "delete", isDestructive: true) });
            if (result != "delete") return;
            TopologyError = CommitTopology(expected, draft =>
            {
                draft.nodes.RemoveAll(node => ids.Contains(node.id));
                draft.links.RemoveAll(link => ids.Contains(link.from) || ids.Contains(link.to));
                return null;
            }) ?? string.Empty;
        }
        finally { IsBusy = false; Refresh(); }
    }
}
