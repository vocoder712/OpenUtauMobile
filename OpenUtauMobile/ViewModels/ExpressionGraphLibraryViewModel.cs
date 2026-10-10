using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtau.Core;
using OpenUtau.Core.ExpressionGraph;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Storage;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

public sealed record ExpressionGraphFilter(string? Slot, string Label)
{
    public override string ToString() => Label;
}

/// <summary>列表保存稳定 ID 和显示信息，不持有可写的工程节点。</summary>
public sealed record ExpressionGraphLibraryItem(string Id, string Name, string Renderer,
    bool IsDefault, string References, string Status, bool HasError, string Usage, string DefaultLabel);

/// <summary>工程图库；实际变更统一走 Core 的图命令。</summary>
public sealed class ExpressionGraphLibraryViewModel : NavigateViewModelBase, ICmdSubscriber, IDisposable
{
    private readonly UProject _project;
    private readonly CompositeDisposable _subscriptions = new();
    private bool _disposed;
    private bool _refreshing;
    private ExpressionGraphFilter? _filter;
    private string? _selectedId;

    public ObservableCollection<ExpressionGraphLibraryItem> Graphs { get; } = [];
    [Reactive] public IReadOnlyList<ExpressionGraphFilter> Filters { get; private set; } = [];
    [Reactive] public IReadOnlyList<FabMenuAction> CreationActions { get; private set; } = [];
    [Reactive] public bool IsCreationMenuOpen { get; set; }
    [Reactive] public bool CanUndo { get; private set; }
    [Reactive] public bool CanRedo { get; private set; }
    [Reactive] public bool IsCurrentProject { get; private set; }
    [Reactive] public bool IsBusy { get; private set; }
    [Reactive] public string ReferenceWarnings { get; private set; } = string.Empty;
    [Reactive] public string EmptyMessage { get; private set; } = string.Empty;
    [Reactive] public string Error { get; private set; } = string.Empty;
    public bool IsEmpty => Graphs.Count == 0;
    public string ReferenceWarningSummary => string.Format(L.S("ExpressionGraph.ReferenceWarnings"), ReferenceWarnings.Split('\n').Length);
    public event Action<string>? RequestRevealGraph;

    public ExpressionGraphFilter? SelectedFilter
    {
        get => _filter;
        set
        {
            // 重建选项时控件可能临时回写 null；“全部”是明确的选项，不用 null 表示。
            if (_refreshing || _disposed || value == null) return;
            ExpressionGraphFilter? selected = Filters.FirstOrDefault(filter => filter.Slot == value.Slot);
            if (selected == null || ReferenceEquals(_filter, selected)) return;
            this.RaiseAndSetIfChanged(ref _filter, selected);
            Refresh();
        }
    }

    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> UndoCommand { get; }
    public ReactiveCommand<Unit, Unit> RedoCommand { get; }
    public ReactiveCommand<ExpressionGraphLibraryItem, Unit> SettingsCommand { get; }
    public ReactiveCommand<ExpressionGraphLibraryItem, Unit> OpenGraphCommand { get; }
    public ReactiveCommand<ExpressionGraphLibraryItem, Unit> DuplicateCommand { get; }
    public ReactiveCommand<ExpressionGraphLibraryItem, Unit> DeleteCommand { get; }
    public ReactiveCommand<string, Unit> CreateCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowWarningsCommand { get; }
    public ReactiveCommand<Unit, Unit> ImportCommand { get; }
    public ReactiveCommand<ExpressionGraphLibraryItem, Unit> ExportCommand { get; }

