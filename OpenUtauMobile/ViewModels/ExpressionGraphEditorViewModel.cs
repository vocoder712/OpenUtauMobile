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

/// <summary>单图只读查看会话；仅图设置提交命令，所有画布动作只影响本地状态。</summary>
public sealed class ExpressionGraphEditorViewModel : NavigateViewModelBase, ICmdSubscriber, IDisposable
{
    private readonly UProject _project;
    private readonly string _graphId;
    private readonly CompositeDisposable _subscriptions = [];
    private UExpressionGraph? _source;
    private bool _disposed;
    private bool _pending;
    private string? _selectedKey;

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
    public bool HasSelection => SelectedNode != null;
    public bool ShowInspector => HasSelection || IsSearchOpen;
    public event Action<string>? RequestLocate;

    public string? SelectedKey
    {
        get => _selectedKey;
        set
        {
            if (_disposed || value == _selectedKey) return;
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

    /// <summary>通过工程身份与图 ID 创建只读会话，不持有可写节点引用。</summary>
    public ExpressionGraphEditorViewModel(MainViewModel navigator, UProject project, string graphId) : base(navigator)
    {
        _project = project; _graphId = graphId;
        BackCommand = ReactiveCommand.Create(OnBackRequested).DisposeWith(_subscriptions);
        SearchCommand = ReactiveCommand.Create(() => { IsSearchOpen = !IsSearchOpen; }).DisposeWith(_subscriptions);
        CloseInspectorCommand = ReactiveCommand.Create(() => { IsSearchOpen = false; IsDetailsExpanded = false; SelectedKey = null; }).DisposeWith(_subscriptions);
        ToggleDetailsCommand = ReactiveCommand.Create(() => { IsDetailsExpanded = !IsDetailsExpanded; }).DisposeWith(_subscriptions);
        LocateCommand = ReactiveCommand.Create<string>(key =>
        {
            if (IsAvailable && Scene?.ByKey.ContainsKey(key) == true)
            {
                SelectedKey = key; IsSearchOpen = false; RequestLocate?.Invoke(key);
            }
        }).DisposeWith(_subscriptions);
        SettingsCommand = ReactiveCommand.CreateFromTask(ShowSettingsAsync).DisposeWith(_subscriptions);
        DiagnosticCommand = ReactiveCommand.CreateFromTask(ShowDiagnosticAsync).DisposeWith(_subscriptions);
        this.WhenAnyValue(vm => vm.Query).Subscribe(_ => UpdateSearch()).DisposeWith(_subscriptions);
        this.WhenAnyValue(vm => vm.IsSearchOpen).Subscribe(_ => this.RaisePropertyChanged(nameof(ShowInspector))).DisposeWith(_subscriptions);
        if (Application.Current != null)
            Application.Current.GetResourceObservable("GraphViewer.ReadOnly")
                .Subscribe(_ => Dispatcher.UIThread.Post(() => Refresh(true))).DisposeWith(_subscriptions);
        DocManager.Inst.AddSubscriber(this);
        Refresh();
    }

    private void Refresh(bool languageChanged = false)
    {
        if (_disposed) return;
        UExpressionGraph? graph = ReferenceEquals(DocManager.Inst.Project, _project)
            ? _project.expressionGraphs?.FirstOrDefault(graph => graph.id == _graphId) : null;
        IsAvailable = graph != null;
        if (graph == null)
        {
            Scene = null; IsEmpty = false; SelectedKey = null;
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
        Status = $"{graph.renderer ?? L.S("ExpressionGraph.NoRenderer")} · {L.S("GraphViewer.ReadOnly")}";
    }

    private void UpdateSelection()
    {
        SelectedNode = _selectedKey != null && Scene?.ByKey.TryGetValue(_selectedKey, out GraphVisualNode? node) == true ? node : null;
        if (SelectedNode == null && _selectedKey != null)
            this.RaiseAndSetIfChanged(ref _selectedKey, null, nameof(SelectedKey));
        this.RaisePropertyChanged(nameof(HasSelection)); this.RaisePropertyChanged(nameof(ShowInspector));
        if (SelectedNode == null || Scene == null) { Details = []; return; }
        GraphVisualNode selected = SelectedNode;
        List<GraphNodeDetail> details = [];
        HashSet<string> parameters = [];
        foreach (GraphNodeParameter parameter in GraphNodeTypes.ParametersOf(selected.Data.type))
        {
            parameters.Add(parameter.Name);
            details.Add(new(parameter.Name, selected.Data.GetString(parameter.Name) ?? parameter.Default ?? "—"));
        }
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
                    details.Add(new(label, $"{link.From.Title} · {ExpressionGraphScene.Humanize(link.FromPort)}\n{L.S("GraphViewer.Fallback")}: {fallback}", link.From.Key));
            else details.Add(new(label, $"{L.S("GraphViewer.Fallback")}: {fallback}"));
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
        IsBusy = true;
        try
        {
            using ExpressionGraphSettingsViewModel vm = new(_project, _source);
            await PopupService.Show<bool>(new ExpressionGraphSettingsPopup(), vm);
        }
        finally { IsBusy = false; Refresh(); }
    }

    private Task ShowDiagnosticAsync() => Diagnostic.Length == 0 ? Task.CompletedTask
        : OptionConfirmPopupService.ShowAsync(L.S("GraphViewer.Diagnostic"), Diagnostic + "\n\n" + Usage,
            new OptionConfirmOption[] { new(L.S("Common.Close"), "close") });

    /// <summary>返回先收起搜索和详情，再清除选择，最后离开页面。</summary>
    public override void OnBackRequested()
    {
        if (IsSearchOpen) { IsSearchOpen = false; return; }
        if (IsDetailsExpanded) { IsDetailsExpanded = false; return; }
        if (HasSelection) { SelectedKey = null; return; }
        Navigator.NavigateBack(this);
    }

    /// <summary>合并文档更新；播放进度和纯视口操作不触发重新编译。</summary>
    public void OnNext(UCommand cmd, bool isUndo)
    {
        if (_disposed || _pending || cmd is not (SetExpressionGraphsCommand or ConfigureExpressionsCommand or LoadProjectNotification or TrackCommand)) return;
        _pending = true;
        Dispatcher.UIThread.Post(() => { _pending = false; Refresh(); });
    }

    /// <summary>结束文档和本地化订阅，释放导航回调。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DocManager.Inst.RemoveSubscriber(this); _subscriptions.Dispose();
        RequestLocate = null;
    }
}
