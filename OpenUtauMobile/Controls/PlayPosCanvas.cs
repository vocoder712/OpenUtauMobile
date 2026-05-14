using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using ReactiveUI;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 走带编曲区播放标记。
/// <para>
/// 在走带编曲区叠加绘制一条代表播放指针位置的竖线。
/// - 停止状态：半透明细线（厚度 2，透明度 0.45），无拖影
/// - 播放状态：亮色实线（厚度 2）+ 左侧渐淡拖影矩形
/// - 等待渲染：亮色线以连续波形（默认正弦）缓慢闪烁（透明度 0.2 ↔ 1.0）+ 拖影同步闪烁
/// 不接收任何输入事件（IsHitTestVisible = false）。
/// </para>
/// </summary>
public class PlayPosCanvas : Control
{
    #region 属性封装

    public static readonly StyledProperty<double> TickWidthProperty =
        AvaloniaProperty.Register<PlayPosCanvas, double>(nameof(TickWidth));

    public static readonly StyledProperty<double> TickOffsetProperty =
        AvaloniaProperty.Register<PlayPosCanvas, double>(nameof(TickOffset));

    public static readonly StyledProperty<int> TickOriginProperty =
        AvaloniaProperty.Register<PlayPosCanvas, int>(nameof(TickOrigin));

    public static readonly StyledProperty<int> PlayPosTickProperty =
        AvaloniaProperty.Register<PlayPosCanvas, int>(nameof(PlayPosTick));

    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<PlayPosCanvas, bool>(nameof(IsPlaying));

    public static readonly StyledProperty<bool> IsWaitingProperty =
        AvaloniaProperty.Register<PlayPosCanvas, bool>(nameof(IsWaiting));

    public double TickWidth
    {
        get => GetValue(TickWidthProperty);
        set => SetValue(TickWidthProperty, value);
    }

    public double TickOffset
    {
        get => GetValue(TickOffsetProperty);
        set => SetValue(TickOffsetProperty, value);
    }

    public int TickOrigin
    {
        get => GetValue(TickOriginProperty);
        set => SetValue(TickOriginProperty, value);
    }

    public int PlayPosTick
    {
        get => GetValue(PlayPosTickProperty);
        set => SetValue(PlayPosTickProperty, value);
    }

    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    public bool IsWaiting
    {
        get => GetValue(IsWaitingProperty);
        set => SetValue(IsWaitingProperty, value);
    }

    #endregion

    private const double BlinkMinOpacity = 0.2;
    private const double BlinkMaxOpacity = 1.0;
    private const double MinBlinkHz = 0.25;
    private const double MaxBlinkHz = 4.0;
    private const double BlinkFps = 30.0;

    private readonly DispatcherTimer _blinkTimer;
    private double _blinkOpacity = 1.0;
    private long _blinkStartTimestamp;

    public PlayPosCanvas()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;

