using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.ExpressionGraph;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Services.ExpressionGraph;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>只读参数或端口信息；可定位项保存稳定节点键。</summary>
public sealed record GraphNodeDetail(string Label, string Value, string? TargetKey = null);
/// <summary>节点搜索结果，数量限制仅影响展示，不改动图。</summary>
public sealed record GraphSearchResult(string Key, string Title, string Summary);

/// <summary>单图编辑会话；参数经校验后提交命令，画布动作只影响本地状态。</summary>
public sealed class ExpressionGraphEditorViewModel : NavigateViewModelBase, ICmdSubscriber, IDisposable
{
    private readonly UProject _project;
    private readonly string _graphId;
    private readonly CompositeDisposable _subscriptions = [];
    private UExpressionGraph? _source;
    private bool _disposed;
    private bool _pending;
    private string? _selectedKey;
    private int? _parameterNodeId;
    private string? _parameterNodeType;

    public GraphViewport Viewport { get; } = new();
    [Reactive] public ExpressionGraphScene? Scene { get; private set; }
    [Reactive] public string Title { get; private set; } = string.Empty;
    [Reactive] public string Status { get; private set; } = string.Empty;
    [Reactive] public string Diagnostic { get; private set; } = string.Empty;
    [Reactive] public string Usage { get; private set; } = string.Empty;
    [Reactive] public bool IsAvailable { get; private set; }
    [Reactive] public bool IsEmpty { get; private set; }
    [Reactive] public bool IsBusy { get; private set; }
    [Reactive] public bool IsSearchOpen { get; set; }
    [Reactive] public bool IsDetailsExpanded { get; set; }
    [Reactive] public string Query { get; set; } = string.Empty;
    [Reactive] public IReadOnlyList<GraphSearchResult> SearchResults { get; private set; } = [];
    [Reactive] public IReadOnlyList<GraphNodeDetail> Details { get; private set; } = [];
    [Reactive] public GraphVisualNode? SelectedNode { get; private set; }
    [Reactive] public IReadOnlyList<GraphParameterEdit> Parameters { get; private set; } = [];
    [Reactive] public bool CanUndo { get; private set; }
    [Reactive] public bool CanRedo { get; private set; }
    public bool HasSelection => SelectedNode != null;
    public bool ShowInspector => HasSelection || IsSearchOpen;
    public event Action<string>? RequestLocate;
    public event Action<GraphParameterEdit>? RequestRevealParameter;

    public string? SelectedKey
    {
        get => _selectedKey;
        set
        {
            if (_disposed || value == _selectedKey) return;
            if (!ResolveParameterEdits()) return;
            this.RaiseAndSetIfChanged(ref _selectedKey, value);
            UpdateSelection();
        }
    }

    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> SearchCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseInspectorCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleDetailsCommand { get; }
    public ReactiveCommand<string, Unit> LocateCommand { get; }
    public ReactiveCommand<Unit, Unit> SettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> DiagnosticCommand { get; }
    public ReactiveCommand<Unit, Unit> UndoCommand { get; }
    public ReactiveCommand<Unit, Unit> RedoCommand { get; }

