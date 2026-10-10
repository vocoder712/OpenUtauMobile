using System;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenUtauMobile.ViewModels;
using ReactiveUI;

namespace OpenUtauMobile.Views;

/// <summary>复用同一详情内容，窄屏占用底部空间，宽屏移至右侧。</summary>
public partial class ExpressionGraphEditorView : UserControl
{
    private ExpressionGraphEditorViewModel? _vm;
    private CompositeDisposable? _subscriptions;
    private bool _layoutPending;
    private string? _layoutSelection;
    private bool _layoutDetails;
    private bool _layoutSearch;

    /// <summary>组合标准页面控件与功能局部画布、详情区域。</summary>
    public ExpressionGraphEditorView()
    {
        InitializeComponent();
        GraphCanvas.NodeSelected += key => { if (_vm != null) _vm.SelectedKey = key; };
        SizeChanged += (_, _) => QueueInspectorLayout();
        WorkArea.SizeChanged += (_, _) => QueueInspectorLayout();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _vm != null)
            {
                GraphCanvas.CancelPointers(); _vm.OnBackRequested(); e.Handled = true;
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _vm = DataContext as ExpressionGraphEditorViewModel;
        if (_vm == null) return;
        _subscriptions = new(); _vm.RequestLocate += Locate;
        _vm.WhenAnyValue(vm => vm.HasSelection, vm => vm.IsDetailsExpanded, vm => vm.IsSearchOpen)
            .Subscribe(_ => QueueInspectorLayout()).DisposeWith(_subscriptions);
        QueueInspectorLayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_vm != null) _vm.RequestLocate -= Locate;
        _subscriptions?.Dispose(); _subscriptions = null; _vm = null;
        GraphCanvas.CancelPointers();
        base.OnDetachedFromVisualTree(e);
    }

    private void QueueInspectorLayout()
    {
        if (_layoutPending) return;
        _layoutPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _layoutPending = false;
            if (_vm == null) return;
            bool reveal = _layoutSelection != _vm.SelectedKey || _layoutDetails != _vm.IsDetailsExpanded || _layoutSearch != _vm.IsSearchOpen;
            _layoutSelection = _vm.SelectedKey; _layoutDetails = _vm.IsDetailsExpanded; _layoutSearch = _vm.IsSearchOpen;
            bool wide = Bounds.Width >= 840;
            Grid.SetRow(Inspector, wide ? 0 : 1); Grid.SetColumn(Inspector, wide ? 1 : 0);
            Inspector.Width = wide ? 320 : double.NaN;
            Inspector.Height = wide ? double.NaN : _vm.IsDetailsExpanded || _vm.IsSearchOpen
                ? Math.Min(280, Math.Max(96, WorkArea.Bounds.Height * .48)) : Math.Min(100, WorkArea.Bounds.Height * .35);
            if (reveal && _vm.SelectedKey is { } key)
                Dispatcher.UIThread.Post(() =>
                {
                    if (_vm?.SelectedKey != key) return;
                    WorkArea.UpdateLayout(); GraphCanvas.Locate(key, true);
                }, DispatcherPriority.Loaded);
            if (_vm.IsSearchOpen && !SearchBox.IsFocused) SearchBox.Focus();
        });
    }

    private void Locate(string key) => Dispatcher.UIThread.Post(() =>
    {
        if (_vm?.SelectedKey == key) { WorkArea.UpdateLayout(); GraphCanvas.Locate(key); }
    }, DispatcherPriority.Loaded);

    private void FitClicked(object? sender, RoutedEventArgs e) => GraphCanvas.Fit();
    private void LocateClicked(object? sender, RoutedEventArgs e) { if (_vm?.SelectedKey is { } key) GraphCanvas.Locate(key); }
    private void ZoomInClicked(object? sender, RoutedEventArgs e) => GraphCanvas.Zoom(1.25, new Point(GraphCanvas.Bounds.Width / 2, GraphCanvas.Bounds.Height / 2));
    private void ZoomOutClicked(object? sender, RoutedEventArgs e) => GraphCanvas.Zoom(.8, new Point(GraphCanvas.Bounds.Width / 2, GraphCanvas.Bounds.Height / 2));
}
