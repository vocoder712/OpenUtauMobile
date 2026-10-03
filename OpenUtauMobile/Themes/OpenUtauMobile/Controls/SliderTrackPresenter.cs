using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Controls;

/// <summary>全局 Slider 轨道绘制；指针输入由共享相对拖动轨道处理。</summary>
public class SliderTrackPresenter : Control
{
    public static readonly StyledProperty<Slider?> SourceProperty = AvaloniaProperty.Register<SliderTrackPresenter, Slider?>(nameof(Source));
    public static readonly StyledProperty<IBrush> ActiveTickBrushProperty = AvaloniaProperty.Register<SliderTrackPresenter, IBrush>(nameof(ActiveTickBrush), Brushes.White);
    public static readonly StyledProperty<IBrush> InactiveTickBrushProperty = AvaloniaProperty.Register<SliderTrackPresenter, IBrush>(nameof(InactiveTickBrush), Brushes.Black);
    public static readonly StyledProperty<IBrush> DisabledActiveBrushProperty = AvaloniaProperty.Register<SliderTrackPresenter, IBrush>(nameof(DisabledActiveBrush), Brushes.Gray);
    public static readonly StyledProperty<IBrush> DisabledInactiveBrushProperty = AvaloniaProperty.Register<SliderTrackPresenter, IBrush>(nameof(DisabledInactiveBrush), Brushes.LightGray);
    public static readonly StyledProperty<double> ThumbSizeProperty = AvaloniaProperty.Register<SliderTrackPresenter, double>(nameof(ThumbSize), SliderTokens.MinHeight);
    public static readonly StyledProperty<double> TrackHeightProperty = AvaloniaProperty.Register<SliderTrackPresenter, double>(nameof(TrackHeight), SliderTokens.TrackHeight);
    public static readonly StyledProperty<double> HandleWidthProperty = AvaloniaProperty.Register<SliderTrackPresenter, double>(nameof(HandleWidth), SliderTokens.HandleWidth);
    public static readonly StyledProperty<double> ActiveHandleWidthProperty = AvaloniaProperty.Register<SliderTrackPresenter, double>(nameof(ActiveHandleWidth), SliderTokens.ActiveHandleWidth);
    public static readonly StyledProperty<double> HandleGapProperty = AvaloniaProperty.Register<SliderTrackPresenter, double>(nameof(HandleGap), SliderTokens.HandleGap);
    public Slider? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public IBrush ActiveTickBrush { get => GetValue(ActiveTickBrushProperty); set => SetValue(ActiveTickBrushProperty, value); }
    public IBrush InactiveTickBrush { get => GetValue(InactiveTickBrushProperty); set => SetValue(InactiveTickBrushProperty, value); }
    public IBrush DisabledActiveBrush { get => GetValue(DisabledActiveBrushProperty); set => SetValue(DisabledActiveBrushProperty, value); }
    public IBrush DisabledInactiveBrush { get => GetValue(DisabledInactiveBrushProperty); set => SetValue(DisabledInactiveBrushProperty, value); }
    public double ThumbSize { get => GetValue(ThumbSizeProperty); set => SetValue(ThumbSizeProperty, value); }
    public double TrackHeight { get => GetValue(TrackHeightProperty); set => SetValue(TrackHeightProperty, value); }
    public double HandleWidth { get => GetValue(HandleWidthProperty); set => SetValue(HandleWidthProperty, value); }
    public double ActiveHandleWidth { get => GetValue(ActiveHandleWidthProperty); set => SetValue(ActiveHandleWidthProperty, value); }
    public double HandleGap { get => GetValue(HandleGapProperty); set => SetValue(HandleGapProperty, value); }
    private Slider? _subscribed;
    private INotifyCollectionChanged? _ticks;
    private bool _attached;