    /// <summary>通过工程身份与图 ID 创建只读会话，不持有可写节点引用。</summary>
    public ExpressionGraphEditorViewModel(MainViewModel navigator, UProject project, string graphId) : base(navigator)
    {
        _project = project; _graphId = graphId;
        BackCommand = ReactiveCommand.Create(OnBackRequested).DisposeWith(_subscriptions);
        SearchCommand = ReactiveCommand.Create(() => { if (ResolveParameterEdits()) IsSearchOpen = !IsSearchOpen; }).DisposeWith(_subscriptions);
        CloseInspectorCommand = ReactiveCommand.Create(() => { if (!ResolveParameterEdits()) return; IsSearchOpen = false; IsDetailsExpanded = false; SelectedKey = null; }).DisposeWith(_subscriptions);
        ToggleDetailsCommand = ReactiveCommand.Create(() => { if (!IsDetailsExpanded || ResolveParameterEdits()) IsDetailsExpanded = !IsDetailsExpanded; }).DisposeWith(_subscriptions);
        LocateCommand = ReactiveCommand.Create<string>(key =>
        {
            if (IsAvailable && Scene?.ByKey.ContainsKey(key) == true && ResolveParameterEdits())
            {
                SelectedKey = key; IsSearchOpen = false; RequestLocate?.Invoke(key);
            }
        }).DisposeWith(_subscriptions);
        SettingsCommand = ReactiveCommand.CreateFromTask(ShowSettingsAsync).DisposeWith(_subscriptions);
        DiagnosticCommand = ReactiveCommand.CreateFromTask(ShowDiagnosticAsync).DisposeWith(_subscriptions);
        UndoCommand = ReactiveCommand.Create(() => { if (CanUndo && ContextIsCurrent()) { CancelParameterPreviews(); DocManager.Inst.Undo(); Refresh(); } }).DisposeWith(_subscriptions);
        RedoCommand = ReactiveCommand.Create(() => { if (CanRedo && ContextIsCurrent()) { CancelParameterPreviews(); DocManager.Inst.Redo(); Refresh(); } }).DisposeWith(_subscriptions);
        this.WhenAnyValue(vm => vm.Query).Subscribe(_ => UpdateSearch()).DisposeWith(_subscriptions);
        this.WhenAnyValue(vm => vm.IsSearchOpen).Subscribe(_ => this.RaisePropertyChanged(nameof(ShowInspector))).DisposeWith(_subscriptions);
        if (Application.Current != null)
            Application.Current.GetResourceObservable("GraphEdit.Parameters")
                .Subscribe(_ => Dispatcher.UIThread.Post(() => Refresh(true))).DisposeWith(_subscriptions);
        DocManager.Inst.AddSubscriber(this);
        Refresh();
    }

    private void Refresh(bool languageChanged = false)
    {
        if (_disposed) return;
        UpdateUndoState();
        UExpressionGraph? graph = ReferenceEquals(DocManager.Inst.Project, _project)
            ? _project.expressionGraphs?.FirstOrDefault(graph => graph.id == _graphId) : null;
        IsAvailable = graph != null;
        if (graph == null)
        {
            ClearParameters(); Scene = null; IsEmpty = false;
            this.RaiseAndSetIfChanged(ref _selectedKey, null, nameof(SelectedKey)); UpdateSelection();
            Diagnostic = L.S(ReferenceEquals(DocManager.Inst.Project, _project) ? "GraphViewer.GraphMissing" : "ExpressionGraph.StaleProject");
            Status = Diagnostic; _source = null; UpdateSearch(); return;
        }
        Title = RendererGraphOption.Name(graph);
        Usage = ExpressionGraphLibraryInfo.Usage(_project, graph);
        if (!ReferenceEquals(graph, _source) || languageChanged)
        {
            _source = graph;
            Scene = new ExpressionGraphScene(graph);
            IsEmpty = Scene.Nodes.Count == 0;
            Diagnostic = Scene.Diagnostic;
            UpdateSelection(); UpdateSearch();
        }
        else SynchronizeParameters();
        Status = $"{graph.renderer ?? L.S("ExpressionGraph.NoRenderer")} · {L.S("GraphEdit.Parameters")}";
    }

