using System;
using System.Collections.Generic;
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

/// <summary>单控件只读画布；缓存文字、连线与空间索引，无空闲计时器和逐节点控件。</summary>
public sealed class ExpressionGraphCanvas : Control
{
    public static readonly StyledProperty<ExpressionGraphScene?> SceneProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, ExpressionGraphScene?>(nameof(Scene));
    public static readonly StyledProperty<GraphViewport?> ViewportProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, GraphViewport?>(nameof(Viewport));
    public static readonly StyledProperty<string?> SelectedKeyProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, string?>(nameof(SelectedKey));
    public static readonly StyledProperty<IBrush> NodeBrushProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, IBrush>(nameof(NodeBrush), Brushes.LightGray);
    public static readonly StyledProperty<IBrush> InputBrushProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, IBrush>(nameof(InputBrush), Brushes.LightBlue);
    public static readonly StyledProperty<IBrush> OutputBrushProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, IBrush>(nameof(OutputBrush), Brushes.LightGreen);
    public static readonly StyledProperty<IBrush> ForegroundProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, IBrush>(nameof(Foreground), Brushes.Black);
    public static readonly StyledProperty<IBrush> LineBrushProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, IBrush>(nameof(LineBrush), Brushes.Gray);
    public static readonly StyledProperty<IBrush> SelectionBrushProperty = AvaloniaProperty.Register<ExpressionGraphCanvas, IBrush>(nameof(SelectionBrush), Brushes.Blue);
    public ExpressionGraphScene? Scene { get => GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
    public GraphViewport? Viewport { get => GetValue(ViewportProperty); set => SetValue(ViewportProperty, value); }
    public string? SelectedKey { get => GetValue(SelectedKeyProperty); set => SetValue(SelectedKeyProperty, value); }
    public IBrush NodeBrush { get => GetValue(NodeBrushProperty); set => SetValue(NodeBrushProperty, value); }
    public IBrush InputBrush { get => GetValue(InputBrushProperty); set => SetValue(InputBrushProperty, value); }
    public IBrush OutputBrush { get => GetValue(OutputBrushProperty); set => SetValue(OutputBrushProperty, value); }
    public IBrush Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IBrush LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush SelectionBrush { get => GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
    public event Action<string?>? NodeSelected;

    private readonly List<GraphVisualNode> _visibleNodes = [];
    private readonly List<GraphVisualLink> _visibleLinks = [];
    private readonly Dictionary<int, StreamGeometry> _paths = [];
    private readonly Dictionary<(string, bool), TextLayout> _texts = [];
    private readonly GestureInterpreter _gesture = new(uniformScale: true);
    private IDisposable? _nativeInput;
    private Window? _window;
    public int LastDrawnNodes { get; private set; }
    public int LastDrawnLinks { get; private set; }

    static ExpressionGraphCanvas() => AffectsRender<ExpressionGraphCanvas>(SceneProperty, SelectedKeyProperty,
        ViewportProperty, NodeBrushProperty, InputBrushProperty, OutputBrushProperty, ForegroundProperty, LineBrushProperty, SelectionBrushProperty);

    /// <summary>只在实际输入和数据变化时更新画布。</summary>
    public ExpressionGraphCanvas()
    {
        ClipToBounds = true; Focusable = true;
        _gesture.Tap = _gesture.DoubleTap = point => NodeSelected?.Invoke(Hit(point));
        _gesture.DragUpdate = (_, step, _, _, _) => Pan(step);
        _gesture.PinchUpdate = (scale, _, center, pan) =>
        {
            Zoom(scale, center - pan); Pan(pan);
        };
        AddHandler(PointerTouchPadGestureMagnifyEvent, (_, e) =>
        {
            if (!IsInputAvailable) return;
            Zoom(1 + e.Delta.X, e.GetPosition(this)); e.Handled = true;
        });
    }

    private bool IsInputAvailable => IsEnabled && Scene != null && !DialogHost.IsDialogOpen("MainDialogHost");

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SceneProperty)
        {
            _paths.Clear(); ClearText(); CancelPointers();
            if (Viewport is { Initialized: false } && Bounds.Width > 0) Fit();
        }
        if (change.Property == ForegroundProperty) ClearText();
        if (change.Property == BoundsProperty && Viewport is { } viewport)
        {
            Rect old = change.GetOldValue<Rect>();
            if (!viewport.Initialized) Fit();
            else if (old.Width > 0 && old.Height > 0)
            {
                viewport.Offset += new Vector((Bounds.Width - old.Width) / 2, (Bounds.Height - old.Height) / 2);
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
        PopupService.Opening += CancelPointers;
        if (TopLevel.GetTopLevel(this) is { } root)
        {
            _window = root as Window;
            if (_window != null) _window.Deactivated += OnDeactivated;
            _nativeInput = ServiceHub.ViewportInputPlatform?.Attach(root, input =>
            {
                Point? position = root.TranslatePoint(input.Position, this);
                if (!IsVisible || !IsInputAvailable || position == null || !new Rect(Bounds.Size).Contains(position.Value)) return false;
                if (input.Phase == ViewportInputPhase.Cancel) { CancelPointers(); return true; }
                if (input.Phase == ViewportInputPhase.End) return true;
                if (input.Kind == ViewportInputKind.Magnify) Zoom(input.Scale, position.Value);
                else if (input.Modifiers.HasFlag(KeyModifiers.Control)) Zoom(Math.Exp(input.Delta.Y * .12), position.Value);
                else Pan(input.Delta * (input.Kind == ViewportInputKind.Wheel ? 48 : 1));
                return true;
            });
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _nativeInput?.Dispose(); _nativeInput = null;
        PopupService.Opening -= CancelPointers;
        if (_window != null) _window.Deactivated -= OnDeactivated;
        _window = null;
        CancelPointers(); ClearText(); _paths.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>只改变视口，适应原有坐标与所有可绘制连线。</summary>
    public void Fit()
    {
        if (Scene == null || Viewport == null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        Rect graph = Scene.Bounds.Inflate(24);
        Viewport.Scale = Math.Clamp(Math.Min(Bounds.Width / graph.Width, Bounds.Height / graph.Height), .08, 1.25);
        Viewport.Offset = new Vector(Bounds.Width, Bounds.Height) / 2 - new Vector(graph.Center.X, graph.Center.Y) * Viewport.Scale;
        Viewport.Initialized = true; InvalidateVisual();
    }

    /// <summary>定位稳定节点键；详情面板占用空间后仍以剩余画布中心为锚点。</summary>
    public void Locate(string key, bool onlyIfHidden = false)
    {
        if (Scene == null || !Scene.ByKey.TryGetValue(key, out GraphVisualNode? node) || Viewport == null) return;
        Rect screen = new(ToScreen(node.Bounds.TopLeft), node.Bounds.Size * Viewport.Scale);
        if (onlyIfHidden && new Rect(Bounds.Size).Deflate(16).Contains(screen) && Viewport.Scale >= .9) return;
        if (!onlyIfHidden || Viewport.Scale < .9) Viewport.Scale = Math.Max(Viewport.Scale, 1);
        Viewport.Offset = new Vector(Bounds.Width, Bounds.Height) / 2 - new Vector(node.Bounds.Center.X, node.Bounds.Center.Y) * Viewport.Scale;
        InvalidateVisual();
    }

    /// <summary>以屏幕锚点等比缩放；不创建动画或持续后台刷新。</summary>
    public void Zoom(double factor, Point anchor)
    {
        if (Viewport == null || !double.IsFinite(factor) || factor <= 0) return;
        Point world = ToWorld(anchor);
        Viewport.Scale = Math.Clamp(Viewport.Scale * factor, .08, 3);
        Viewport.Offset = new Vector(anchor.X, anchor.Y) - new Vector(world.X, world.Y) * Viewport.Scale;
        Viewport.Initialized = true; InvalidateVisual();
    }

    private void Pan(Vector delta)
    {
        if (Viewport == null || !double.IsFinite(delta.X) || !double.IsFinite(delta.Y)) return;
        Viewport.Offset += delta; Viewport.Initialized = true; InvalidateVisual();
    }

    /// <summary>绘制、命中和缩放共用同一个逆变换。</summary>
    public Point ToWorld(Point screen) => Viewport == null ? screen : (screen - Viewport.Offset) / Viewport.Scale;
    /// <summary>将图坐标转换为画布 DIP。</summary>
    public Point ToScreen(Point world) => Viewport == null ? world : world * Viewport.Scale + Viewport.Offset;

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
                if (!_paths.TryGetValue(link.Index, out StreamGeometry? path))
                {
                    double bend = Math.Max(40, Math.Abs(link.End.X - link.Start.X) * .5);
                    path = new StreamGeometry();
                    using StreamGeometryContext geometry = path.Open();
                    geometry.BeginFigure(link.Start, false);
                    geometry.CubicBezierTo(link.Start + new Vector(bend, 0), link.End - new Vector(bend, 0), link.End);
                    geometry.EndFigure(false); _paths[link.Index] = path;
                }
                context.DrawGeometry(null, link.From.Key == SelectedKey || link.To.Key == SelectedKey ? highlighted : ordinary, path);
            }
            foreach (GraphVisualNode node in _visibleNodes)
            {
                context.DrawRectangle(NodeBrush, node.Key == SelectedKey ? highlighted : ordinary, node.Bounds, 8, 8);
                IBrush header = node.Type?.Role == GraphNodeRole.Input ? InputBrush
                    : node.Type?.IsOutput == true ? OutputBrush : NodeBrush;
                context.DrawRectangle(header, null, new Rect(node.Bounds.Position, new Size(node.Bounds.Width, 24)), 8, 8);
                DrawText(context, node.Title, node.Bounds.TopLeft + new Vector(8, 3), true, node.Bounds.Width - 16);
                if (scale < .45) continue;
                DrawText(context, node.Summary, node.Bounds.TopLeft + new Vector(8, 26), false, node.Bounds.Width - 16);
                for (int i = 0; i < node.Inputs.Length; i++)
                {
                    Point point = node.InputPoint(i);
                    context.DrawEllipse(LineBrush, null, point, 3, 3);
                    DrawText(context, node.InputLabels[i], point + new Vector(8, -9), false, node.Bounds.Width / 2 - 12);
                }
                for (int i = 0; i < node.Outputs.Length; i++)
                {
                    Point point = node.OutputPoint(i);
                    context.DrawEllipse(LineBrush, null, point, 3, 3);
                    DrawText(context, node.OutputLabels[i], point + new Vector(-node.Bounds.Width / 2 + 4, -9), false, node.Bounds.Width / 2 - 12);
                }
            }
        }
        LastDrawnNodes = _visibleNodes.Count; LastDrawnLinks = _visibleLinks.Count;
    }

    private void DrawText(DrawingContext context, string content, Point point, bool bold = false, double width = 212)
    {
        if (!_texts.TryGetValue((content, bold), out TextLayout? text))
        {
            text = new TextLayout(content, new Typeface(FontFamily.Default, weight: bold ? FontWeight.SemiBold : FontWeight.Normal),
                bold ? 14 : 12, Foreground, textWrapping: TextWrapping.NoWrap);
            _texts[(content, bold)] = text;
        }
        using (context.PushClip(new Rect(point, new Size(width, 24)))) text.Draw(context, point);
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
        if (Viewport == null || !IsInputAvailable) return;
        Focus(); _gesture.OnPointerPressed(e, this);
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
        _gesture.OnPointerReleased(e, this);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _gesture.OnPointerCancelled(e, this);
    }

    /// <summary>通过共享识别器释放触点和识别状态，避免重挂载后出现残留拖动。</summary>
    public void CancelPointers() => _gesture.Cancel();

    private void OnDeactivated(object? sender, EventArgs e) => CancelPointers();

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!IsInputAvailable) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) Zoom(Math.Exp(e.Delta.Y * .12), e.GetPosition(this));
        else Pan((e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? new Vector(e.Delta.Y, e.Delta.X) : e.Delta) * 48);
        e.Handled = true;
    }
}
