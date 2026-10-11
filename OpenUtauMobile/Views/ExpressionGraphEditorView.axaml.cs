using System;
using System.Linq;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Media;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;
using ReactiveUI;

namespace OpenUtauMobile.Views;

/// <summary>编辑面板支持分隔拖动；画布操作集中在响应式浮动工具栏。</summary>
public partial class ExpressionGraphEditorView : UserControl
{
    private ExpressionGraphEditorViewModel? _vm;
    private CompositeDisposable? _subscriptions;
    private bool _layoutPending;
    private Window? _window;
    private double? _inspectorHeight;
    private double _inspectorWidth = 320;
    private bool _wide;
    private IPointer? _splitPointer;
    private bool _splitDown;
    private bool _splitDragging;
    private Point _splitPress;
    private double _splitExtent;
    private double _handleRatio = .5;
    private double _splitRatio;
    private ContextMenu? _overflowMenu;
    private Button[] _tools = [];
    private readonly string[] _toolLabels = ["GraphTopology.AddNode", "GraphViewer.Search", "GraphViewer.Fit", "GraphViewer.Locate", "GraphViewer.ZoomOut", "GraphViewer.ZoomIn"];

    /// <summary>组合标准页面控件与功能局部画布、属性编辑区域。</summary>
    public ExpressionGraphEditorView()
    {
        InitializeComponent();
        _tools = [AddTool, SearchTool, FitTool, LocateTool, ZoomOutTool, ZoomInTool];
        CanvasRegion.SizeChanged += (_, _) => { UpdateToolbar(); UpdateHandle(); };
        InspectorHandle.PointerPressed += SplitPressed;
        InspectorHandle.PointerMoved += SplitMoved;
        InspectorHandle.PointerReleased += SplitReleased;
        InspectorHandle.PointerCaptureLost += (_, _) => { _splitDown = false; _splitDragging = false; _splitPointer = null; };
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
        // MD3 二级高度：主阴影 30%，环境阴影 15%；只绘制容器阴影，不改变命中区。
        this.GetResourceObservable("Sem.Value.Shadow").Subscribe(value =>
        {
            if (value is Color shadow)
                CanvasToolbar.BoxShadow = new(new BoxShadow { OffsetY = 1, Blur = 2, Color = Color.FromArgb(77, shadow.R, shadow.G, shadow.B) },
                    [new BoxShadow { OffsetY = 2, Blur = 6, Spread = 2, Color = Color.FromArgb(38, shadow.R, shadow.G, shadow.B) }]);
        }).DisposeWith(_subscriptions);
        GraphCanvas.PrepareNodeMove = _vm.PrepareNodeMove;
        _vm.GetInsertionPoint = () => GraphCanvas.ToWorld(new Point(GraphCanvas.Bounds.Width / 2, GraphCanvas.Bounds.Height / 2));
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window != null) _window.Deactivated += OnDeactivated;
        PopupService.Opening += CancelParameterPreviews;
        _vm.WhenAnyValue(vm => vm.HasSelection, vm => vm.IsSearchOpen)
            .Subscribe(_ => QueueInspectorLayout()).DisposeWith(_subscriptions);
        QueueInspectorLayout();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelParameterPreviews();
        _overflowMenu?.Close();
        CancelSplit();
        PopupService.Opening -= CancelParameterPreviews;
        if (_window != null) _window.Deactivated -= OnDeactivated;
        _window = null;
        if (_vm != null) { _vm.RequestLocate -= Locate; _vm.RequestRevealParameter -= RevealParameter; _vm.GetInsertionPoint = null; GraphCanvas.PrepareNodeMove = null; }
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

    private void CancelParameterPreviews() { _vm?.CancelParameterPreviews(); CancelSplit(); _overflowMenu?.Close(); }
    private void OnDeactivated(object? sender, EventArgs e) { CancelParameterPreviews(); CancelSplit(); _overflowMenu?.Close(); }

