using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using DialogHostAvalonia;
using OpenUtau.Core.ExpressionGraph;
using OpenUtauMobile.Controls.Gestures;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.ExpressionGraph;
using OpenUtauMobile.Services.Dialogs;

namespace OpenUtauMobile.Controls;

/// <summary>单控件图画布；缓存文字、连线与空间索引，节点拖动只预览局部几何。</summary>
public sealed class ExpressionGraphCanvas : Control, IEditorViewport
{
    public static readonly StyledProperty<ExpressionGraphScene?> SceneProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, ExpressionGraphScene?>(nameof(Scene));
    public static readonly StyledProperty<GraphViewport?> ViewportProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, GraphViewport?>(nameof(Viewport));
    public static readonly StyledProperty<string?> SelectedKeyProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, string?>(nameof(SelectedKey));
    public static readonly StyledProperty<IReadOnlySet<int>> SelectedNodeIdsProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, IReadOnlySet<int>>(nameof(SelectedNodeIds), new HashSet<int>());
    public static readonly StyledProperty<bool> IsMultiSelectProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, bool>(nameof(IsMultiSelect));
    public static readonly StyledProperty<IBrush> NodeBrushProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, IBrush>(nameof(NodeBrush), Brushes.LightGray);
    public static readonly StyledProperty<IBrush> InputBrushProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, IBrush>(nameof(InputBrush), Brushes.LightBlue);
    public static readonly StyledProperty<IBrush> OutputBrushProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, IBrush>(nameof(OutputBrush), Brushes.LightGreen);
    public static readonly StyledProperty<IBrush> ForegroundProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, IBrush>(nameof(Foreground), Brushes.Black);
    public static readonly StyledProperty<IBrush> LineBrushProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, IBrush>(nameof(LineBrush), Brushes.Gray);
    public static readonly StyledProperty<IBrush> SelectionBrushProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, IBrush>(nameof(SelectionBrush), Brushes.Blue);
    public ExpressionGraphScene? Scene { get => GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
    public GraphViewport? Viewport { get => GetValue(ViewportProperty); set => SetValue(ViewportProperty, value); }
    public string? SelectedKey { get => GetValue(SelectedKeyProperty); set => SetValue(SelectedKeyProperty, value); }
    public IReadOnlySet<int> SelectedNodeIds { get => GetValue(SelectedNodeIdsProperty); set => SetValue(SelectedNodeIdsProperty, value); }
    public bool IsMultiSelect { get => GetValue(IsMultiSelectProperty); set => SetValue(IsMultiSelectProperty, value); }
    public IBrush NodeBrush { get => GetValue(NodeBrushProperty); set => SetValue(NodeBrushProperty, value); }
    public IBrush InputBrush { get => GetValue(InputBrushProperty); set => SetValue(InputBrushProperty, value); }
    public IBrush OutputBrush { get => GetValue(OutputBrushProperty); set => SetValue(OutputBrushProperty, value); }
    public IBrush Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IBrush LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush SelectionBrush { get => GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
    public event Action<string?>? NodeSelected;

    /// <summary>页面校验字段后返回一次性提交动作；画布只持有拖动期间的预览。</summary>
    public Func<string, Action<Vector>?>? PrepareNodeMove { get; set; }
    /// <summary>页面给出未被浮动操作栏遮挡的画布区域。</summary>
    public Func<Rect>? GetVisibleViewport { get; set; }
    private enum DragTarget { Canvas, Node, Blocked }
    private DragTarget _dragTarget;
    private bool _multiPointerGesture;
    private bool _releasingPointer;
    private bool _nodeMovementStarted;
    private GraphVisualNode? _moveNode;
    private readonly Dictionary<string, GraphVisualNode> _moveNodes = [];
    private Action<Vector>? _moveCommit;
    private Vector _moveOffset;
    private readonly Dictionary<int, (GraphVisualLink Link, StreamGeometry Path)> _moveLinks = [];
    private readonly List<GraphVisualNode> _visibleNodes = [];
    private readonly List<GraphVisualLink> _visibleLinks = [];
    private readonly Dictionary<int, StreamGeometry> _paths = [];
    private enum NodeTextRole { Summary, Title, Port }
    private readonly Dictionary<(string, NodeTextRole), TextLayout> _texts = [];
    private readonly GestureInterpreter _gesture = new(uniformScale: true);
    private ViewportMotionController? _panMotion;
    private ViewportInputSession? _viewportInput;
    private bool _attached;
    private IDisposable? _nativeInput;
    private Window? _window;
    public int LastDrawnNodes { get; private set; }
    public int LastDrawnLinks { get; private set; }

    static ExpressionGraphCanvas() => AffectsRender<ExpressionGraphCanvas>(SceneProperty, SelectedKeyProperty, SelectedNodeIdsProperty, IsMultiSelectProperty,
        ViewportProperty, NodeBrushProperty, InputBrushProperty, OutputBrushProperty, ForegroundProperty, LineBrushProperty, SelectionBrushProperty);

    /// <summary>只在实际输入和数据变化时更新画布。</summary>
    public ExpressionGraphCanvas()
    {
        ClipToBounds = true; Focusable = true;
        _gesture.Tap = _gesture.DoubleTap = point => NodeSelected?.Invoke(Hit(point));
        _gesture.DragBegin = _ => _nodeMovementStarted = _dragTarget == DragTarget.Node;
        _gesture.DragUpdate = (_, step, total, _, timestamp) =>
        {
            if (_dragTarget == DragTarget.Blocked) return;
            if (_dragTarget == DragTarget.Node)
            {
                PreviewNodeMove(total); return;
            }
            if (_panMotion == null) return;
            if (!_panMotion.IsRunning) _panMotion.BeginDirectManipulation(timestamp);
            _panMotion.UpdateDirectManipulation(step, timestamp);
        };
        _gesture.DragEnd = (_, total, timestamp) =>
        {
            if (_dragTarget == DragTarget.Canvas) { _panMotion?.EndDirectManipulation(timestamp); return; }
            Action<Vector>? commit = _nodeMovementStarted ? _moveCommit : null;
            double scale = Viewport?.Scale ?? 1;
            ClearNodeMove();
            commit?.Invoke(total / scale);
        };
        _gesture.PinchUpdate = (scale, _, center, pan) =>
        {
            Zoom(scale, center - pan); Pan(pan);
        };
        AddHandler(PointerTouchPadGestureMagnifyEvent, (_, e) =>
        {
            if (!IsInputAvailable) return;
            e.Handled = SubmitViewportInput(new(ViewportInputKind.Magnify, default, 1 + e.Delta.X,
                e.GetPosition(this), e.KeyModifiers), e.GetPosition(this));
        });
    }

    /// <summary>后台页面、禁用状态和模态弹窗期间禁止继续运动。</summary>
    private bool IsInputAvailable => _attached && IsEffectivelyVisible && IsEffectivelyEnabled && Scene != null && !DialogHost.IsDialogOpen("MainDialogHost");

    bool IEditorViewport.CanNavigateViewport => IsInputAvailable && Viewport != null;
    bool IEditorViewport.SupportsVerticalZoom => false;
    void IEditorViewport.BeginViewportInput() => _panMotion?.Cancel();
    Vector IEditorViewport.PanViewport(Vector pixels) { Pan(pixels); return pixels; }
    void IEditorViewport.ZoomViewport(double scaleX, double scaleY, Point anchor) => ApplyZoom(scaleX, anchor);
    void IEditorViewport.EndViewportInput(bool hadZoomInput, bool interrupted) { }

    /// <summary>系统惯性由平台提供，仅复用共享输入会话的合并和平滑，不叠加拖拽惯性。</summary>
    private bool SubmitViewportInput(ViewportInput input, Point anchor)
    {
        if (input.Phase != ViewportInputPhase.Update)
        {
            if (_viewportInput?.IsActive != true) return false;
            if (input.Phase == ViewportInputPhase.Cancel) _viewportInput.Cancel();
            else _viewportInput.EndInput();
            return true;
        }
        if (!IsInputAvailable || _gesture.HasActivePointers) return false;
        return _viewportInput?.Submit(this, input, anchor, ViewportAxes.Both) == true;
    }

    /// <summary>直接导航或新的触摸操作优先，立即丢弃未应用的平滑尾段和惯性。</summary>
    internal void InterruptMotion()
    {
        _panMotion?.Cancel(); _viewportInput?.Cancel();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneProperty)
        {
            _paths.Clear(); ClearText(); CancelPointers();
            if (Viewport is { Initialized: false } && Bounds.Width > 0) Fit();
        }
        if (change.Property == ForegroundProperty) ClearText();
        if (change.Property == IsMultiSelectProperty) CancelPointers();
        if (change.Property == ViewportProperty || (change.Property == IsEnabledProperty || change.Property == IsEffectivelyEnabledProperty || change.Property == IsVisibleProperty) && change.NewValue is false)
            CancelPointers();
        if (change.Property == BoundsProperty && Viewport is { } viewport)
        {
            Rect old = change.GetOldValue<Rect>();
            if (!viewport.Initialized) Fit();
            else if (old.Width > 0 && old.Height > 0)
            {
                CancelPointers();
                InvalidateVisual();
            }
        }
    }

    private void ClearText()
    {
        foreach (TextLayout text in _texts.Values) text.Dispose();
        _texts.Clear();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        _panMotion = new ViewportMotionController { PanDelta = delta =>
        {
            if (IsInputAvailable) Pan(delta); else _panMotion?.Cancel();
        } };
        _viewportInput = new ViewportInputSession();
        PopupService.Opening += CancelPointers;
        if (TopLevel.GetTopLevel(this) is { } root)
        {
            _window = root as Window;
            if (_window != null) _window.Deactivated += OnDeactivated;
            _nativeInput = ServiceHub.ViewportInputPlatform?.Attach(root, input =>
            {
                if (input.Phase != ViewportInputPhase.Update) return SubmitViewportInput(input, default);
                Point? position = root.TranslatePoint(input.Position, this);
                if (!IsVisible || !IsInputAvailable || position == null || !new Rect(Bounds.Size).Contains(position.Value)) return false;
                return SubmitViewportInput(input, position.Value);
            });
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _nativeInput?.Dispose(); _nativeInput = null;
        PopupService.Opening -= CancelPointers;
        if (_window != null) _window.Deactivated -= OnDeactivated;
        _window = null;
        CancelPointers(); ClearText(); _paths.Clear();
        _panMotion?.Dispose(); _panMotion = null;
        _viewportInput?.Dispose(); _viewportInput = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>只改变视口，适应原有坐标与所有可绘制连线。</summary>
    public void Fit()
    {
        InterruptMotion();
        if (Scene == null || Viewport == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        Rect graph = Scene.Bounds.Inflate(24);
        Viewport.Scale = Math.Clamp(Math.Min(Bounds.Width / graph.Width, Bounds.Height / graph.Height), .08, 1.25);
        Viewport.Offset = new Vector(Bounds.Width, Bounds.Height) / 2 - new Vector(graph.Center.X, graph.Center.Y) * Viewport.Scale;
        Viewport.Initialized = true; InvalidateVisual();
    }

    /// <summary>显式定位稳定节点键，以当前画布中心为锚点。</summary>
    public void Locate(string key, bool onlyIfHidden = false)
    {
        if (Scene == null || !Scene.ByKey.TryGetValue(key, out GraphVisualNode? node) || Viewport == null) return;
        Rect screen = new(ToScreen(node.Bounds.TopLeft), node.Bounds.Size * Viewport.Scale);
        if (onlyIfHidden && new Rect(Bounds.Size).Deflate(16).Contains(screen) && Viewport.Scale >= .9) return;
        InterruptMotion();
        if (!onlyIfHidden || Viewport.Scale < .9) Viewport.Scale = Math.Max(Viewport.Scale, 1);
        Viewport.Offset = new Vector(Bounds.Width, Bounds.Height) / 2 - new Vector(node.Bounds.Center.X, node.Bounds.Center.Y) * Viewport.Scale;
        InvalidateVisual();
    }

    /// <summary>以屏幕锚点等比缩放；不创建动画或持续后台刷新。</summary>
    public void Zoom(double factor, Point anchor)
    {
        InterruptMotion(); ApplyZoom(factor, anchor);
    }

    /// <summary>共享输入会话和直接缩放共用锚点计算，避免平滑更新打断自身。</summary>
    private void ApplyZoom(double factor, Point anchor)
    {
        if (Viewport == null || !double.IsFinite(factor) || factor <= 0) return;
        Point world = ToWorld(anchor);
        Viewport.Scale = Math.Clamp(Viewport.Scale * factor, .08, 3);
        Viewport.Offset = new Vector(anchor.X, anchor.Y) - new Vector(world.X, world.Y) * Viewport.Scale;
        Viewport.Initialized = true; InvalidateVisual();
    }

    /// <summary>直接输入与惯性共用 O(1) 平移路径，只请求重绘，不重建图。</summary>
    private void Pan(Vector delta)
    {
        if (Viewport == null || !double.IsFinite(delta.X) || !double.IsFinite(delta.Y)) return;
        Viewport.Offset += delta; Viewport.Initialized = true; InvalidateVisual();
    }

    /// <summary>绘制、命中和缩放共用同一个逆变换。</summary>
    public Point ToWorld(Point screen) => Viewport == null ? screen : (screen - Viewport.Offset) / Viewport.Scale;
    /// <summary>将图坐标转换为画布 DIP。</summary>
    public Point ToScreen(Point world) => Viewport == null ? world : world * Viewport.Scale + Viewport.Offset;

    /// <summary>定位新粘贴集合；仅在集合大于可见区域时缩小，不改动文档坐标。</summary>
    public void RevealNodes(IReadOnlySet<int> ids)
    {
        if (Scene == null || Viewport == null) return;
        GraphVisualNode[] nodes = Scene.Nodes.Where(node => ids.Contains(node.Data.id)).ToArray();
        if (nodes.Length == 0) return;
        Rect area = GetVisibleViewport?.Invoke() ?? new Rect(Bounds.Size);
        Rect bounds = nodes.Select(node => node.Bounds).Aggregate((a, b) => a.Union(b));
        if (area.Width <= 0 || area.Height <= 0) return;
        InterruptMotion();
        Viewport.Scale = Math.Min(Viewport.Scale, Math.Max(.001, Math.Min(area.Width / Math.Max(1, bounds.Width), area.Height / Math.Max(1, bounds.Height))));
        Viewport.Offset = area.Center - bounds.Center * Viewport.Scale;
        Viewport.Initialized = true;
        InvalidateVisual();
    }

    private bool IsNodeSelected(GraphVisualNode node) => IsMultiSelect ? SelectedNodeIds.Contains(node.Data.id) : node.Key == SelectedKey;

    /// <summary>缓存图坐标几何，仅提交当前视口包围盒查询得到的绘制项。</summary>
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        LastDrawnNodes = LastDrawnLinks = 0;
        if (Scene == null || Viewport == null) return;
        Rect visible = new(ToWorld(default), Bounds.Size / Viewport.Scale);
        Rect candidates = visible.Inflate(3 / Viewport.Scale);
        Scene.NodeIndex.Query(candidates, _visibleNodes);
        Scene.LinkIndex.Query(candidates, _visibleLinks);
        double scale = Viewport.Scale;
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(Viewport.Offset)))
        {
            Pen ordinary = new(LineBrush, 1.5 / scale);
            Pen highlighted = new(SelectionBrush, 2.5 / scale);
            foreach (GraphVisualLink link in _visibleLinks)
            {
                if (_moveLinks.ContainsKey(link.Index)) continue;
                if (!_paths.TryGetValue(link.Index, out StreamGeometry? path))
                {
                    double bend = Math.Max(40, Math.Abs(link.End.X - link.Start.X) * .5);
                    path = new StreamGeometry();
                    using StreamGeometryContext geometry = path.Open();
                    geometry.BeginFigure(link.Start, false);
                    geometry.CubicBezierTo(link.Start + new Vector(bend, 0), link.End - new Vector(bend, 0), link.End);
                    geometry.EndFigure(false); _paths[link.Index] = path;
                }
                context.DrawGeometry(null, IsNodeSelected(link.From) || IsNodeSelected(link.To) ? highlighted : ordinary, path);
            }
            foreach (var preview in _moveLinks.Values)
                context.DrawGeometry(null, highlighted, preview.Path);
            if (_nodeMovementStarted)
            {
                HashSet<string> visibleKeys = _visibleNodes.Select(node => node.Key).ToHashSet();
                foreach (GraphVisualNode moving in _moveNodes.Values)
                    if (visibleKeys.Add(moving.Key)) _visibleNodes.Add(moving);
            }
            foreach (GraphVisualNode node in _visibleNodes)
            {
                using var moved = context.PushTransform(Matrix.CreateTranslation(_nodeMovementStarted && _moveNodes.ContainsKey(node.Key) ? _moveOffset : default));
                context.DrawRectangle(NodeBrush, null, node.Bounds, 8, 8);
                IBrush header = node.Type?.Role == GraphNodeRole.Input ? InputBrush
                    : node.Type?.IsOutput == true ? OutputBrush : NodeBrush;
                context.DrawRectangle(header, null, new Rect(node.Bounds.Position, new Size(node.Bounds.Width, 24)), 8, 8);
                // 标题底色先绘制，外框最后描边，避免圆角和选中边框被标题遮挡。
                context.DrawRectangle(null, IsNodeSelected(node) ? highlighted : ordinary, node.Bounds, 8, 8);
                DrawText(context, node.Title, node.Bounds.TopLeft + new Vector(8, 3), NodeTextRole.Title, node.Bounds.Width - 16);
                if (scale < .45) continue;
                DrawText(context, node.Summary, node.Bounds.TopLeft + new Vector(8, 26), NodeTextRole.Summary, node.Bounds.Width - 16);
                for (int i = 0; i < node.Inputs.Length; i++)
                {
                    Point point = node.InputPoint(i);
                    context.DrawEllipse(LineBrush, null, point, 3, 3);
                    DrawPortLabel(context, node.InputLabels[i], point, node.Bounds.Width / 2 - 12, false);
                }
                for (int i = 0; i < node.Outputs.Length; i++)
                {
                    Point point = node.OutputPoint(i);
                    context.DrawEllipse(LineBrush, null, point, 3, 3);
                    DrawPortLabel(context, node.OutputLabels[i], point, node.Bounds.Width / 2 - 12, true);
                }
            }
        }
        LastDrawnNodes = _visibleNodes.Count; LastDrawnLinks = _visibleLinks.Count;
    }

    /// <summary>按语义缓存文字布局；标题和端口比参数摘要更醒目，平移缩放不重新排版。</summary>
    private TextLayout GetText(string content, NodeTextRole role)
    {
        if (!_texts.TryGetValue((content, role), out TextLayout? text))
        {
            FontWeight weight = role == NodeTextRole.Title ? FontWeight.SemiBold
                : role == NodeTextRole.Port ? FontWeight.Medium : FontWeight.Normal;
            text = new TextLayout(content, new Typeface(FontFamily.Default, weight: weight),
                role == NodeTextRole.Summary ? 12 : 14, Foreground, textWrapping: TextWrapping.NoWrap);
            _texts[(content, role)] = text;
        }
        return text;
    }

    /// <summary>标题和摘要限制在节点内部，不影响节点宽度或连线位置。</summary>
    private void DrawText(DrawingContext context, string content, Point point, NodeTextRole role, double width)
    {
        TextLayout text = GetText(content, role);
        using (context.PushClip(new Rect(point, new Size(width, 24)))) text.Draw(context, point);
    }

    /// <summary>端口文字与接口垂直居中，输入左对齐、输出右对齐，并各占节点的一半。</summary>
    private void DrawPortLabel(DrawingContext context, string content, Point port, double width, bool output)
    {
        TextLayout text = GetText(content, NodeTextRole.Port);
        double left = output ? port.X - 8 - width : port.X + 8;
        double x = output ? port.X - 8 - text.Width : left;
        Rect clip = new(left, port.Y - 10, width, 20);
        using (context.PushClip(clip)) text.Draw(context, new Point(x, port.Y - text.Height / 2));
    }

    private string? Hit(Point screen)
    {
        if (Scene == null || Viewport == null) return null;
        Point point = ToWorld(screen);
        double radius = 22 / Viewport.Scale;
        Scene.NodeIndex.Query(new Rect(point - new Vector(radius, radius), new Size(radius * 2, radius * 2)), _visibleNodes);
        GraphVisualNode? closest = null;
        double distance = double.MaxValue;
        for (int i = _visibleNodes.Count - 1; i >= 0; i--)
            if (_visibleNodes[i].Bounds.Contains(point)) return _visibleNodes[i].Key;
        foreach (GraphVisualNode node in _visibleNodes)
        {
            Rect target = node.Bounds.Inflate(Math.Max(0, radius - node.Bounds.Height / 2));
            double candidate = Distance(node.Bounds.Center, point);
            if (target.Contains(point) && candidate < distance) { closest = node; distance = candidate; }
        }
        return closest?.Key;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Viewport == null || !IsInputAvailable || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        InterruptMotion(); Focus();
        if (_gesture.HasActivePointers)
        {
            ClearNodeMove(); _dragTarget = DragTarget.Blocked; _multiPointerGesture = true;
        }
        else
        {
            _multiPointerGesture = false; _dragTarget = DragTarget.Canvas;
            Point world = ToWorld(e.GetPosition(this));
            string? key = Hit(e.GetPosition(this));
            if (key != null && Scene!.ByKey.TryGetValue(key, out GraphVisualNode? node) && IsNodeSelected(node)
                && (IsMultiSelect || new Rect(node.Bounds.Position, new Size(node.Bounds.Width, 24)).Contains(world)))
            {
                _dragTarget = DragTarget.Blocked;
                Action<Vector>? commit = PrepareNodeMove?.Invoke(key);
                if (commit != null && Scene?.ByKey.TryGetValue(key, out node) == true)
                {
                    _moveNode = node; _moveCommit = commit; _dragTarget = DragTarget.Node;
                    foreach (GraphVisualNode moving in Scene.Nodes.Where(IsNodeSelected)) _moveNodes[moving.Key] = moving;
                }
            }
        }
        _gesture.OnPointerPressed(e, this);
        if (_multiPointerGesture) _dragTarget = DragTarget.Canvas;
    }

    private static double Distance(Point a, Point b) => new Vector(a.X - b.X, a.Y - b.Y).Length;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _gesture.OnPointerMoved(e, this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _releasingPointer = true;
        try { _gesture.OnPointerReleased(e, this); }
        finally { _releasingPointer = false; }
        if (!_gesture.HasActivePointers) { ClearNodeMove(); _multiPointerGesture = false; _dragTarget = DragTarget.Canvas; }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_releasingPointer) return;
        if (_gesture.HasActivePointers) InterruptMotion();
        ClearNodeMove(); _dragTarget = DragTarget.Blocked;
        _gesture.OnPointerCancelled(e, this);
        if (!_gesture.HasActivePointers) { _multiPointerGesture = false; _dragTarget = DragTarget.Canvas; }
    }