    public ExpressionGraphLibraryViewModel(MainViewModel navigator, UProject project)
        : base(navigator)
    {
        _project = project;
        BackCommand = ReactiveCommand.Create(OnBackRequested).DisposeWith(_subscriptions);
        UndoCommand = ReactiveCommand.Create(() => { if (CanUndo && CanEdit()) DocManager.Inst.Undo(); })
            .DisposeWith(_subscriptions);
        RedoCommand = ReactiveCommand.Create(() => { if (CanRedo && CanEdit()) DocManager.Inst.Redo(); })
            .DisposeWith(_subscriptions);
        SettingsCommand = ReactiveCommand.CreateFromTask<ExpressionGraphLibraryItem>(ShowSettingsAsync).DisposeWith(_subscriptions);
        OpenGraphCommand = ReactiveCommand.Create<ExpressionGraphLibraryItem>(item =>
        {
            if (!CanEdit() || Find(item.Id) == null) return;
            IsCreationMenuOpen = false; _selectedId = item.Id;
            Navigator.Navigate(new ExpressionGraphEditorViewModel(Navigator, _project, item.Id));
        }).DisposeWith(_subscriptions);
        DuplicateCommand = ReactiveCommand.Create<ExpressionGraphLibraryItem>(Duplicate).DisposeWith(_subscriptions);
        DeleteCommand = ReactiveCommand.CreateFromTask<ExpressionGraphLibraryItem>(DeleteAsync).DisposeWith(_subscriptions);
        CreateCommand = ReactiveCommand.CreateFromTask<string>(CreateAsync).DisposeWith(_subscriptions);
        ShowWarningsCommand = ReactiveCommand.CreateFromTask(ShowWarningsAsync).DisposeWith(_subscriptions);
        ImportCommand = ReactiveCommand.CreateFromTask(ImportAsync).DisposeWith(_subscriptions);
        ExportCommand = ReactiveCommand.CreateFromTask<ExpressionGraphLibraryItem>(ExportAsync).DisposeWith(_subscriptions);
        DocManager.Inst.AddSubscriber(this);
        if (Application.Current != null)
            Application.Current.GetResourceObservable("ExpressionGraph.LibraryTitle")
                .Subscribe(_ => Dispatcher.UIThread.Post(Refresh)).DisposeWith(_subscriptions);
        Refresh();
    }

    public override void OnNavigatedTo()
    {
        Refresh();
        if (_selectedId != null) Dispatcher.UIThread.Post(() => RequestRevealGraph?.Invoke(_selectedId));
    }

    public override void OnBackRequested()
    {
        if (IsCreationMenuOpen) { IsCreationMenuOpen = false; return; }
        Navigator.NavigateBack(this);
    }

    private bool CanEdit() => !_disposed && !IsBusy && ReferenceEquals(DocManager.Inst.Project, _project)
        && !DocManager.Inst.HasOpenUndoGroup;

    private UExpressionGraph? Find(string id) => _project.expressionGraphs?.FirstOrDefault(graph => graph.id == id);

    // 文件选择不持有工程锁；返回后同时检查工程与页面，禁止过期结果继续操作。
    private bool IsFileOperationCurrent() => !_disposed && ReferenceEquals(DocManager.Inst.Project, _project)
        && ReferenceEquals(Navigator.CurrentViewModel, this) && !DocManager.Inst.HasOpenUndoGroup;

    private async Task ImportAsync()
    {
        if (!CanEdit()) return;
        IsCreationMenuOpen = false;
        IsBusy = true;
        Error = string.Empty;
        try
        {
            string path = await FilePicker.PickSingleFileAsync(L.S("ExpressionGraph.Import"), ["*.ougraph"]);
            if (path.Length == 0 || !IsFileOperationCurrent()) return;
            ExpressionGraphFile file = await Task.Run(() => ExpressionGraphFile.Load(path));
            if (!IsFileOperationCurrent()) return;
            // 在命令开始前验证可克隆性及定义唯一性，避免损坏文件留下部分导入。
            if (file.expressions == null || file.expressions.Any(descriptor => descriptor == null)
                || file.expressions.Where(descriptor => !string.IsNullOrEmpty(descriptor.abbr))
                    .GroupBy(descriptor => descriptor.abbr).Any(group => group.Count() > 1))
                throw new InvalidDataException(L.S("ExpressionGraph.InvalidFileData"));
            _ = file.graph!.Clone();
            foreach (UExpressionDescriptor descriptor in file.expressions) _ = descriptor.Clone();
            _ = ExpressionGraphProgram.Compile(file.graph, out _);
            ExpressionGraphFile.ImportResult result = file.ImportInto(_project);
            Refresh();
            Reveal(result.GraphId);
            UExpressionGraph graph = Find(result.GraphId)!;
            string report = string.Format(L.S("ExpressionGraph.Imported"), RendererGraphOption.Name(graph));
            if (result.AddedExpressions.Count > 0)
                report += "\n\n" + string.Format(L.S("ExpressionGraph.ImportAdded"), string.Join(", ", result.AddedExpressions));
            if (result.ConflictingExpressions.Count > 0)
                report += "\n\n" + string.Format(L.S("ExpressionGraph.ImportConflicts"), string.Join(", ", result.ConflictingExpressions));
            if (ExpressionGraphProgram.Compile(graph, out string? problem) == null)
                report += "\n\n" + string.Format(L.S("ExpressionGraph.Invalid"), problem);
            await OptionConfirmPopupService.ShowAsync(L.S("ExpressionGraph.Import"), report,
                new OptionConfirmOption[] { new(L.S("Common.Close"), "close") });
        }
        catch (Exception exception)
        {
            if (IsFileOperationCurrent()) Error = string.Format(L.S("ExpressionGraph.ImportFailed"), exception.Message);
        }
        finally { IsBusy = false; Refresh(); }
    }