    private void QueueInspectorLayout()
    {
        if (_layoutPending) return;
        _layoutPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _layoutPending = false;
            if (_vm == null) return;
            bool wide = Bounds.Width >= 840;
            if (_wide != wide || !_vm.ShowInspector) CancelSplit();
            _wide = wide;
            Grid.SetRow(Inspector, wide ? 0 : 1); Grid.SetColumn(Inspector, wide ? 1 : 0);
            Inspector.Width = wide ? Math.Clamp(_inspectorWidth, 240, Math.Max(240, WorkArea.Bounds.Width - 160)) : double.NaN;
            // 短窗口仍需容纳固定标题和可滚动输入，同时至少保留一段画布。
            Inspector.Height = wide ? double.NaN
                : Math.Clamp(_inspectorHeight ?? Math.Min(280, WorkArea.Bounds.Height * .48), Math.Min(156, Math.Max(0, WorkArea.Bounds.Height - 96)), Math.Max(0, WorkArea.Bounds.Height - 96));
            UpdateToolbar(); UpdateHandle();
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

    /// <summary>只在画布尺寸改变时重新分配六个操作，隐藏项进入菜单且保留原命令。</summary>
    private void UpdateToolbar()
    {
        double available = Math.Max(0, CanvasRegion.Bounds.Width - 32);
        int slots = Math.Max(1, (int)Math.Floor((available - 12) / 52));
        int visible = slots >= _tools.Length ? _tools.Length : Math.Max(0, slots - 1);
        bool changed = false;
        for (int i = 0; i < _tools.Length; i++)
        {
            bool show = i < visible;
            changed |= _tools[i].IsVisible != show;
            _tools[i].IsVisible = show;
        }
        OverflowTool.IsVisible = visible < _tools.Length;
        if (changed) _overflowMenu?.Close();
    }

    /// <summary>菜单项使用相同操作和图标库，控件禁用状态实时跟随画布按钮。</summary>
    private void OverflowClicked(object? sender, RoutedEventArgs e)
    {
        _overflowMenu?.Close();
        ContextMenu menu = new();
        CompositeDisposable bindings = new();
        menu.Closed += (_, _) => { bindings.Dispose(); if (ReferenceEquals(_overflowMenu, menu)) _overflowMenu = null; };
        for (int i = 0; i < _tools.Length; i++)
        {
            Button button = _tools[i];
            if (button.IsVisible) continue;
            MenuItem item = new() { Header = L.S(_toolLabels[i]) };
            if (button.Content is PackIconPhosphorIcons icon)
                item.Icon = new PackIconPhosphorIcons { Kind = icon.Kind, Width = 24, Height = 24 };
            item.Bind(IsEnabledProperty, button.GetObservable(IsEffectivelyEnabledProperty)).DisposeWith(bindings);
            int action = i;
            item.Click += (_, _) =>
            {
                if (_vm?.IsAvailable != true || !button.IsEffectivelyEnabled) return;
                menu.Close();
                if (button.Command is { } command)
                { if (command.CanExecute(button.CommandParameter)) command.Execute(button.CommandParameter); }
                else if (action == 2) FitClicked(button, e);
                else if (action == 3) LocateClicked(button, e);
                else if (action == 4) ZoomOutClicked(button, e);
                else ZoomInClicked(button, e);
            };
            menu.Items.Add(item);
        }
        _overflowMenu = menu;
        menu.Open(OverflowTool);
    }

    /// <summary>沿面板分隔线放置 EditorView 的同款手柄，横屏旋转为垂直方向。</summary>
    private void UpdateHandle()
    {
        double width = ViewConstants.EditorSplitHandleWidth;
        double height = ViewConstants.EditorSplitHandleHeight;
        double inset = ViewConstants.EditorSplitHandleEdgeInset;
        InspectorHandle.Cursor = _wide ? ViewConstants.cursorSizeWE : ViewConstants.cursorSizeNS;
        InspectorHandle.RenderTransform = _wide ? new RotateTransform(90) : null;
        double axis = _wide ? WorkArea.Bounds.Height : CanvasRegion.Bounds.Width;
        double position = inset + Math.Max(0, axis - width - 2 * inset) * _handleRatio;
        Canvas.SetLeft(InspectorHandle, _wide ? CanvasRegion.Bounds.Width - width / 2 : position);
        Canvas.SetTop(InspectorHandle, _wide ? position + (width - height) / 2 : CanvasRegion.Bounds.Height - height / 2);
    }

    /// <summary>捕获鼠标或触摸指针，保存起始布局；复用编辑页面的拖动阈值。</summary>
    private void SplitPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(InspectorHandle).Properties.IsLeftButtonPressed) return;
        _splitDown = true; _splitDragging = false; _splitPress = e.GetPosition(this);
        _splitExtent = _wide ? Inspector.Bounds.Width : Inspector.Bounds.Height;
        _splitRatio = _handleRatio; _splitPointer = e.Pointer;
        e.Pointer.Capture(InspectorHandle); e.Handled = true;
    }

    /// <summary>拖动仅调整面板尺寸及手柄位置，不写入工程或重建图场景。</summary>
    private void SplitMoved(object? sender, PointerEventArgs e)
    {
        if (!_splitDown) return;
        if (!e.GetCurrentPoint(null).Properties.IsLeftButtonPressed) { CancelSplit(); return; }
        Vector delta = e.GetPosition(this) - _splitPress;
        if (!_splitDragging && Math.Max(Math.Abs(delta.X), Math.Abs(delta.Y)) <= ViewConstants.SplitDragThreshold) return;
        _splitDragging = true;
        if (_wide) _inspectorWidth = Math.Clamp(_splitExtent - delta.X, 240, Math.Max(240, WorkArea.Bounds.Width - 160));
        else _inspectorHeight = Math.Clamp(_splitExtent - delta.Y, Math.Min(156, Math.Max(0, WorkArea.Bounds.Height - 96)), Math.Max(0, WorkArea.Bounds.Height - 96));
        double axis = _wide ? WorkArea.Bounds.Height : WorkArea.Bounds.Width;
        _handleRatio = Math.Clamp(_splitRatio + (_wide ? delta.Y : delta.X) / Math.Max(1, axis - ViewConstants.EditorSplitHandleWidth - 2 * ViewConstants.EditorSplitHandleEdgeInset), 0, 1);
        QueueInspectorLayout(); e.Handled = true;
    }

    /// <summary>正常释放结束调整并释放捕获，保持已调整的面板尺寸。</summary>
    private void SplitReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_splitDown) return;
        _splitDown = false; _splitDragging = false; _splitPointer = null; e.Pointer.Capture(null); e.Handled = true;
    }

    /// <summary>窗口失活、模式变化或弹窗打断时清理未完成的拖动。</summary>
    private void CancelSplit()
    {
        _splitDown = false; _splitDragging = false;
        _splitPointer?.Capture(null); _splitPointer = null;
    }

    private void FitClicked(object? sender, RoutedEventArgs e) { if (_vm?.ResolveParameterEdits() == true) GraphCanvas.Fit(); }
    private void LocateClicked(object? sender, RoutedEventArgs e) { if (_vm?.ResolveParameterEdits() == true && _vm.SelectedKey is { } key) GraphCanvas.Locate(key); }
    private void ZoomInClicked(object? sender, RoutedEventArgs e) { if (_vm?.ResolveParameterEdits() == true) GraphCanvas.Zoom(1.25, new Point(GraphCanvas.Bounds.Width / 2, GraphCanvas.Bounds.Height / 2)); }
    private void ZoomOutClicked(object? sender, RoutedEventArgs e) { if (_vm?.ResolveParameterEdits() == true) GraphCanvas.Zoom(.8, new Point(GraphCanvas.Bounds.Width / 2, GraphCanvas.Bounds.Height / 2)); }
}
