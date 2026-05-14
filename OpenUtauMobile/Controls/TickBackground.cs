using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.ViewModels;
using ReactiveUI;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 网格背景控件，绘制小节线、拍线、吸附线以及标尺区文本（小节编号、BPM、拍号）。
/// </summary>
public class TickBackground : Control
{
    #region 属性封装

    public static readonly StyledProperty<double> TickWidthProperty =
        AvaloniaProperty.Register<TickBackground, double>(nameof(TickWidth));

    public static readonly StyledProperty<double> TickOffsetProperty =
        AvaloniaProperty.Register<TickBackground, double>(nameof(TickOffset));

    public static readonly StyledProperty<int> TickOriginProperty =
        AvaloniaProperty.Register<TickBackground, int>(nameof(TickOrigin));

    public static readonly StyledProperty<int> SnapDivProperty =
        AvaloniaProperty.Register<TickBackground, int>(nameof(SnapDiv), ViewConstants.DefaultSnapDiv);

    public static readonly StyledProperty<double> RulerHeightProperty =
        AvaloniaProperty.Register<TickBackground, double>(nameof(RulerHeight), ViewConstants.TickRulerHeight);

    public static readonly StyledProperty<bool> ShowBarNumberProperty =
        AvaloniaProperty.Register<TickBackground, bool>(nameof(ShowBarNumber), true);

    public static readonly StyledProperty<bool> ShowTempoMarkersProperty =
        AvaloniaProperty.Register<TickBackground, bool>(nameof(ShowTempoMarkers), true);

    public static readonly StyledProperty<bool> ShowTimeSignatureMarkersProperty =
        AvaloniaProperty.Register<TickBackground, bool>(nameof(ShowTimeSignatureMarkers), true);

    public static readonly StyledProperty<double> MinTicklineWidthProperty =
        AvaloniaProperty.Register<TickBackground, double>(nameof(MinTicklineWidth),
            ViewConstants.PianoRollMinTicklineWidth);

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

    public int SnapDiv
    {
        get => GetValue(SnapDivProperty);
        set => SetValue(SnapDivProperty, value);
    }

    public double RulerHeight
    {
        get => GetValue(RulerHeightProperty);
        set => SetValue(RulerHeightProperty, value);
    }

    public bool ShowBarNumber
    {
        get => GetValue(ShowBarNumberProperty);
        set => SetValue(ShowBarNumberProperty, value);
    }

    public bool ShowTempoMarkers
    {
        get => GetValue(ShowTempoMarkersProperty);
        set => SetValue(ShowTempoMarkersProperty, value);
    }

    public bool ShowTimeSignatureMarkers
    {
        get => GetValue(ShowTimeSignatureMarkersProperty);
        set => SetValue(ShowTimeSignatureMarkersProperty, value);
    }

    public double MinTicklineWidth
    {
        get => GetValue(MinTicklineWidthProperty);
        set => SetValue(MinTicklineWidthProperty, value);
    }

    #endregion

    public TickBackground()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;

