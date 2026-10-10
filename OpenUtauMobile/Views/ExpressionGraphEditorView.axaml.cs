using System;
using System.Linq;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Services.Dialogs;
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
    private Window? _window;

    /// <summary>组合标准页面控件与功能局部画布、详情区域。</summary>
    public ExpressionGraphEditorView()
    {
        InitializeComponent();
        // 页内工具栏、详情和键盘操作也优先于尚未结束的视口运动，不清除识别器中的触点。
        AddHandler(PointerPressedEvent, (_, _) => GraphCanvas.InterruptMotion(), RoutingStrategies.Tunnel, true);
        AddHandler(KeyDownEvent, (_, _) => GraphCanvas.InterruptMotion(), RoutingStrategies.Tunnel, true);
        GraphCanvas.NodeSelected += key => { if (_vm != null) _vm.SelectedKey = key; };
        SizeChanged += (_, _) => QueueInspectorLayout();
        WorkArea.SizeChanged += (_, _) => QueueInspectorLayout();
        KeyDown += (_, e) =>
        {
            if (_vm != null && e.KeyModifiers.HasFlag(KeyModifiers.Control)
                && e.Source is Control source && source is not TextBox && !source.GetVisualAncestors().OfType<TextBox>().Any())
            {
                if (e.Key == Key.Z)
                {
                    if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _vm.RedoCommand.Execute().Subscribe();
                    else _vm.UndoCommand.Execute().Subscribe();
                    e.Handled = true;
                }
                if (e.Key == Key.Y) { _vm.RedoCommand.Execute().Subscribe(); e.Handled = true; }
            }
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
        _subscriptions = new(); _vm.RequestLocate += Locate; _vm.RequestRevealParameter += RevealParameter;
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window != null) _window.Deactivated += OnDeactivated;
        PopupService.Opening += CancelParameterPreviews;
        _vm.WhenAnyValue(vm => vm.HasSelection, vm => vm.IsDetailsExpanded, vm => vm.IsSearchOpen)
            .Subscribe(_ => QueueInspectorLayout()).DisposeWith(_subscriptions);
        QueueInspectorLayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelParameterPreviews();
        PopupService.Opening -= CancelParameterPreviews;
        if (_window != null) _window.Deactivated -= OnDeactivated;
        _window = null;
        if (_vm != null) { _vm.RequestLocate -= Locate; _vm.RequestRevealParameter -= RevealParameter; }
        _subscriptions?.Dispose(); _subscriptions = null; _vm = null;
        GraphCanvas.CancelPointers();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == IsVisibleProperty || change.Property == IsEffectivelyEnabledProperty) && change.NewValue is false)
            CancelParameterPreviews();
    }

    private void CancelParameterPreviews() => _vm?.CancelParameterPreviews();
    private void OnDeactivated(object? sender, EventArgs e) => CancelParameterPreviews();

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
            // 短窗口仍需容纳固定标题和可滚动输入，同时至少保留一段画布。
            Inspector.Height = wide ? double.NaN : _vm.IsDetailsExpanded || _vm.IsSearchOpen
                ? Math.Min(280, Math.Min(Math.Max(156, WorkArea.Bounds.Height * .48), Math.Max(0, WorkArea.Bounds.Height - 64)))
                : Math.Min(100, WorkArea.Bounds.Height * .35);
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

    private void RevealParameter(GraphParameterEdit field) => Dispatcher.UIThread.Post(() =>
    {
        if (_vm?.Parameters.Contains(field) != true) return;
        WorkArea.UpdateLayout();
        this.GetVisualDescendants().OfType<GraphParameterEditor>().FirstOrDefault(editor => ReferenceEquals(editor.DataContext, field))?.Reveal();
    }, DispatcherPriority.Loaded);

    private void FitClicked(object? sender, RoutedEventArgs e) { if (_vm?.ResolveParameterEdits() == true) GraphCanvas.Fit(); }
    private void LocateClicked(object? sender, RoutedEventArgs e) { if (_vm?.ResolveParameterEdits() == true && _vm.SelectedKey is { } key) GraphCanvas.Locate(key); }
    private void ZoomInClicked(object? sender, RoutedEventArgs e) { if (_vm?.ResolveParameterEdits() == true) GraphCanvas.Zoom(1.25, new Point(GraphCanvas.Bounds.Width / 2, GraphCanvas.Bounds.Height / 2)); }
    private void ZoomOutClicked(object? sender, RoutedEventArgs e) { if (_vm?.ResolveParameterEdits() == true) GraphCanvas.Zoom(.8, new Point(GraphCanvas.Bounds.Width / 2, GraphCanvas.Bounds.Height / 2)); }
}
