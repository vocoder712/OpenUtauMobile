using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace OpenUtauMobile.Controls;

/// <summary>连续双声道电平、峰值保持和手动复位削波灯；输入暂由原型宿主提供。</summary>
public class MixerLevelMeter : Control
{
    public static readonly StyledProperty<double> LeftDbProperty =
        AvaloniaProperty.Register<MixerLevelMeter, double>(nameof(LeftDb), double.NegativeInfinity);
    public static readonly StyledProperty<double> RightDbProperty =
        AvaloniaProperty.Register<MixerLevelMeter, double>(nameof(RightDb), double.NegativeInfinity);
    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<MixerLevelMeter, IBrush>(nameof(TrackBrush), Brushes.Gray);
    public static readonly StyledProperty<IBrush> LabelBrushProperty =
        AvaloniaProperty.Register<MixerLevelMeter, IBrush>(nameof(LabelBrush), Brushes.Gray);
    public static readonly StyledProperty<IBrush> ClipBrushProperty =
        AvaloniaProperty.Register<MixerLevelMeter, IBrush>(nameof(ClipBrush), Brushes.Red);

    public double LeftDb { get => GetValue(LeftDbProperty); set => SetValue(LeftDbProperty, value); }
    public double RightDb { get => GetValue(RightDbProperty); set => SetValue(RightDbProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public IBrush ClipBrush { get => GetValue(ClipBrushProperty); set => SetValue(ClipBrushProperty, value); }

    private static readonly IBrush SignalBrush = new SolidColorBrush(Color.Parse("#43A875"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#E5AA37"));
    private static readonly int[] Scale = [0, -3, -6, -12, -18, -24, -36, -48, -60];
    private readonly MixerMeterEnvelope _left = new();
    private readonly MixerMeterEnvelope _right = new();
    private readonly DispatcherTimer _timer;
    private double _lastFrame;
    private bool _attached;
    private static double Now => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    static MixerLevelMeter()
    {
        // 输入变化只累计采样，不直接请求重绘，避免音频采样频率带动 UI 刷新。
        AffectsRender<MixerLevelMeter>(TrackBrushProperty, LabelBrushProperty, ClipBrushProperty);
    }

    public MixerLevelMeter()
    {
        Focusable = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(34) };
        _timer.Tick += OnFrame;
        EffectiveViewportChanged += (_, _) => UpdateTimer();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == LeftDbProperty) _left.Push(LeftDb, Now);
        else if (change.Property == RightDbProperty) _right.Push(RightDb, Now);
        else if (change.Property == IsVisibleProperty) { UpdateTimer(); return; }
        else return;
        UpdateTimer();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateTimer();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateTimer()
    {
        if (_attached && IsEffectivelyVisible && (_left.NeedsFrame || _right.NeedsFrame)) _timer.Start();
        else _timer.Stop();
    }

    /// <summary>
    /// 每帧更新包络状态并请求重绘，帧率限制在 30Hz，隐藏期间不积压帧。
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void OnFrame(object? sender, EventArgs e)
    {
        if (!_attached || !IsEffectivelyVisible) { _timer.Stop(); return; }
        double now = Now;
        if (now - _lastFrame < 1.0 / 30) return;
        _lastFrame = now;
        _left.Advance(now);
        _right.Advance(now);
        InvalidateVisual();
        UpdateTimer();
    }

    public void ResetClip()
    {
        _left.ResetClip();
        _right.ResetClip();
        UpdateTimer();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetPosition(this).Y > 24 ||
            (e.Pointer.Type != PointerType.Touch && !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)) return;
        ResetClip();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is not (Key.Space or Key.Enter)) return;
        ResetClip();
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double height = Bounds.Height - 28;
        if (Bounds.Width < 36) return;
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        DrawChannel(context, 0, _left, height);
        DrawChannel(context, 9, _right, height);
        foreach (int db in Scale)
        {
            double y = Y(db, height);
            context.DrawLine(new Pen(LabelBrush), new Point(18, y), new Point(21, y));
            DrawLabel(context, db.ToString(CultureInfo.InvariantCulture), 23, y - 5);
        }
        DrawLabel(context, "L", 0, Bounds.Height - 10);
        DrawLabel(context, "R", 9, Bounds.Height - 10);
    }

    private static double Y(double db, double height) => 12 - db / 60 * height;

    private void DrawChannel(DrawingContext context, double x, MixerMeterEnvelope channel, double height)
    {
        context.DrawRectangle(channel.Clipped ? ClipBrush : TrackBrush, null, new Rect(x, 0, 6, 4), 1, 1);
        context.DrawRectangle(TrackBrush, null, new Rect(x, 12, 6, height), 1, 1);
        DrawRange(context, x, channel.Level, -60, -12, height, SignalBrush);
        DrawRange(context, x, channel.Level, -12, -6, height, WarningBrush);
        DrawRange(context, x, channel.Level, -6, 0, height, ClipBrush);
        if (channel.Peak > -60)
            context.DrawLine(new Pen(LabelBrush, 1.5), new Point(x, Y(channel.Peak, height)), new Point(x + 6, Y(channel.Peak, height)));
    }

    private static void DrawRange(DrawingContext context, double x, double db, double bottom, double top, double height, IBrush brush)
    {
        double end = Math.Min(db, top);
        if (end <= bottom) return;
        context.DrawRectangle(brush, null, new Rect(x, Y(end, height), 6, (end - bottom) / 60 * height));
    }

    private void DrawLabel(DrawingContext context, string value, double x, double y)
    {
        FormattedText text = new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, 8, LabelBrush);
        context.DrawText(text, new Point(x, y));
    }
}

/// <summary>显示包络独立于绘制时钟，下降使用真实经过时间，隐藏期间不积压帧。</summary>
internal sealed class MixerMeterEnvelope
{
    public double Level { get; private set; } = -60;
    public double Peak { get; private set; } = -60;
    public bool Clipped { get; private set; }
    public bool NeedsFrame => _dirty || Level > _input || Peak > Level;
    private double _input = -60;
    private double _pending = -60;
    private double _pendingTime;
    private double _holdUntil;
    private double _lastTime;
    private bool _dirty;

    public void Push(double db, double now)
    {
        if (db >= 0) Clipped = true;
        _input = double.IsNaN(db) ? -60 : Math.Clamp(db, -60, 0);
        // 在两次 UI 帧之间保留最大采样，避免短暂峰值被后续低采样覆盖。
        if (_input >= _pending) { _pending = _input; _pendingTime = now; }
        _dirty = true;
    }

    public void Advance(double now)
    {
        double elapsed = Math.Max(0, now - _lastTime);
        Level = Math.Max(Math.Max(_input, _pending), Level - elapsed * 18);
        if (_pending >= Peak)
        {
            Peak = _pending;
            _holdUntil = _pendingTime + 1;
        }
        if (_input >= Peak)
        {
            Peak = _input;
            _holdUntil = now + 1;
        }
        double releaseTime = Math.Max(0, now - Math.Max(_lastTime, _holdUntil));
        Peak = Math.Max(Level, Peak - releaseTime * 18);
        _pending = -60;
        _lastTime = now;
        _dirty = false;
    }

    public void ResetClip() { Clipped = false; _dirty = true; }
}