        // 主题切换时重绘
        MessageBus.Current.Listen<ThemeChangedEvent>()
            .Subscribe(_ => InvalidateVisual());
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TickWidthProperty ||
            change.Property == TickOffsetProperty ||
            change.Property == TickOriginProperty ||
            change.Property == SnapDivProperty ||
            change.Property == RulerHeightProperty ||
            change.Property == ShowBarNumberProperty ||
            change.Property == ShowTempoMarkersProperty ||
            change.Property == ShowTimeSignatureMarkersProperty ||
            change.Property == MinTicklineWidthProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        switch (DataContext)
        {
            case EditorViewModel evm:
                evm.RequestInvalidateVisual += InvalidateVisual;
                break;
            case PianoRollViewModel pvm:
                pvm.RequestInvalidateVisual += InvalidateVisual;
                break;
        }
    }

    public override void Render(DrawingContext context)
    {
        if (TickWidth <= 0)
        {
            return;
        }

        UProject project = DocManager.Inst.Project;
        int resolution = project.resolution;
        double rulerHeight = RulerHeight;

        // 计算吸附单元（Tick 数）。
        // SnapDiv < 0（自动）或 SnapDiv == 0（关）：均使用 GetSnapUnit 根据当前缩放推导最合适的网格密度。
        // SnapDiv > 0（固定）：直接换算，过密时翻倍。
        int snapUnit;
        if (SnapDiv > 0)
        {
            snapUnit = resolution * 4 / SnapDiv;
            while (snapUnit * TickWidth < MinTicklineWidth)
            {
                snapUnit *= 2;
            }
        }
        else
        {
            double minTicks = TickWidth > 0 ? MinTicklineWidth / TickWidth : resolution;
            MusicMath.GetSnapUnit(resolution, minTicks, triplet: false, out snapUnit, out _);
        }

        double minLineTick = MinTicklineWidth / TickWidth;
        double pixelOffset = (TickOffset + TickOrigin) * TickWidth;
        double leftTick = TickOffset + TickOrigin;
        double rightTick = TickOffset + TickOrigin + Bounds.Width / TickWidth;
        IPen barPen = ThemeResources.GetPen("Sem.Pen.Grid.Bar");
        IPen beatPen = ThemeResources.GetPen("Sem.Pen.Grid.Beat");
        IPen snapGridPen = ThemeResources.GetPen("Sem.Pen.Grid.Snap");
        IPen snapMarkerPen = ThemeResources.GetPen("Sem.Pen.Grid.SnapMarker");

        // 定位起始小节
        project.timeAxis.TickPosToBarBeat(TickOrigin, out int bar, out _, out _);
        if (bar > 0)
        {
            bar--;
        }

        int barTick = project.timeAxis.BarBeatToTickPos(bar, 0);

        while (barTick <= rightTick)
        {
            // 小节线
            double x = Math.Round(barTick * TickWidth - pixelOffset) + 0.5;
            context.DrawLine(barPen, new Point(x, -0.5), new Point(x, Bounds.Height + 0.5));

            // 小节编号
            if (ShowBarNumber)
            {
                TextLayout textLayout = TextLayoutCache.Get((bar + 1).ToString(),
                    ThemeResources.GetBrush("Sem.Color.OnSurfaceVariant"), 10);
                using (context.PushTransform(Matrix.CreateTranslation(x + 3, 10)))
                {
                    textLayout.Draw(context, new Point());
                }
            }

            // 小节内线条
            UTimeSignature timeSig = project.timeAxis.TimeSignatureAtBar(bar);
            int nextBarTick = project.timeAxis.BarBeatToTickPos(bar + 1, 0);
            int ticksPerBeat = resolution * 4 / timeSig.beatUnit;
            int ticksPerLine = snapUnit;
            if (ticksPerBeat < snapUnit)
            {
                ticksPerLine = ticksPerBeat;
            }
            else if (ticksPerBeat % snapUnit != 0)
            {
                if (ticksPerBeat > minLineTick)
                {
                    ticksPerLine = ticksPerBeat;
                }
                else
                {
                    ticksPerLine = nextBarTick - barTick;
                }
            }

            if (nextBarTick > leftTick)
            {
                for (int tick = barTick + ticksPerLine; tick < nextBarTick; tick += ticksPerLine)
                {
                    project.timeAxis.TickPosToBarBeat(tick, out int _, out int _, out int snapRemainingTicks);
                    // 节拍实线、吸附虚线
                    IPen pen = snapRemainingTicks != 0 ? snapGridPen : beatPen;
                    x = Math.Round(tick * TickWidth - pixelOffset) + 0.5;
                    context.DrawLine(pen, new Point(x, rulerHeight), new Point(x, Bounds.Height + 0.5));
                }
            }

            barTick = nextBarTick;
            bar++;
        }

        // 标尺区：BPM 标记
        if (ShowTempoMarkers)
        {
            foreach (UTempo tempo in project.tempos)
            {
                double x = Math.Round(tempo.position * TickWidth - pixelOffset) + 0.5;
                context.DrawLine(snapMarkerPen, new Point(x, 0), new Point(x, rulerHeight));
                TextLayout textLayout = TextLayoutCache.Get(tempo.bpm.ToString("#0.00"),
                    ThemeResources.GetBrush("Sem.Color.OnSurfaceVariant"), 10);
                using (context.PushTransform(Matrix.CreateTranslation(x + 3, 0)))
                {
                    textLayout.Draw(context, new Point());
                }
            }
        }

        // 标尺区：拍号标记
        if (ShowTimeSignatureMarkers)
        {
            foreach (UTimeSignature timeSig in project.timeSignatures)
            {
                int tick = project.timeAxis.BarBeatToTickPos(timeSig.barPosition, 0);
                TextLayout barTextLayout = TextLayoutCache.Get((timeSig.barPosition + 1).ToString(),
                    ThemeResources.GetBrush("Sem.Color.OnSurfaceVariant"), 10);
                double x = Math.Round(tick * TickWidth - pixelOffset) + 0.5 + barTextLayout.Width + 4;
                TextLayout textLayout = TextLayoutCache.Get($"{timeSig.beatPerBar}/{timeSig.beatUnit}",
                    ThemeResources.GetBrush("Sem.Color.OnSurfaceVariant"), 10);
                using (context.PushTransform(Matrix.CreateTranslation(x + 3, 10)))
                {
                    textLayout.Draw(context, new Point());
                }
            }
        }
    }
}