    static SliderTrackPresenter()
    {
        AffectsRender<SliderTrackPresenter>(SourceProperty, ActiveTickBrushProperty, InactiveTickBrushProperty, DisabledActiveBrushProperty, DisabledInactiveBrushProperty,
            ThumbSizeProperty, TrackHeightProperty, HandleWidthProperty, ActiveHandleWidthProperty, HandleGapProperty);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Subscribe();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        Unsubscribe();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SourceProperty) { Unsubscribe(); Subscribe(); }
    }

    private void Subscribe()
    {
        if (!_attached || Source == null) return;
        _subscribed = Source;
        _subscribed.PropertyChanged += OnSourceChanged;
        _subscribed.Classes.CollectionChanged += OnTicksChanged;
        _ticks = Source.Ticks;
        if (_ticks != null) _ticks.CollectionChanged += OnTicksChanged;
    }

    private void Unsubscribe()
    {
        if (_subscribed != null)
        {
            _subscribed.PropertyChanged -= OnSourceChanged;
            _subscribed.Classes.CollectionChanged -= OnTicksChanged;
        }
        if (_ticks != null) _ticks.CollectionChanged -= OnTicksChanged;
        _subscribed = null;
        _ticks = null;
    }

    private void OnSourceChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Slider.TicksProperty) { Unsubscribe(); Subscribe(); }
        InvalidateVisual();
    }

    private void OnTicksChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Source is not { } slider) return;
        bool vertical = slider.Orientation == Orientation.Vertical;
        double length = vertical ? Bounds.Height : Bounds.Width;
        double cross = vertical ? Bounds.Width : Bounds.Height;
        // 绘制和 Track 共用命中区尺寸，桌面缩放后把手与轨道仍使用同一坐标。
        double inset = Math.Min(ThumbSize, length) / 2;
        double extent = length - inset * 2;
        if (extent <= 0) return;
        double range = slider.Maximum - slider.Minimum;
        double fraction = range > 0 ? Math.Clamp((slider.Value - slider.Minimum) / range, 0, 1) : 0;
        bool reverse = vertical != slider.IsDirectionReversed;
        double Position(double f) => inset + (reverse ? 1 - f : f) * extent;
        double center = Position(fraction);
        double handleWidth = slider.Classes.Contains(":pressed") || slider.Classes.Contains(":focus-visible")
            ? ActiveHandleWidth : HandleWidth;
        double gap = HandleGap + handleWidth / 2;
        IBrush active = slider.IsEffectivelyEnabled ? slider.Foreground ?? Brushes.Transparent : DisabledActiveBrush;
        IBrush inactive = slider.IsEffectivelyEnabled ? slider.Background ?? Brushes.Transparent : DisabledInactiveBrush;

        Segment(inset, center - gap, reverse ? inactive : active, true);
        Segment(center + gap, length - inset, reverse ? active : inactive, false);

        double lastTick = double.NegativeInfinity;
        bool showStops = slider.IsSnapToTickEnabled || slider.TickPlacement != TickPlacement.None;
        if (showStops && slider.Ticks is { Count: > 0 })
        {
            foreach (double tick in slider.Ticks.OrderBy(value => value)) Dot(tick);
        }
        else if (showStops && slider.TickFrequency > 0 && double.IsFinite(slider.TickFrequency) && range > 0)
        {
            double steps = range / slider.TickFrequency;
            int visibleCount = Math.Max(1, (int)(extent / 12));
            double stride = Math.Max(1, Math.Ceiling(steps / visibleCount));
            if (double.IsFinite(steps))
                for (int i = 0; i <= visibleCount && i * stride <= steps; i++) Dot(slider.Minimum + i * stride * slider.TickFrequency);
        }
        // 连续参数不显示端点圆点，避免声像等双向参数出现单侧视觉强调。
        if (showStops)
        {
            Dot(slider.Minimum);
            Dot(slider.Maximum);
        }
        return;

        void Dot(double value)
        {
            if (!double.IsFinite(value) || range <= 0 || value < slider.Minimum || value > slider.Maximum) return;
            double f = (value - slider.Minimum) / range;
            // 端点圆点到轨道末端保留 6 DIP，窄轨道也不产生反向区间。
            double stopInset = Math.Min(extent / 2, SliderTokens.StopInset + SliderTokens.StopSize / 2);
            double axis = Math.Clamp(Position(f), inset + stopInset, length - inset - stopInset);
            if (Math.Abs(axis - center) < gap + SliderTokens.StopSize / 2 || Math.Abs(axis - lastTick) < 12) return;
            lastTick = axis;
            IBrush brush = !slider.IsEffectivelyEnabled ? DisabledActiveBrush : f <= fraction ? ActiveTickBrush : InactiveTickBrush;
            Point point = vertical ? new(cross / 2, axis) : new(axis, cross / 2);
            context.DrawEllipse(brush, null, point, SliderTokens.StopSize / 2, SliderTokens.StopSize / 2);
        }

        void Segment(double start, double end, IBrush brush, bool leading)
        {
            if (end <= start) return;
            Rect rect = vertical ? new Rect((cross - TrackHeight) / 2, start, TrackHeight, end - start)
                : new Rect(start, (cross - TrackHeight) / 2, end - start, TrackHeight);
            double outer = TrackHeight / 2;
            double inner = SliderTokens.InsideCorner;
            double first = leading ? outer : inner;
            double last = leading ? inner : outer;
            CornerRadius corners = vertical ? new(first, first, last, last) : new(first, last, last, first);
            context.DrawRectangle(brush, null, new RoundedRect(rect, corners));
        }
    }
}