        _blinkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.0 / BlinkFps),
        };
        _blinkTimer.Tick += OnBlinkTick;

        // 主题切换时重绘
        MessageBus.Current.Listen<ThemeChangedEvent>()
            .Subscribe(_ => InvalidateVisual());
    }

    private void OnBlinkTick(object? sender, EventArgs e)
    {
        UpdateBlinkOpacity();
        InvalidateVisual();
    }

    private void StartBlink()
    {
        _blinkStartTimestamp = Stopwatch.GetTimestamp();
        _blinkOpacity = BlinkMaxOpacity;
        _blinkTimer.Start();
    }

    private void StopBlink()
    {
        _blinkTimer.Stop();
        _blinkOpacity = BlinkMaxOpacity;
        InvalidateVisual();
    }

    private void UpdateBlinkOpacity()
    {
        double elapsedSec = (Stopwatch.GetTimestamp() - _blinkStartTimestamp) / (double)Stopwatch.Frequency;
        double phase = elapsedSec * GetBlinkHzFromProject();
        double wave = 0.5 * (1.0 + Math.Sin(2.0 * Math.PI * phase + Math.PI / 2.0));
        _blinkOpacity = BlinkMinOpacity + (BlinkMaxOpacity - BlinkMinOpacity) * wave;
    }

    private static double GetBlinkHzFromProject()
    {
        double bpm = DocManager.Inst.Project.tempos.Count > 0
            ? DocManager.Inst.Project.tempos[0].bpm / 2
            : 60d;
        return Math.Clamp(bpm / 60.0, MinBlinkHz, MaxBlinkHz);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsWaitingProperty)
        {
            if (GetValue(IsWaitingProperty))
                StartBlink();
            else
                StopBlink();
        }

        if (change.Property == TickWidthProperty ||
            change.Property == TickOffsetProperty ||
            change.Property == TickOriginProperty ||
            change.Property == PlayPosTickProperty ||
            change.Property == IsPlayingProperty ||
            change.Property == IsWaitingProperty)
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 在播放/等待渲染时，在竖线左侧绘制一条渐淡的拖影矩形。
    /// 拖影宽度固定为 <paramref name="shadowWidth"/> 像素，颜色与主线相同但透明度从右到左线性衰减至 0。
    /// </summary>
    private static void DrawTrailShadow(DrawingContext context, double lineX, double height,
        Color lineColor, double opacity, double shadowWidth = 24.0)
    {
        double left = lineX - shadowWidth;
        double right = lineX; // 拖影右边缘紧贴竖线左侧

        // 超出画布左边缘时裁剪
        if (right <= 0) return;
        if (left < 0) left = 0;
        if (left >= right) return;

        // 渐变：左侧全透明 → 右侧按 opacity 显示线色
        Color startColor = Color.FromArgb(0, lineColor.R, lineColor.G, lineColor.B);
        Color endColor = Color.FromArgb((byte)(lineColor.A * opacity * 0.45), lineColor.R, lineColor.G, lineColor.B);

        LinearGradientBrush brush = new()
        {
            StartPoint = new RelativePoint(new Point(left, 0), RelativeUnit.Absolute),
            EndPoint = new RelativePoint(new Point(right, 0), RelativeUnit.Absolute),
            GradientStops =
            {
                new GradientStop(startColor, 0.0),
                new GradientStop(endColor, 1.0),
            },
            SpreadMethod = GradientSpreadMethod.Pad,
        };

        context.FillRectangle(brush, new Rect(left, 0, right - left, height));
    }

    public override void Render(DrawingContext context)
    {
        if (TickWidth <= 0) return;

        double x = Math.Round((PlayPosTick - TickOffset - TickOrigin) * TickWidth) + 0.5;

        // 超出可视范围则不绘制
        if (x < -1 || x > Bounds.Width + 1) return;

        Color lineColor = ThemeResources.GetColor("Sem.Color.Primary");

        if (IsWaiting)
        {
            // 等待渲染：闪烁的亮色线（厚度 2）+ 拖影同步闪烁
            using (context.PushOpacity(_blinkOpacity))
            {
                DrawTrailShadow(context, x, Bounds.Height, lineColor, 1.0);
                context.DrawLine(ThemeResources.GetPen("Sem.Color.Primary", 2),
                    new Point(x, 0), new Point(x, Bounds.Height));
            }
        }
        else if (IsPlaying)
        {
            // 播放中：亮色实线（厚度 2）+ 左侧渐淡拖影
            DrawTrailShadow(context, x, Bounds.Height, lineColor, 1.0);
            context.DrawLine(ThemeResources.GetPen("Sem.Color.Primary", 2),
                new Point(x, 0), new Point(x, Bounds.Height));
        }
        else
        {
            // 停止：细线（厚度 2），无拖影
            context.DrawLine(ThemeResources.GetPen("Sem.Color.Primary", 2),
                new Point(x, 0), new Point(x, Bounds.Height));
        }
    }
}