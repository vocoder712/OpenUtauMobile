using System;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Controls;

/// <summary>全局 Slider 轨道绘制；输入、捕获和步进仍由框架的 Track 与 Thumb 处理。</summary>
public class SliderTrackPresenter : Control
{
    public static readonly StyledProperty<Slider?> SourceProperty = AvaloniaProperty.Register<SliderTrackPresenter, Slider?>(nameof(Source));
    public static readonly StyledProperty<IBrush> ActiveTickBrushProperty = AvaloniaProperty.Register<SliderTrackPresenter, IBrush>(nameof(ActiveTickBrush), Brushes.White);
    public static readonly StyledProperty<IBrush> InactiveTickBrushProperty = AvaloniaProperty.Register<SliderTrackPresenter, IBrush>(nameof(InactiveTickBrush), Brushes.Black);
    public static readonly StyledProperty<IBrush> DisabledActiveBrushProperty = AvaloniaProperty.Register<SliderTrackPresenter, IBrush>(nameof(DisabledActiveBrush), Brushes.Gray);
    public static readonly StyledProperty<IBrush> DisabledInactiveBrushProperty = AvaloniaProperty.Register<SliderTrackPresenter, IBrush>(nameof(DisabledInactiveBrush), Brushes.LightGray);
    public Slider? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public IBrush ActiveTickBrush { get => GetValue(ActiveTickBrushProperty); set => SetValue(ActiveTickBrushProperty, value); }
    public IBrush InactiveTickBrush { get => GetValue(InactiveTickBrushProperty); set => SetValue(InactiveTickBrushProperty, value); }
    public IBrush DisabledActiveBrush { get => GetValue(DisabledActiveBrushProperty); set => SetValue(DisabledActiveBrushProperty, value); }
    public IBrush DisabledInactiveBrush { get => GetValue(DisabledInactiveBrushProperty); set => SetValue(DisabledInactiveBrushProperty, value); }
    private Slider? _subscribed;
    private INotifyCollectionChanged? _ticks;
    private bool _attached;

    static SliderTrackPresenter()
    {
        AffectsRender<SliderTrackPresenter>(SourceProperty, ActiveTickBrushProperty, InactiveTickBrushProperty, DisabledActiveBrushProperty, DisabledInactiveBrushProperty);
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
        double inset = Math.Min(SliderTokens.MinHeight, length) / 2;
        double extent = length - inset * 2;
        if (extent <= 0) return;
        double range = slider.Maximum - slider.Minimum;
        double fraction = range > 0 ? Math.Clamp((slider.Value - slider.Minimum) / range, 0, 1) : 0;
        bool reverse = vertical != slider.IsDirectionReversed;
        double Position(double f) => inset + (reverse ? 1 - f : f) * extent;
        double center = Position(fraction);
        double handleWidth = slider.Classes.Contains(":pressed") || slider.Classes.Contains(":focus-visible")
            ? SliderTokens.ActiveHandleWidth : SliderTokens.HandleWidth;
        double gap = SliderTokens.HandleGap + handleWidth / 2;
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
        if (showStops) Dot(slider.Minimum);
        Dot(slider.Maximum);
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
            Rect rect = vertical ? new Rect((cross - SliderTokens.TrackHeight) / 2, start, SliderTokens.TrackHeight, end - start)
                : new Rect(start, (cross - SliderTokens.TrackHeight) / 2, end - start, SliderTokens.TrackHeight);
            double outer = SliderTokens.TrackHeight / 2;
            double inner = SliderTokens.InsideCorner;
            double first = leading ? outer : inner;
            double last = leading ? inner : outer;
            CornerRadius corners = vertical ? new(first, first, last, last) : new(first, last, last, first);
            context.DrawRectangle(brush, null, new RoundedRect(rect, corners));
        }
    }
}
