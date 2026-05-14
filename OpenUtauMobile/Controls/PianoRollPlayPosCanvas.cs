using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.ViewModels;
using ReactiveUI;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 钢琴卷帘专用播放位置竖线画布。
/// <para>
///     竖线的屏幕 X 坐标由 ViewModel 直接提供（PlayMarkerScreenX），
///     不依赖 TickOffset/PlayPosTick 计算，因此外观位置始终静止，
///     仅当屏幕尺寸改变时（OnNoteAreaSizeChanged）才随之更新。
///     不接收任何输入事件（IsHitTestVisible = false）。
/// </para>
/// </summary>
public class PianoRollPlayPosCanvas : Control
{
    #region 属性封装

    /// <summary>
    /// 播放标记的屏幕 X 坐标（像素）。由 PianoRollViewModel.PlayMarkerScreenX 绑定。
    /// 仅在画布宽度变化时更新，其他时刻保持静止。
    /// </summary>
    public static readonly StyledProperty<double> PlayMarkerScreenXProperty =
        AvaloniaProperty.Register<PianoRollPlayPosCanvas, double>(nameof(PlayMarkerScreenX));

    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<PianoRollPlayPosCanvas, bool>(nameof(IsPlaying));

    public static readonly StyledProperty<bool> IsWaitingProperty =
        AvaloniaProperty.Register<PianoRollPlayPosCanvas, bool>(nameof(IsWaiting));

    public double PlayMarkerScreenX
    {
        get => GetValue(PlayMarkerScreenXProperty);
        set => SetValue(PlayMarkerScreenXProperty, value);
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
    private PianoRollViewModel? _viewModel;
    private readonly DispatcherTimer _blinkTimer;
    private double _blinkOpacity = 1.0;
    private long _blinkStartTimestamp;

    public PianoRollPlayPosCanvas()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        // 闪烁定时器
        _blinkTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.0 / BlinkFps),
        };
        _blinkTimer.Tick += OnBlinkTick;

        // 主题切换时重绘
        MessageBus.Current.Listen<ThemeChangedEvent>()
            .Subscribe(_ => InvalidateVisual());
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _viewModel = DataContext as PianoRollViewModel;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _viewModel = null;
        StopBlink();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _viewModel?.OnNoteAreaSizeChanged(e.NewSize.Width, e.NewSize.Height);
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
    // 每一帧调用，插值更新透明度
    private void UpdateBlinkOpacity()
    {
        double elapsedSec = (Stopwatch.GetTimestamp() - _blinkStartTimestamp) / (double)Stopwatch.Frequency;
        double phase = elapsedSec * GetBlinkHzFromProject();
        double wave = 0.5 * (1.0 + Math.Sin(2.0 * Math.PI * phase + Math.PI / 2.0));
        _blinkOpacity = BlinkMinOpacity + (BlinkMaxOpacity - BlinkMinOpacity) * wave;
    }
    // 获取闪烁频率
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

        if (change.Property == PlayMarkerScreenXProperty ||
            change.Property == IsPlayingProperty ||
            change.Property == IsWaitingProperty)
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// 在播放/等待渲染时，在竖线左侧绘制一条渐淡的拖影矩形。
    /// </summary>
    private static void DrawTrailShadow(DrawingContext context, double lineX, double height,
        Color lineColor, double opacity, double shadowWidth = 24.0)
    {
        double left = lineX - shadowWidth;
        double right = lineX;

        if (right <= 0) return;
        if (left < 0) left = 0;
        if (left >= right) return;

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
        double x = Math.Round(PlayMarkerScreenX) + 0.5;

        // 屏幕 X 未初始化（≤0 且画布有内容时跳过）时不绘制
        if (x < -1 || x > Bounds.Width + 1) return;

        Color lineColor = ThemeResources.GetColor("Sem.Color.Primary");

        if (IsWaiting)
        {
            using (context.PushOpacity(_blinkOpacity))
            {
                DrawTrailShadow(context, x, Bounds.Height, lineColor, 1.0);
                context.DrawLine(ThemeResources.GetPen("Sem.Color.Primary", 2),
                    new Point(x, 0), new Point(x, Bounds.Height));
            }
        }
        else if (IsPlaying)
        {
            DrawTrailShadow(context, x, Bounds.Height, lineColor, 1.0);
            context.DrawLine(ThemeResources.GetPen("Sem.Color.Primary", 2),
                new Point(x, 0), new Point(x, Bounds.Height));
        }
        else
        {
            context.DrawLine(ThemeResources.GetPen("Sem.Color.Primary", 2),
                new Point(x, 0), new Point(x, Bounds.Height));
        }
    }
}