    private async Task ExportAsync(ExpressionGraphLibraryItem item)
    {
        if (!CanEdit() || Find(item.Id) is not { } graph) return;
        IsCreationMenuOpen = false;
        IsBusy = true;
        Error = string.Empty;
        try
        {
            // 图与表情定义一起克隆；文件选择期间的编辑不会混入导出结果。
            ExpressionGraphFile snapshot = ExpressionGraphFile.Create(_project, graph);
            string name = RendererGraphOption.Name(graph);
            foreach (char invalid in Path.GetInvalidFileNameChars()) name = name.Replace(invalid, '_');
            string path = await FilePicker.SaveFileAsync(L.S("ExpressionGraph.Export"), "ougraph", name,
                Path.GetDirectoryName(_project.FilePath) ?? string.Empty);
            if (path.Length == 0 || !IsFileOperationCurrent()) return;
            await Task.Run(() => snapshot.Save(path));
            await ServiceHub.FlushFileSystemAsync();
            if (!IsFileOperationCurrent()) return;
            await OptionConfirmPopupService.ShowAsync(L.S("ExpressionGraph.Export"),
                string.Format(L.S("ExpressionGraph.Exported"), path),
                new OptionConfirmOption[] { new(L.S("Common.Close"), "close") });
        }
        catch (Exception exception)
        {
            if (IsFileOperationCurrent()) Error = string.Format(L.S("ExpressionGraph.ExportFailed"), exception.Message);
        }
        finally { IsBusy = false; Refresh(); }
    }

    private string[] RendererFamilies()
    {
        string[] preferred = [Renderers.CLASSIC, Renderers.WORLDLINE_R, Renderers.DIFFSINGER, Renderers.ENUNU,
            Renderers.VOGEN, Renderers.VOICEVOX, Renderers.NEUTRINO];
        string[] used =
        [
            .. _project.tracks.Select(track => track.RendererSettings?.renderer).OfType<string>()
                .Select(Renderers.GetExpressionGraphSlot).Distinct()
        ];
        return
        [
            .. Enum.GetValues<USingerType>().SelectMany(Renderers.GetSupportedRenderers)
                .Concat(_project.tracks.Select(track => track.RendererSettings?.renderer).OfType<string>())
                .Select(Renderers.GetExpressionGraphSlot).Distinct(StringComparer.Ordinal)
                .OrderBy(slot => used.Contains(slot) ? 0 : 1)
                .ThenBy(slot =>
                {
                    int index = Array.IndexOf(preferred, slot);
                    return index >= 0 ? index : preferred.Length;
                })
                .ThenBy(slot => slot, StringComparer.Ordinal)
        ];
    }

