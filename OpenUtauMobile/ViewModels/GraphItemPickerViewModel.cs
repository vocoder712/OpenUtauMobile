using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using OpenUtauMobile.Helpers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>选择器中的稳定键和显示摘要；筛选不更改候选身份。</summary>
public sealed class GraphPickerItem : ReactiveObject
{
    public string Key { get; }
    public string Title { get; }
    public string Summary { get; }
    [Reactive] public bool IsSelected { get; set; }

    /// <summary>候选只保存标识与显示文本，不引用可写工程对象。</summary>
    public GraphPickerItem(string key, string title, string summary)
    {
        Key = key; Title = title; Summary = summary;
    }
}

/// <summary>节点类型和来源输出共用的搜索选择草稿；确认校验失败时留在弹窗。</summary>
public sealed class GraphItemPickerViewModel : PopupViewModelBase, IDisposable
{
    private readonly IReadOnlyList<GraphPickerItem> _items;
    private readonly Func<GraphPickerItem, string?> _confirm;
    private readonly CompositeDisposable _subscriptions = [];
    private GraphPickerItem? _selected;
    public string Title { get; }
    public string Message { get; }
    [Reactive] public string Query { get; set; } = string.Empty;
    [Reactive] public IReadOnlyList<GraphPickerItem> Items { get; private set; } = [];
    [Reactive] public string Error { get; private set; } = string.Empty;
    [Reactive] public string CountHint { get; private set; } = string.Empty;
    [Reactive] public bool CanConfirm { get; private set; }
    public ReactiveCommand<GraphPickerItem, Unit> SelectCommand { get; }
    public ReactiveCommand<Unit, Unit> ConfirmCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    /// <summary>确认回调在 UI 线程重新核对当前工程，成功才关闭弹窗。</summary>
    public GraphItemPickerViewModel(string title, string message, IReadOnlyList<GraphPickerItem> items,
        Func<GraphPickerItem, string?> confirm, string? selectedKey = null)
    {
        Title = title; Message = message; _items = items; _confirm = confirm;
        _selected = items.FirstOrDefault(item => item.Key == selectedKey);
        if (_selected != null) _selected.IsSelected = true;
        CanConfirm = _selected != null;
        SelectCommand = ReactiveCommand.Create<GraphPickerItem>(item =>
        {
            if (!Items.Contains(item)) return;
            if (_selected != null) _selected.IsSelected = false;
            _selected = item; item.IsSelected = true; CanConfirm = true; Error = string.Empty;
        }).DisposeWith(_subscriptions);
        ConfirmCommand = ReactiveCommand.Create(() =>
        {
            if (_selected == null || !CanConfirm) return;
            Error = _confirm(_selected) ?? string.Empty;
            if (Error.Length == 0) RaiseClose(true);
        }).DisposeWith(_subscriptions);
        CancelCommand = ReactiveCommand.Create(RequestBack).DisposeWith(_subscriptions);
        this.WhenAnyValue(vm => vm.Query).Subscribe(_ => Filter()).DisposeWith(_subscriptions);
    }

    /// <summary>仅搜索时扫描一次候选，限制控件数量；所有候选仍可通过精确搜索访问。</summary>
    private void Filter()
    {
        string query = Query.Trim();
        GraphPickerItem[] matches = _items.Where(item => query.Length == 0
            || (item.Title + " " + item.Summary + " " + item.Key).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        Items = matches.Take(100).ToArray();
        CanConfirm = _selected != null && Items.Contains(_selected);
        CountHint = string.Format(L.S(matches.Length > 100 ? "GraphTopology.MoreResults" : "GraphTopology.Results"), matches.Length);
    }

    /// <summary>关闭后释放搜索和命令订阅，不保留工程操作回调。</summary>
    public void Dispose() => _subscriptions.Dispose();
}