    private void UpdateSelection()
    {
        SelectedNode = _selectedKey != null && Scene?.ByKey.TryGetValue(_selectedKey, out GraphVisualNode? node) == true ? node : null;
        if (SelectedNode == null && _selectedKey != null)
            this.RaiseAndSetIfChanged(ref _selectedKey, null, nameof(SelectedKey));
        this.RaisePropertyChanged(nameof(HasSelection)); this.RaisePropertyChanged(nameof(ShowInspector));
        SynchronizeParameters();
        if (SelectedNode == null || Scene == null) { Details = []; return; }
        GraphVisualNode selected = SelectedNode;
        List<GraphNodeDetail> details = [];
        HashSet<string> parameters = [];
        foreach (GraphNodeParameter parameter in GraphNodeTypes.ParametersOf(selected.Data.type))
        {
            parameters.Add(parameter.Name);
            if (Parameters.Count == 0) details.Add(new(parameter.Name, selected.Data.GetString(parameter.Name) ?? parameter.Default ?? "—"));
        }
        foreach (GraphParameterEdit field in Parameters) parameters.Add(field.Name);
        foreach (KeyValuePair<string, string> parameter in selected.Data.parameters)
            if (!parameters.Contains(parameter.Key)) details.Add(new(parameter.Key, parameter.Value));
        if (selected.Type == null) details.Insert(0, new(L.S("GraphViewer.UnknownNode"), selected.Data.type ?? "?"));
        Dictionary<string, GraphVisualLink[]> incoming = Scene.Incoming[selected.Key].GroupBy(link => link.ToPort).ToDictionary(group => group.Key, group => group.ToArray());
        for (int i = 0; i < selected.Inputs.Length; i++)
        {
            string port = selected.Inputs[i];
            string fallback = selected.Data.GetString(port) ?? selected.Type!.PortDefaults[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
            string label = $"{L.S("GraphViewer.Input")} · {ExpressionGraphScene.Humanize(port)}";
            if (incoming.TryGetValue(port, out GraphVisualLink[]? links))
                foreach (GraphVisualLink link in links)
                    details.Add(new(label, $"{link.From.Title} · {ExpressionGraphScene.Humanize(link.FromPort)}"
                        + (Parameters.Any(field => field.Name == port) ? string.Empty : $"\n{L.S("GraphViewer.Fallback")}: {fallback}"), link.From.Key));
            else if (!Parameters.Any(field => field.Name == port)) details.Add(new(label, $"{L.S("GraphViewer.Fallback")}: {fallback}"));
        }
        foreach (string port in selected.Outputs)
        {
            GraphVisualLink[] links = Scene.Outgoing[selected.Key].Where(link => link.FromPort == port).ToArray();
            string label = $"{L.S("GraphViewer.Output")} · {ExpressionGraphScene.Humanize(port)}";
            if (links.Length == 0) details.Add(new(label, L.S("GraphViewer.Unconnected")));
            foreach (GraphVisualLink link in links)
                details.Add(new(label, $"{link.To.Title} · {ExpressionGraphScene.Humanize(link.ToPort)}", link.To.Key));
        }
        Details = details;
    }

    private bool ContextIsCurrent() => !_disposed && ReferenceEquals(DocManager.Inst.Project, _project);

    private UExpressionGraph? CurrentGraph() => ContextIsCurrent()
        ? _project.expressionGraphs?.FirstOrDefault(graph => graph.id == _graphId) : null;

    private UGraphNode? CurrentNode(int id)
    {
        UGraphNode[] nodes = (CurrentGraph()?.nodes ?? []).Where(node => node != null && node.id == id).ToArray();
        return nodes.Length == 1 && nodes[0].parameters != null ? nodes[0] : null;
    }

    private IReadOnlyList<GraphParameterChoice> ChoicesFor(GraphNodeParameter parameter, UGraphNode node) =>
        parameter.Kind == GraphParameterKind.Expression
            ? GraphNodeTypes.ExpressionChoices(_project, node.type, CurrentGraph()?.renderer)
                .Select(expression => new GraphParameterChoice(expression.abbr, $"{expression.name} ({expression.abbr})")).ToArray()
            : parameter.Options.Select(option => new GraphParameterChoice(option, ExpressionGraphScene.Humanize(option))).ToArray();

    /// <summary>同节点刷新复用字段对象，避免重建控件丢失焦点和未提交文本。</summary>
    private void SynchronizeParameters()
    {
        GraphVisualNode? selected = SelectedNode;
        UGraphNode? node = selected == null ? null : CurrentNode(selected.Data.id);
        if (selected == null || node == null) { ClearParameters(); return; }
        if (selected.Type == null)
        {
            if (_parameterNodeId == node.id && Parameters.Any(field => field.IsDirty))
                foreach (GraphParameterEdit field in Parameters) field.Synchronize(node, string.Empty, []);
            else ClearParameters();
            return;
        }
        if (_parameterNodeId != node.id || _parameterNodeType != node.type && !Parameters.Any(field => field.IsDirty))
        {
            ClearParameters(); _parameterNodeId = node.id; _parameterNodeType = node.type;
            List<GraphParameterEdit> fields = [];
            foreach (GraphNodeParameter parameter in GraphNodeTypes.ParametersOf(node.type))
                fields.Add(new(this, node, parameter, ExpressionGraphScene.Humanize(parameter.Name), string.Empty, ChoicesFor(parameter, node)));
            for (int i = 0; i < selected.Inputs.Length; i++)
            {
                string port = selected.Inputs[i];
                if (fields.Any(field => field.Name == port)) continue;
                GraphNodeParameter fallback = new(port, GraphParameterKind.Number,
                    selected.Type.PortDefaults[i].ToString(System.Globalization.CultureInfo.InvariantCulture));
                fields.Add(new(this, node, fallback, $"{L.S("GraphViewer.Input")} · {ExpressionGraphScene.Humanize(port)}", string.Empty, []));
            }
            Parameters = fields;
        }
        foreach (GraphParameterEdit field in Parameters)
        {
            bool isInput = selected.Inputs.Contains(field.Name);
            bool connected = isInput && Scene!.Incoming[selected.Key].Any(link => link.ToPort == field.Name);
            string hint = connected ? L.S("GraphEdit.OverriddenFallback") : isInput ? L.S("GraphViewer.Fallback")
                : field.Definition.Kind == GraphParameterKind.Number && field.Definition.Default == null ? L.S("GraphEdit.OptionalNumber") : string.Empty;
            string label = isInput ? $"{L.S("GraphViewer.Input")} · {ExpressionGraphScene.Humanize(field.Name)}" : ExpressionGraphScene.Humanize(field.Name);
            field.Synchronize(node, hint, ChoicesFor(field.Definition, node), label);
        }
    }

    private void ClearParameters()
    {
        foreach (GraphParameterEdit field in Parameters) { field.ApplyCommand.Dispose(); field.CancelCommand.Dispose(); }
        Parameters = []; _parameterNodeId = null; _parameterNodeType = null;
    }

    /// <summary>在当前工程最新副本上提交字段；全部校验成功才创建一条撤销命令。</summary>
    private bool CommitParameters(IReadOnlyList<GraphParameterEdit> fields)
    {
        if (!ContextIsCurrent() || DocManager.Inst.HasOpenUndoGroup || _parameterNodeId == null) return fields.Count == 0;
        UGraphNode? node = CurrentNode(_parameterNodeId.Value);
        if (node == null) { Refresh(); return false; }
        List<(GraphParameterEdit Field, string? Value)> changes = [];
        bool valid = true;
        foreach (GraphParameterEdit field in fields)
        {
            if (!Parameters.Contains(field)) return false;
            if (!field.TryPrepare(node, ChoicesFor(field.Definition, node), out string? value)) valid = false;
            else if (field.IsDirty && field.ChangesValue(node, value)) changes.Add((field, value));
        }
        if (!valid)
        {
            IsSearchOpen = false; IsDetailsExpanded = true;
            RequestRevealParameter?.Invoke(fields.First(field => field.HasError));
            return false;
        }
        if (changes.Count > 0)
        {
            int id = node.id;
            ExpressionGraphEdits.Apply(_project, draft =>
            {
                UGraphNode target = draft.Find(_graphId)!.nodes.Single(node => node.id == id);
                foreach ((GraphParameterEdit field, string? value) in changes)
                {
                    if (value == null) target.parameters.Remove(field.Name);
                    else target.Set(field.Name, value);
                }
            });
            UGraphNode updated = CurrentNode(id)!;
            foreach (GraphParameterEdit field in Parameters)
                if (!fields.Contains(field)) field.RebaseOwnChange(updated);
            foreach (GraphParameterEdit field in fields) field.Reload(updated);
        }
        else foreach (GraphParameterEdit field in fields) field.Reload(node);
        Refresh(); return true;
    }

    /// <summary>单字段明确确认，不提交其他尚在输入的字段。</summary>
    public bool ApplyParameter(GraphParameterEdit field) => CommitParameters([field]);

    /// <summary>取消字段从最新工程重载；不把过期快照写回。</summary>
    public void ReloadParameter(GraphParameterEdit field)
    {
        if (!Parameters.Contains(field)) return;
        if (CurrentNode(field.NodeId) is { } node) field.Reload(node);
        SynchronizeParameters();
    }

    /// <summary>离开当前字段区域前原子确认有效草稿，无效或冲突草稿阻止离开。</summary>
    public bool ResolveParameterEdits()
    {
        if (_disposed) return false;
        CancelParameterPreviews();
        GraphParameterEdit[] dirty = Parameters.Where(field => field.IsDirty).ToArray();
        return dirty.Length == 0 || CommitParameters(dirty);
    }

    /// <summary>打断只取消滑块的手势预览，数字和文本草稿继续保留。</summary>
    public void CancelParameterPreviews()
    {
        foreach (GraphParameterEdit field in Parameters.Where(field => field.IsSliding).ToArray()) field.EndSlider(true);
    }

    private void UpdateUndoState()
    {
        CanUndo = ContextIsCurrent() && !DocManager.Inst.HasOpenUndoGroup && DocManager.Inst.GetUndoState(out _);
        CanRedo = ContextIsCurrent() && !DocManager.Inst.HasOpenUndoGroup && DocManager.Inst.GetRedoState(out _);
    }

    private void UpdateSearch()
    {
        if (_disposed) return;
        SearchResults = Scene?.Nodes.Where(node => string.IsNullOrWhiteSpace(Query)
                || node.SearchText.Contains(Query.Trim(), StringComparison.OrdinalIgnoreCase)).Take(50)
            .Select(node => new GraphSearchResult(node.Key, node.Title, node.Summary)).ToArray() ?? [];
    }

    private async Task ShowSettingsAsync()
    {
        if (_disposed || !IsAvailable || IsBusy || _source == null) return;
        if (!ResolveParameterEdits()) return;
        IsBusy = true;
        try
        {
            using ExpressionGraphSettingsViewModel vm = new(_project, _source);
            await PopupService.Show<bool>(new ExpressionGraphSettingsPopup(), vm);
        }
        finally { IsBusy = false; Refresh(); }
    }

    private Task ShowDiagnosticAsync() => Diagnostic.Length == 0 || !ResolveParameterEdits() ? Task.CompletedTask
        : OptionConfirmPopupService.ShowAsync(L.S("GraphViewer.Diagnostic"), Diagnostic + "\n\n" + Usage,
            new OptionConfirmOption[] { new(L.S("Common.Close"), "close") });

    /// <summary>返回先收起搜索和详情，再清除选择，最后离开页面。</summary>
    public override void OnBackRequested()
    {
        if (!ResolveParameterEdits()) return;
        if (IsSearchOpen) { IsSearchOpen = false; return; }
        if (IsDetailsExpanded) { IsDetailsExpanded = false; return; }
        if (HasSelection) { SelectedKey = null; return; }
        Navigator.NavigateBack(this);
    }

    /// <summary>合并文档更新；播放进度和纯视口操作不触发重新编译。</summary>
    public void OnNext(UCommand cmd, bool isUndo)
    {
        if (cmd is LoadProjectNotification && !ReferenceEquals(DocManager.Inst.Project, _project)) { Refresh(); return; }
        UpdateUndoState();
        if (_disposed || _pending || cmd is not (SetExpressionGraphsCommand or ConfigureExpressionsCommand or LoadProjectNotification or TrackCommand)) return;
        _pending = true;
        Dispatcher.UIThread.Post(() => { _pending = false; Refresh(); });
    }

    /// <summary>结束文档和本地化订阅，释放导航回调。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ClearParameters();
        DocManager.Inst.RemoveSubscriber(this); _subscriptions.Dispose();
        RequestLocate = null; RequestRevealParameter = null;
    }
}