    private void Refresh()
    {
        if (_disposed) return;
        IsCurrentProject = ReferenceEquals(DocManager.Inst.Project, _project);
        CanUndo = IsCurrentProject && !DocManager.Inst.HasOpenUndoGroup && DocManager.Inst.GetUndoState(out _);
        CanRedo = IsCurrentProject && !DocManager.Inst.HasOpenUndoGroup && DocManager.Inst.GetRedoState(out _);
        if (!IsCurrentProject)
        {
            IsCreationMenuOpen = false;
            Error = L.S("ExpressionGraph.StaleProject");
            return;
        }
        _refreshing = true;
        try
        {
            string? selectedSlot = _filter?.Slot;
            IReadOnlyList<string> families = RendererFamilies();
            string?[] slots = [null, .. families.Concat((_project.expressionGraphs ?? []).Select(graph => graph.renderer)
                .OfType<string>().Select(Renderers.GetExpressionGraphSlot)).Distinct()];
            ExpressionGraphFilter[] filters = slots.Select(slot =>
            {
                string label = slot ?? L.S("ExpressionGraph.AllRenderers");
                return Filters.FirstOrDefault(filter => filter.Slot == slot && filter.Label == label)
                    ?? new ExpressionGraphFilter(slot, label);
            }).ToArray();
            bool optionsChanged = !Filters.SequenceEqual(filters);
            if (optionsChanged) Filters = filters;
            ExpressionGraphFilter selected = Filters.FirstOrDefault(filter => filter.Slot == selectedSlot) ?? Filters[0];
            this.RaiseAndSetIfChanged(ref _filter, selected, nameof(SelectedFilter));
            // ItemsSource 变化可能清掉控件选择，即使选项实例没变，也必须重新推送已确认的选择。
            if (optionsChanged) this.RaisePropertyChanged(nameof(SelectedFilter));
            // 当前菜单展示最多五项；其余家族通过最后一项打开标准选择器。
            IReadOnlyList<FabMenuAction> actions = families.Take(families.Count > 5 ? 4 : 5)
                .Select(slot => new FabMenuAction(slot, slot, PackIconPhosphorIconsKind.Waveform)).ToArray();
            if (families.Count > 5)
                actions = actions.Append(new FabMenuAction("", L.S("ExpressionGraph.OtherRenderers"),
                    PackIconPhosphorIconsKind.DotsThree)).ToArray();
            if (!CreationActions.SequenceEqual(actions)) CreationActions = actions;
            List<ExpressionGraphLibraryItem> rows = [];
            foreach (UExpressionGraph graph in _project.expressionGraphs ?? [])
            {
                if (_filter?.Slot != null && (graph.renderer == null || Renderers.GetExpressionGraphSlot(graph.renderer) != _filter.Slot)) continue;
                bool valid = ExpressionGraphProgram.Compile(graph, out string? problem) != null;
                bool isDefault = graph.renderer != null && _project.defaultExpressionGraphs?
                    .GetValueOrDefault(Renderers.GetExpressionGraphSlot(graph.renderer)) == graph.id;
                UTrack[] references =
                [
                    .. _project.tracks.Where(track => track.ExpressionGraph == graph.id
                                                      || ExpressionGraphProgram.GetEffectiveGraph(_project, track)
                                                          ?.id == graph.id)
                ];
                rows.Add(new ExpressionGraphLibraryItem(graph.id, RendererGraphOption.Name(graph), graph.renderer ?? L.S("ExpressionGraph.NoRenderer"),
                    isDefault, string.Format(L.S("ExpressionGraph.ReferenceCount"), references.Length),
                    valid ? L.S("ExpressionGraph.Valid") : string.Format(L.S("ExpressionGraph.Invalid"), problem), !valid,
                    ExpressionGraphLibraryInfo.Usage(_project, graph),
                    string.Format(L.S("ExpressionGraph.DefaultBadge"), graph.renderer == null ? string.Empty : Renderers.GetExpressionGraphSlot(graph.renderer))));
            }
            // 未变化的条目保留实例，避免命令刷新重置列表滚动和正在收起的 FAB 菜单。
            for (int i = 0; i < rows.Count; i++)
            {
                if (i >= Graphs.Count) Graphs.Add(rows[i]);
                else if (Graphs[i] != rows[i]) Graphs[i] = rows[i];
            }
            while (Graphs.Count > rows.Count) Graphs.RemoveAt(Graphs.Count - 1);
            ReferenceWarnings = ExpressionGraphLibraryInfo.ReferenceWarnings(_project);
            this.RaisePropertyChanged(nameof(ReferenceWarningSummary));
            EmptyMessage = L.S(_filter?.Slot == null ? "ExpressionGraph.Empty" : "ExpressionGraph.FilterEmpty");
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
        finally { _refreshing = false; }
    }

    private async Task CreateAsync(string renderer)
    {
        if (!CanEdit()) return;
        IsCreationMenuOpen = false;
        if (renderer.Length == 0)
        {
            IsBusy = true;
            try
            {
                renderer = await PopupService.Show<string>(new ExpressionGraphRendererPickerPopup(),
                    new ExpressionGraphRendererPickerViewModel(RendererFamilies())) ?? string.Empty;
            }
            finally { IsBusy = false; }
        }
        if (renderer.Length == 0 || !CanEdit() || !RendererFamilies().Contains(renderer)) return;
        string name = string.Format(L.S("ExpressionGraph.NewName"), renderer);
        string? id = null;
        TryEdit(() => ExpressionGraphLibraryInfo.Apply(_project, draft =>
        {
            id = draft.NewId(name);
            draft.Graphs.Add(ExpressionGraphEdits.CreateDefault(id, name, renderer));
            draft.Defaults.TryAdd(Renderers.GetExpressionGraphSlot(renderer), id);
        }));
        if (id != null) Reveal(id);
    }

    private async Task ShowWarningsAsync()
    {
        if (!CanEdit() || ReferenceWarnings.Length == 0) return;
        IsBusy = true;
        try
        {
            await OptionConfirmPopupService.ShowAsync(ReferenceWarningSummary, ReferenceWarnings,
                new OptionConfirmOption[] { new(L.S("Common.Close"), "close") });
        }
        finally { IsBusy = false; }
    }

    private void Duplicate(ExpressionGraphLibraryItem item)
    {
        if (!CanEdit() || Find(item.Id) is not { } graph) return;
        string? id = null;
        TryEdit(() => ExpressionGraphLibraryInfo.Apply(_project, draft =>
        {
            UExpressionGraph copy = graph.Clone();
            copy.name = string.Format(L.S("ExpressionGraph.CopyName"), RendererGraphOption.Name(graph));
            copy.id = id = draft.NewId(copy.name);
            draft.Graphs.Add(copy);
        }));
        if (id != null) Reveal(id);
    }

    private async Task ShowSettingsAsync(ExpressionGraphLibraryItem item)
    {
        if (!CanEdit() || Find(item.Id) is not { } graph) return;
        IsBusy = true;
        _selectedId = graph.id;
        try
        {
            using ExpressionGraphSettingsViewModel vm = new(_project, graph);
            await PopupService.Show<bool>(new ExpressionGraphSettingsPopup(), vm);
        }
        finally
        {
            IsBusy = false;
            Refresh();
            if (!_disposed && IsCurrentProject && Find(item.Id) != null) Reveal(item.Id, clearFilter: false);
        }
    }

    private async Task DeleteAsync(ExpressionGraphLibraryItem item)
    {
        if (!CanEdit() || Find(item.Id) is not { } graph) return;
        string before = ExpressionGraphLibraryInfo.Fingerprint(_project);
        bool hasReferences = _project.tracks.Any(track => track.ExpressionGraph == graph.id)
            || _project.defaultExpressionGraphs?.Values.Contains(graph.id) == true;
        if (hasReferences)
        {
            IsBusy = true;
            string? result;
            try
            {
                result = await OptionConfirmPopupService.ShowAsync(L.S("ExpressionGraph.DeleteTitle"),
                    string.Format(L.S("ExpressionGraph.DeleteMessage"), RendererGraphOption.Name(graph),
                        ExpressionGraphLibraryInfo.Usage(_project, graph)),
                    new OptionConfirmOption[] { new(L.S("Common.Cancel"), "cancel", isDefault: true), new(L.S("Common.Delete"), "delete", isDestructive: true) });
            }
            finally { IsBusy = false; }
            if (result != "delete" || !CanEdit()) return;
            if (before != ExpressionGraphLibraryInfo.Fingerprint(_project))
            {
                Error = L.S("ExpressionGraph.StaleSettings");
                return;
            }
        }
        TryEdit(() => ExpressionGraphLibraryInfo.Apply(_project, draft => draft.Remove(item.Id)));
    }

    private void TryEdit(Action edit)
    {
        Error = string.Empty;
        try { edit(); }
        catch (Exception exception) { Error = exception.Message; }
        Refresh();
    }

    private void Reveal(string id, bool clearFilter = true)
    {
        _selectedId = id;
        if (clearFilter) SelectedFilter = Filters[0];
        Dispatcher.UIThread.Post(() => { if (!_disposed) RequestRevealGraph?.Invoke(id); });
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
        // 等撤销组结束后读取状态，不把播放进度通知变成列表重建。
        if (cmd is not UNotification || cmd is LoadProjectNotification or PreRenderNotification)
            Dispatcher.UIThread.Post(Refresh);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DocManager.Inst.RemoveSubscriber(this);
        _subscriptions.Dispose();
    }
}

/// <summary>引用、诊断及失效检查共用 Core 的有效图解析，不修改未知数据。</summary>
internal static class ExpressionGraphLibraryInfo
{
    public static void Apply(UProject project, Action<ExpressionGraphEdits.Draft> edit)
    {
        ExpressionGraphEdits.Draft draft = new(project);
        edit(draft);
        SetExpressionGraphsCommand command = new(project, draft.ToState());
        // 草稿和命令先构建完毕；提交异常也必须结束撤销组，避免后续操作被锁住。
        DocManager.Inst.StartUndoGroup("command.expressiongraph.edit");
        try { DocManager.Inst.ExecuteCmd(command); }
        finally { DocManager.Inst.EndUndoGroup(); }
    }