    /// <summary>通过共享识别器释放触点和识别状态，避免重挂载后出现残留拖动。</summary>
    public void CancelPointers()
    {
        InterruptMotion(); ClearNodeMove(); _dragTarget = DragTarget.Blocked;
        _gesture.Cancel(); _multiPointerGesture = false; _dragTarget = DragTarget.Canvas;
    }

    /// <summary>预览只更新相邻连线的曲线，节点和全图索引直到提交才重建。</summary>
    private void PreviewNodeMove(Vector pixels)
    {
        if (_moveNode == null || Scene == null || Viewport == null) return;
        _moveOffset = pixels / Viewport.Scale;
        if (!double.IsFinite(_moveOffset.X) || !double.IsFinite(_moveOffset.Y)) return;
        void Update(GraphVisualLink link)
        {
            Point start = link.Start + (_moveNodes.ContainsKey(link.From.Key) ? _moveOffset : default);
            Point end = link.End + (_moveNodes.ContainsKey(link.To.Key) ? _moveOffset : default);
            double bend = Math.Max(40, Math.Abs(end.X - start.X) * .5);
            // 每次预览替换几何；重复 Open 已有对象会保留旧路径，形成拖动残影。
            StreamGeometry path = new();
            using (StreamGeometryContext writer = path.Open())
            {
                writer.BeginFigure(start, false);
                writer.CubicBezierTo(start + new Vector(bend, 0), end - new Vector(bend, 0), end);
                writer.EndFigure(false);
            }
            _moveLinks[link.Index] = (link, path);
        }
        HashSet<int> updated = [];
        foreach (GraphVisualNode node in _moveNodes.Values)
        {
            foreach (GraphVisualLink link in Scene.Incoming[node.Key]) if (updated.Add(link.Index)) Update(link);
            foreach (GraphVisualLink link in Scene.Outgoing[node.Key]) if (updated.Add(link.Index)) Update(link);
        }
        InvalidateVisual();
    }

    /// <summary>取消或释放时丢弃局部预览，取消路径不产生工程命令。</summary>
    private void ClearNodeMove()
    {
        bool hadPreview = _nodeMovementStarted;
        _moveNode = null; _moveCommit = null; _nodeMovementStarted = false; _moveOffset = default; _moveLinks.Clear(); _moveNodes.Clear();
        if (hadPreview) InvalidateVisual();
    }

    private void OnDeactivated(object? sender, EventArgs e) => CancelPointers();

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsInputAvailable) return;
        e.Handled = SubmitViewportInput(new(ViewportInputKind.Wheel, e.Delta, 1, e.GetPosition(this), e.KeyModifiers), e.GetPosition(this));
    }
}