    public static string Fingerprint(UProject project) => Yaml.DefaultSerializer.Serialize(new
    {
        graphs = project.expressionGraphs,
        defaults = project.defaultExpressionGraphs,
        tracks = project.tracks.Select(track => new { track.TrackName, track.ExpressionGraph, renderer = track.RendererSettings?.renderer }).ToArray(),
    });

    public static string Usage(UProject project, UExpressionGraph graph)
    {
        List<string> lines = [];
        foreach (string slot in project.defaultExpressionGraphs?.Where(pair => pair.Value == graph.id).Select(pair => pair.Key) ?? [])
            lines.Add(string.Format(L.S("ExpressionGraph.DefaultFor"), slot));
        foreach (UTrack track in project.tracks.Where(track => track.ExpressionGraph == graph.id
                     || ExpressionGraphProgram.GetEffectiveGraph(project, track)?.id == graph.id))
            lines.Add(string.Format(L.S(track.ExpressionGraph == null ? "ExpressionGraph.FollowingTrack" : "ExpressionGraph.ExplicitTrack"),
                track.TrackName));
        return lines.Count == 0 ? L.S("ExpressionGraph.Unused") : string.Join("\n", lines);
    }

    public static string ReferenceWarnings(UProject project)
    {
        List<string> warnings = new();
        foreach (UTrack track in project.tracks)
        {
            string? id = track.ExpressionGraph;
            string? renderer = track.RendererSettings?.renderer;
            if (id == null && renderer != null) project.defaultExpressionGraphs?.TryGetValue(Renderers.GetExpressionGraphSlot(renderer), out id);
            string problem = RendererGraphOption.Problem(project, renderer, id);
            if (problem.Length > 0) warnings.Add($"{track.TrackName}: {problem}");
        }
        foreach (KeyValuePair<string, string> pair in project.defaultExpressionGraphs ?? [])
        {
            string problem = RendererGraphOption.Problem(project, pair.Key, pair.Value);
            if (problem.Length > 0) warnings.Add(string.Format(L.S("ExpressionGraph.DefaultProblem"), pair.Key, problem));
        }
        return string.Join("\n", warnings.Distinct());
    }
}
