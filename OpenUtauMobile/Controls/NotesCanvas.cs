using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public class NotesCanvas : Control, ICmdSubscriber
{
    #region 注册属性

    // 数据源
    public static readonly StyledProperty<UVoicePart?> PartProperty =
        AvaloniaProperty.Register<NotesCanvas, UVoicePart?>(nameof(Part));

    // 变换属性：像素/Tick (横向缩放)
    public static readonly StyledProperty<double> TickWidthProperty =
        AvaloniaProperty.Register<NotesCanvas, double>(nameof(TickWidth), 0.1);

    // 变换属性：音符高度 (纵向缩放)
    public static readonly StyledProperty<double> KeyHeightProperty =
        AvaloniaProperty.Register<NotesCanvas, double>(nameof(KeyHeight), 40.0);

    // 变换属性：X轴偏移 (平移/滚动)
    public static readonly StyledProperty<double> TickOffsetProperty =
        AvaloniaProperty.Register<NotesCanvas, double>(nameof(TickOffset));

    // 变换属性：Y轴偏移 (平移/滚动)
    public static readonly StyledProperty<double> KeyOffsetProperty =
        AvaloniaProperty.Register<NotesCanvas, double>(nameof(KeyOffset));

    #endregion

    #region 属性封装

    public UVoicePart? Part
    {
        get => GetValue(PartProperty);
        set => SetValue(PartProperty, value);
    }

    public double TickWidth
    {
        get => GetValue(TickWidthProperty);
        set => SetValue(TickWidthProperty, value);
    }

    public double KeyHeight
    {
        get => GetValue(KeyHeightProperty);
        set => SetValue(KeyHeightProperty, value);
    }

    public double TickOffset
    {
        get => GetValue(TickOffsetProperty);
        set => SetValue(TickOffsetProperty, value);
    }

    public double KeyOffset
    {
        get => GetValue(KeyOffsetProperty);
        set => SetValue(KeyOffsetProperty, value);
    }

    #endregion

    #region 内部属性

    private PianoRollViewModel? ViewModel { get; set; }

    // 帧率计数器
    private readonly Stopwatch _fpsStopwatch = new();

    private int _frameCount;

    // 绘制辅助
    private readonly Points _tmpPoints = []; // 临时缓存，避免GC
    private readonly PolylineGeometry _tmpPolylineGeometry = new(); // 临时缓存，避免GC
    private const double PitchHandleRadius = 5.0;
    private const double PitchHandleDiameter = PitchHandleRadius * 2;
    private readonly Geometry _pointGeometry = new EllipseGeometry(
        new Rect(-PitchHandleRadius, -PitchHandleRadius, PitchHandleDiameter, PitchHandleDiameter));

    private readonly Geometry
        _selectedPointGeometry = new EllipseGeometry(
            new Rect(-PitchHandleRadius, -PitchHandleRadius, PitchHandleDiameter, PitchHandleDiameter));

    // 颤音叠层画笔/画笔缓存，按主题变化重建以避免每帧分配
    private bool _vibratoBrushCacheValid;
    private bool _vibratoBrushCacheIsDark;
    private IBrush? _vibWaveBrushEnabled, _vibWaveBrushDisabled;
    private IBrush? _vibNoteFillBrush;
    private IBrush? _vibGuideBrushEnabled, _vibGuideBrushDisabled;
    private IBrush? _vibFineGuideBrushEnabled, _vibFineGuideBrushDisabled;
    private IBrush? _vibTrackFillBrush, _vibTrackActiveFillBrush, _vibTrackBorderBrush;
    private IPen? _vibWavePenEnabled, _vibWavePenDisabled;
    private IPen? _vibGuidePenEnabled, _vibGuidePenDisabled;
    private IPen? _vibFineGuidePenEnabled, _vibFineGuidePenDisabled;
    private IPen? _vibTrackBorderPen;
    private IBrush? _vibHandleHaloBrush;
    private IBrush? _vibHandleBrush;
    private IPen? _vibHandlePen;

    #endregion

    #region 事件订阅

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not PianoRollViewModel vm) return;

        if (ViewModel != null) ViewModel.RequestInvalidateVisual -= InvalidateVisual;
        ViewModel = vm;
        ViewModel.RequestInvalidateVisual += InvalidateVisual;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        // 在绑定的UPart和视口变换属性发生变化时触发重绘
        base.OnPropertyChanged(change);
        if (change.Property == PartProperty ||
            change.Property == TickWidthProperty ||
            change.Property == KeyHeightProperty ||
            change.Property == TickOffsetProperty ||
            change.Property == KeyOffsetProperty)
        {
            InvalidateVisual();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DocManager.Inst.AddSubscriber(this);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DocManager.Inst.RemoveSubscriber(this);
    }

    #endregion

    #region 渲染绘图

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(Brushes.Transparent, null, Bounds.WithX(0).WithY(0)); // 绘制透明背景以启用点击事件
        if (Part == null || ViewModel == null)
            return;
        // 计算可见区域的Tick范围，相对项目
        int leftTick = (int)TickOffset;
        int rightTick = (int)(leftTick + Bounds.Width / TickWidth);
        SolidColorBrush brush = TrackPalette.GetTrackColor(DocManager.Inst.Project.tracks[Part.trackNo].TrackColor)
            .AccentColor;
        // ———— 绘制主体、手柄 ————
        foreach (UNote note in Part.notes)
        {
            if (note.position + Part.position > rightTick)
            {
                break; // 后续音符都在右侧不可见，停止绘制
            }

            if (note.End + Part.position < leftTick)
            {
                continue; // 音符在左侧不可见，跳过
            }

            RenderNoteBody(note, context, brush); // 主体

            if ((ViewModel.EditMode != PianoRollEditMode.MultiSelect &&
                 ViewModel.EditMode != PianoRollEditMode.Note) ||
                !ViewModel.SelectedNotes.Contains(note)) // 仅在可直接移动或缩放音符的模式中显示 resize 手柄。
            {
                continue;
            }

            Point lt = ViewModel.TickPitchToPoint(note.position + Part.position, note.AdjustedTone);
            lt = lt.WithX(lt.X + 1).WithY(Math.Round(lt.Y + 1));
            Size sz = ViewModel.TickToneToSize(note.duration, 1);
            sz = sz.WithWidth(sz.Width - 1).WithHeight(Math.Floor(sz.Height - 2));
            DrawNoteResizeHandle(context, new Rect(lt, sz));
        }

        // ———— 绘制最终音高线 ————
        RenderFinalPitch(leftTick, rightTick, context);
        // ———— 绘制锚点 ————
        foreach (UNote note in Part.notes)
        {
            if (note.position + Part.position > rightTick)
            {
                break; // 后续音符都在右侧不可见，停止绘制
            }

            if (note.End + Part.position < leftTick)
            {
                bool anchorVisible = ViewModel.EditMode == PianoRollEditMode.Anchor
                    && note.pitch.data.Count >= 2
                    && DocManager.Inst.Project.timeAxis.MsPosToTickPos(note.PositionMs + note.pitch.data[note.pitch.data.Count - 1].X) >= leftTick;
                if (!anchorVisible)
                    continue; // 音符在左侧不可见，跳过
            }

            // 绘制锚点
            if (ViewModel.EditMode == PianoRollEditMode.Anchor)
            {
                RenderPitchBend(note, context);
            }
        }

        if (ViewModel.EditMode == PianoRollEditMode.Vibrato &&
            ViewModel.GetActiveVibratoOverlayLayout() is { } vibratoLayout)
        {
            RenderVibratoOverlay(vibratoLayout, context);
        }

        // ———— 绘制遮罩 ————
        double partStartX = ViewModel.TickToPointX(Part.position);
        double partEndX = ViewModel.TickToPointX(Part.End);
        if (partStartX > 0)
        {
            double maskWidth = Math.Min(partStartX, Bounds.Width);
            DrawNonEditableMask(context, new Rect(0, 0, maskWidth, Bounds.Height));
        }

        if (partEndX < Bounds.Width)
        {
            double maskLeft = Math.Max(partEndX, 0);
            DrawNonEditableMask(context, new Rect(maskLeft, 0, Bounds.Width - maskLeft, Bounds.Height));
        }

        // ———— 绘制选区可视化叠层 ————
        if (ViewModel.EditMode == PianoRollEditMode.MultiSelect && ViewModel.IsSelecting)
        {
            RenderSelectingRangeOverlay(context);
        }

        // ———— 绘制画笔指示器 ————
        RenderPitchDrawIndicator(context);
        // ———— 帧速率计数器 ————
        if (ViewConstants.EnableBenchMarkTest)
        {
            _frameCount++;
        }
    }

    /// <summary>
    /// 绘制音符主体（矩形 + 歌词）
    /// </summary>
    /// <param name="note"></param>
    /// <param name="context"></param>
    /// <param name="brush"></param>
    private void RenderNoteBody(UNote note, DrawingContext context, SolidColorBrush brush)
    {
        if (ViewModel == null || Part == null) return;
        Point leftTop = ViewModel.TickPitchToPoint(note.position + Part.position, note.AdjustedTone);
        leftTop = leftTop.WithX(leftTop.X + 1).WithY(Math.Round(leftTop.Y + 1));
        Size size = ViewModel.TickToneToSize(note.duration, 1);
        size = size.WithWidth(size.Width - 1).WithHeight(Math.Floor(size.Height - 2));
        Point rightBottom = new(leftTop.X + size.Width, leftTop.Y + size.Height);

        // 根据选中状态和错误状态选择颜色
        bool isSelected = ViewModel.SelectedNotes.Contains(note);
        bool isError = note.Error;

        // 错误音符透明度更高
        using (context.PushOpacity(isError ? 0.24 : 0.88))
        {
            context.DrawRectangle(brush, null, new Rect(leftTop, rightBottom));
        }

        // 边框：表示选中状态
        IPen borderPen = isSelected
            ? ThemeResources.GetPen("Sem.Color.Primary", 1.5)
            : ThemeResources.GetPen("Sem.Color.OutlineVariant", 1);
        context.DrawRectangle(null, borderPen, new Rect(leftTop, rightBottom));

        if (ViewModel.KeyHeight < 10 || note.lyric.Length == 0)
        {
            return;
        }

        string displayLyric = note.lyric;
        int txtsize = 12;
        TextLayout textLayout =
            TextLayoutCache.Get(displayLyric, ThemeResources.GetBrush("Sem.Color.OnSurface"), txtsize);
        if (txtsize > size.Height)
        {
            return; // 空间太小，无法显示歌词
        }

        if (textLayout.Height + 5 < size.Height)
        {
            txtsize = (int)(12 * (size.Height / textLayout.Height));
            textLayout = TextLayoutCache.Get(displayLyric, ThemeResources.GetBrush("Sem.Color.OnSurface"), txtsize);
        }

        if (textLayout.Width + 5 > size.Width)
        {
            displayLyric = displayLyric[0] + ".."; // 尝试使用省略号
            textLayout = TextLayoutCache.Get(displayLyric, ThemeResources.GetBrush("Sem.Color.OnSurface"), txtsize);
            if (textLayout.Width + 5 > size.Width)
            {
                return;
            }
        }

        Point textPosition = leftTop.WithX(leftTop.X + 5)
            .WithY(Math.Round(leftTop.Y - KeyHeight + (size.Height - textLayout.Height) / 2));
        using (context.PushTransform(Matrix.CreateTranslation(textPosition.X, textPosition.Y)))
        {
            textLayout.Draw(context, new Point());
        }
    }

    /// <summary>
    /// 在音符矩形右侧外部绘制拖拽手柄（圆角矩形 + 双竖线图标）。
    /// noteRect 为音符在画布上的屏幕坐标矩形（已含 1px padding）。
    /// </summary>
    private static void DrawNoteResizeHandle(DrawingContext context, Rect noteRect)
    {
        const double hW = ViewConstants.NoteResizeHandleVisualWidth;
        const double gap = ViewConstants.NoteResizeHandleGap;
        const double vPad = 1.5;

        Rect handleRect = new(
            noteRect.Right + gap,
            noteRect.Y + vPad,
            hW,
            noteRect.Height - vPad * 2);

        if (handleRect.Height <= 0) return;

        // 背景圆角矩形（外侧无需裁切）
        context.DrawRectangle(ThemeResources.GetBrush("Sem.Color.Primary"), null, handleRect, radiusX: 3, radiusY: 3);

        // ‖ 图标：2 条竖向短线，居中于 handleRect
        double cx = handleRect.X + handleRect.Width / 2.0;
        double lineH = handleRect.Height * 0.45;
        double ly1 = handleRect.Y + (handleRect.Height - lineH) / 2.0;
        double ly2 = ly1 + lineH;

        context.DrawLine(ThemeResources.GetPen("Sem.Color.OnPrimary", 1.5), new Point(cx - 2.5, ly1),
            new Point(cx - 2.5, ly2));
        context.DrawLine(ThemeResources.GetPen("Sem.Color.OnPrimary", 1.5), new Point(cx + 2.5, ly1),
            new Point(cx + 2.5, ly2));
    }

    /// <summary>
    /// 绘制最终音高曲线
    /// </summary>
    /// <param name="leftTick"></param>
    /// <param name="rightTick"></param>
    /// <param name="context"></param>
    private void RenderFinalPitch(int leftTick, int rightTick, DrawingContext context)
    {
        if (ViewModel == null || Part == null) return;
        IPen pen = ThemeResources.GetPen("Sem.Color.Outline", 2);
        lock (Part)
        {
            foreach (RenderPhrase phrase in Part.renderPhrases)
            {
                if (phrase.position > rightTick || phrase.end < leftTick)
                {
                    continue;
                }

                int pitchStart = phrase.position - phrase.leading;
                int startIdx = Math.Max(0, (leftTick - pitchStart) / 5);
                int endIdx = Math.Min(phrase.pitches.Length, (rightTick - pitchStart) / 5 + 1);
                _tmpPoints.Clear();
                for (int i = startIdx; i < endIdx; ++i)
                {
                    int t = pitchStart + i * 5;
                    float p = phrase.pitches[i];
                    _tmpPoints.Add(ViewModel.TickPitchToPoint(t, p / 100 - 0.5));
                }

                _tmpPolylineGeometry.Points = _tmpPoints;
                context.DrawGeometry(null, pen, _tmpPolylineGeometry);
            }
        }
    }

    /// <summary>
    /// 绘制锚点
    /// </summary>
    /// <param name="note"></param>
    /// <param name="context"></param>
    private void RenderPitchBend(UNote note, DrawingContext context)
    {
        if (ViewModel == null)
        {
            return;
        }

        UPitch pitchExp = note.pitch;
        List<PitchPoint> pts = pitchExp.data;
        if (pts.Count < 2 || Part == null)
        {
            return;
        }

        UProject project = DocManager.Inst.Project;
        bool isAnchorMode = (ViewModel.EditMode == PianoRollEditMode.Anchor);

        // 先处理第一个点p0
        int p0Tick = project.timeAxis.MsPosToTickPos(note.PositionMs + pts[0].X);
        double p0Tone = note.AdjustedTone + pts[0].Y / 10.0;
        Point p0 = ViewModel.TickPitchToPoint(p0Tick, p0Tone - 0.5);
        _tmpPoints.Clear();
        _tmpPoints.Add(p0);

        // 绘制第一个锚点（pts[0]）
        {
            bool isSelected = isAnchorMode && ViewModel.SelectedAnchors.Contains(pts[0]);
            IBrush brush = note.pitch.snapFirst && !isSelected
                ? ThemeResources.GetBrush("Sem.Color.Secondary")
                : ThemeResources.GetBrush("Sem.Color.Primary");
            IPen pen = isSelected
                ? ThemeResources.GetPen("Sem.Color.Tertiary", 2.2)
                : ThemeResources.GetPen("Sem.Color.OnSurface", 1.15);
            Geometry geom = isSelected ? _selectedPointGeometry : _pointGeometry;
            using (context.PushTransform(Matrix.CreateTranslation(p0.X, p0.Y)))
                context.DrawGeometry(brush, pen, geom);
        }

        // 处理剩余点
        for (int i = 1; i < pts.Count; i++)
        {
            int p1Tick = project.timeAxis.MsPosToTickPos(note.PositionMs + pts[i].X);
            double p1Tone = note.AdjustedTone + pts[i].Y / 10.0;
            Point p1 = ViewModel.TickPitchToPoint(p1Tick, p1Tone - 0.5);

            // 绘制曲线
            double x0 = p0.X;
            double y0 = p0.Y;
            double x1 = p0.X;
            if (p1.X - p0.X < 5)
            {
                _tmpPoints.Add(p1);
            }
            else
            {
                _tmpPoints.Add(new Point(x0, y0));
                while (x0 < p1.X)
                {
                    x1 = Math.Min(x1 + 4, p1.X);
                    double y1 = MusicMath.InterpolateShape(p0.X, p1.X, p0.Y, p1.Y, x1, pts[i - 1].shape);
                    _tmpPoints.Add(new Point(x1, y1));
                    x0 = x1;
                }
            }

            p0 = p1; // 往后移一个点

            // 绘制第 i 个锚点，根据选中态决定外观
            bool isSelected = isAnchorMode && ViewModel.SelectedAnchors.Contains(pts[i]);
            IBrush ptBrush = ThemeResources.GetBrush("Sem.Color.Primary");
            IPen ptPen = isSelected // 外框
                ? ThemeResources.GetPen("Sem.Color.Tertiary", 2.2)
                : ThemeResources.GetPen("Sem.Color.OnSurface", 1.15);
            Geometry ptGeom = isSelected ? _selectedPointGeometry : _pointGeometry;
            using (context.PushTransform(Matrix.CreateTranslation(p0.X, p0.Y)))
                context.DrawGeometry(ptBrush, ptPen, ptGeom);
        }

        _tmpPolylineGeometry.Points = _tmpPoints;
        context.DrawGeometry(null, ThemeResources.GetPen("Sem.Color.Primary", 2.0), _tmpPolylineGeometry);
    }

    private void RebuildVibratoBrushCache(bool isDark)
    {
        Color primaryColor = ThemeResources.GetColor("Sem.Color.Primary");
        Color primaryContainerColor = ThemeResources.GetColor("Sem.Color.PrimaryContainer");
        Color outlineVariantColor = ThemeResources.GetColor("Sem.Color.OutlineVariant");

        _vibWaveBrushEnabled = new SolidColorBrush(primaryColor, 0.96);
        _vibWaveBrushDisabled = new SolidColorBrush(primaryColor, 0.78);
        _vibNoteFillBrush = new SolidColorBrush(primaryColor, 0.14);
        _vibGuideBrushEnabled = new SolidColorBrush(primaryColor, 0.82);
        _vibGuideBrushDisabled = new SolidColorBrush(primaryColor, 0.62);
        _vibFineGuideBrushEnabled = new SolidColorBrush(primaryColor, 0.56);
        _vibFineGuideBrushDisabled = new SolidColorBrush(primaryColor, 0.42);
        _vibTrackFillBrush = new SolidColorBrush(primaryContainerColor, 0.70);
        _vibTrackActiveFillBrush = new SolidColorBrush(primaryColor, 0.38);
        _vibTrackBorderBrush = new SolidColorBrush(outlineVariantColor, 0.92);
        _vibWavePenEnabled = new Pen(_vibWaveBrushEnabled, 2.15);
        _vibWavePenDisabled = new Pen(_vibWaveBrushDisabled, 1.8);
        _vibGuidePenEnabled = new Pen(_vibGuideBrushEnabled, 2.0);
        _vibGuidePenDisabled = new Pen(_vibGuideBrushDisabled, 2.0);
        _vibFineGuidePenEnabled = new Pen(_vibFineGuideBrushEnabled, 1.3);
        _vibFineGuidePenDisabled = new Pen(_vibFineGuideBrushDisabled, 1.3);
        _vibTrackBorderPen = new Pen(_vibTrackBorderBrush, 1);
        _vibHandleHaloBrush = new SolidColorBrush(primaryColor, 0.16);
        _vibHandleBrush = ThemeResources.GetBrush("Sem.Color.Primary");
        _vibHandlePen = ThemeResources.GetPen("Sem.Color.OnSurface", 1.1);

        _vibratoBrushCacheIsDark = isDark;
        _vibratoBrushCacheValid = true;
    }

    private void RenderVibratoOverlay(VibratoOverlayLayout layout, DrawingContext context)
    {
        if (ViewModel == null || Part == null)
        {
            return;
        }

        Rect noteRect = layout.NoteRect;
        if (noteRect.Right < -24 || noteRect.Left > Bounds.Width + 24)
        {
            return;
        }

        // Ensure brush/pen cache is valid (rebuild on first use or when theme changes)
        bool isDark = ThemeResources.IsDarkMode;
        if (!_vibratoBrushCacheValid || _vibratoBrushCacheIsDark != isDark)
        {
            RebuildVibratoBrushCache(isDark);
        }

        // Only clone vibrato when it is disabled (needs a temporary length override for preview)
        UVibrato previewVibrato;
        if (layout.IsEnabled)
        {
            previewVibrato = layout.Note.vibrato;
        }
        else
        {
            previewVibrato = layout.Note.vibrato.Clone();
            previewVibrato.length = NotePresets.Default.DefaultVibrato.VibratoLength;
        }

        double waveWidth = Math.Max(layout.WaveEndX - layout.WaveStartX, 1);
        double activeDurationMs = Math.Max(layout.Note.DurationMs * previewVibrato.length / 100.0, 1.0);
        int sampleCount = Math.Clamp(
            (int)Math.Ceiling(Math.Max(waveWidth / 3.0, activeDurationMs / Math.Max(previewVibrato.period, 5f) * 24.0)),
            24,
            360);
        float nPeriod = (float)(previewVibrato.period / Math.Max(layout.Note.DurationMs, 1.0));
        float nStart = previewVibrato.NormalizedStart;

        // Select enabled/disabled brush variants from cache
        bool isEnabled = layout.IsEnabled;
        IBrush noteFillBrush = _vibNoteFillBrush!;
        // IBrush fineGuideBrush = isEnabled ? _vibFineGuideBrushEnabled! : _vibFineGuideBrushDisabled!;
        IBrush trackFillBrush = _vibTrackFillBrush!;
        IBrush trackActiveFillBrush = _vibTrackActiveFillBrush!;
        IPen wavePen = isEnabled ? _vibWavePenEnabled! : _vibWavePenDisabled!;
        IPen guidePen = isEnabled ? _vibGuidePenEnabled! : _vibGuidePenDisabled!;
        IPen fineGuidePen = isEnabled ? _vibFineGuidePenEnabled! : _vibFineGuidePenDisabled!;
        IPen trackBorderPen = _vibTrackBorderPen!;

        if (isEnabled)
        {
            context.DrawRectangle(
                noteFillBrush,
                null,
                new Rect(layout.WaveStartX, noteRect.Y + 1, layout.WaveEndX - layout.WaveStartX, Math.Max(noteRect.Height - 2, 4)),
                radiusX: 5,
                radiusY: 5);
        }
        else
        {
            context.DrawRectangle(
                null,
                fineGuidePen,
                new Rect(layout.WaveStartX, noteRect.Y + 1, layout.WaveEndX - layout.WaveStartX, Math.Max(noteRect.Height - 2, 4)),
                radiusX: 5,
                radiusY: 5);
        }

        _tmpPoints.Clear();
        for (int i = 0; i <= sampleCount; i++)
        {
            double progress = (double)i / sampleCount;
            float nPos = nStart + (1f - nStart) * (float)progress;
            Vector2 point = previewVibrato.Evaluate(nPos, nPeriod, layout.Note);
            double screenX = layout.WaveStartX + waveWidth * progress;
            double screenY = ViewModel.TickPitchToPoint(0, point.Y - 0.5).Y;
            _tmpPoints.Add(new Point(screenX, screenY));
        }

        _tmpPolylineGeometry.Points = _tmpPoints;
        context.DrawGeometry(null, wavePen, _tmpPolylineGeometry);

        Point startStemTop = new(layout.WaveStartX, noteRect.Top + 2);
        double startCapWidth = Math.Clamp(noteRect.Height * 0.36, 8, 12);
        context.DrawLine(guidePen, startStemTop, layout.GuideStartPoint);
        context.DrawLine(fineGuidePen, startStemTop, new Point(startStemTop.X + startCapWidth, startStemTop.Y));
        context.DrawLine(guidePen, layout.GuideStartPoint, layout.FadeInPoint);
        context.DrawLine(guidePen, layout.FadeInPoint, layout.FadeOutPoint);
        context.DrawLine(guidePen, layout.FadeOutPoint, layout.GuideEndPoint);
        context.DrawLine(fineGuidePen, layout.GuideStartPoint, new Point(layout.WaveStartX, layout.WaveCenterY));
        context.DrawLine(fineGuidePen, layout.DriftAnchorPoint, layout.DriftHandlePoint);

        double activeTrackWidth = Math.Max(layout.PhaseHandleRect.Center.X - layout.PhaseTrackRect.Left, 0);
        context.DrawRectangle(
            trackFillBrush,
            trackBorderPen,
            layout.PhaseTrackRect,
            radiusX: layout.PhaseTrackRect.Height * 0.5,
            radiusY: layout.PhaseTrackRect.Height * 0.5);
        if (isEnabled && activeTrackWidth > 0)
        {
            context.DrawRectangle(
                trackActiveFillBrush,
                null,
                new Rect(layout.PhaseTrackRect.X, layout.PhaseTrackRect.Y, activeTrackWidth, layout.PhaseTrackRect.Height),
                radiusX: layout.PhaseTrackRect.Height * 0.5,
                radiusY: layout.PhaseTrackRect.Height * 0.5);
        }

        DrawVibratoHandle(context, layout.FadeInHandleRect, isEnabled, _vibHandleHaloBrush!, _vibHandleBrush!, _vibHandlePen!);
        DrawVibratoHandle(context, layout.FadeOutHandleRect, isEnabled, _vibHandleHaloBrush!, _vibHandleBrush!, _vibHandlePen!);
        DrawVibratoHandle(context, layout.PhaseHandleRect, isEnabled, _vibHandleHaloBrush!, _vibHandleBrush!, _vibHandlePen!);
        DrawVibratoHandle(context, layout.DriftHandleRect, isEnabled, _vibHandleHaloBrush!, _vibHandleBrush!, _vibHandlePen!);
    }

    private static void DrawVibratoHandle(DrawingContext context, Rect rect, bool isEnabled, IBrush haloBrush, IBrush handleBrush, IPen handlePen)
    {
        Point center = rect.Center;
        double radiusX = rect.Width * 0.5;
        double radiusY = rect.Height * 0.5;
        using (context.PushOpacity(isEnabled ? 1.0 : 0.45))
        {
            context.DrawEllipse(
                haloBrush,
                null,
                center,
                radiusX + 1.5,
                radiusY + 1.5);

            context.DrawEllipse(
                handleBrush,
                handlePen,
                center,
                radiusX,
                radiusY);
        }
    }

    /// <summary>
    /// 触点指示器
    /// </summary>
    private void RenderPitchDrawIndicator(DrawingContext context)
    {
        if (ViewModel?.EditMode != PianoRollEditMode.PitchPen || !ViewModel.IsPitchDrawingActive ||
            ViewModel.PitchDrawPointer == null)
        {
            return;
        }

        Point center = ViewModel.PitchDrawPointer.Value;

        const double outerHaloRadius = 12.0; // 外部光晕
        const double strokeRingRadius = 7.0; // 轮廓环
        const double corePointRadius = 2.5; // 核心精准点
        const double strokeThickness = 1.5; // 极细线条

        // 1. 绘制底层软光晕
        using (context.PushOpacity(0.15))
        {
            context.DrawEllipse(ThemeResources.GetBrush("Sem.Color.Primary"), null, center, outerHaloRadius,
                outerHaloRadius);
        }

        // 2. 绘制中间空心圆环
        Pen ringPen = new Pen(ThemeResources.GetBrush("Sem.Color.Primary"), strokeThickness);
        using (context.PushOpacity(0.6))
        {
            context.DrawEllipse(null, ringPen, center, strokeRingRadius, strokeRingRadius);
        }

        // 3. 绘制核心定位点
        context.DrawEllipse(ThemeResources.GetBrush("Sem.Color.OnSurface"), null, center, corePointRadius,
            corePointRadius);
    }

    /// <summary>
    /// 绘制多选模式中的选区范围可视化层。
    /// 选区范围：从 BeginSelectionTick 到 PlayPosTick（以绝对 tick 为基准）。
    /// </summary>
    private void RenderSelectingRangeOverlay(DrawingContext context)
    {
        if (ViewModel == null || Part == null)
        {
            return;
        }

        // 计算选区区间
        int beginAbsTick = Part.position + ViewModel.BeginSelectionTick;
        int endAbsTick = (int)(TickOffset + ViewModel.PlayMarkerScreenX / TickWidth);

        // 交换确保 begin < end
        if (beginAbsTick > endAbsTick)
        {
            (beginAbsTick, endAbsTick) = (endAbsTick, beginAbsTick);
        }

        // 裁剪到分片范围
        int partStart = Part.position;
        int partEnd = Part.End;
        beginAbsTick = Math.Max(beginAbsTick, partStart);
        endAbsTick = Math.Min(endAbsTick, partEnd);

        if (beginAbsTick >= endAbsTick)
        {
            return; // 选区完全在分片外或为零宽度
        }

        // 转换为屏幕坐标
        double screenX1 = ViewModel.TickToPointX(beginAbsTick);
        double screenX2 = ViewModel.TickToPointX(endAbsTick);

        // 裁剪到屏幕范围
        double screenLeft = Math.Max(Math.Min(screenX1, screenX2), 0);
        double screenRight = Math.Min(Math.Max(screenX1, screenX2), Bounds.Width);

        if (screenLeft >= screenRight || screenRight < 0 || screenLeft > Bounds.Width)
        {
            return; // 选区完全不可见
        }

        // 绘制选区填充
        Rect selectionRect = new(screenLeft, 0, screenRight - screenLeft, Bounds.Height);
        using (context.PushOpacity(0.12))
        {
            context.DrawRectangle(
                ThemeResources.GetBrush("Sem.Color.PrimaryContainer"),
                null,
                selectionRect);
        }

        // 绘制左右边界线
        IPen boundaryPen = ThemeResources.GetPen("Sem.Color.Primary", 2.0);
        context.DrawLine(boundaryPen, new Point(screenLeft, 0), new Point(screenLeft, Bounds.Height));
        context.DrawLine(boundaryPen, new Point(screenRight, 0), new Point(screenRight, Bounds.Height));

        // 顶部亮线
        const double topLineHeight = 3.0;
        using (context.PushOpacity(0.35))
        {
            context.DrawRectangle(
                ThemeResources.GetBrush("Sem.Color.Primary"),
                null,
                new Rect(screenLeft, 0, screenRight - screenLeft, topLineHeight));
        }
    }

    /// <summary>
    /// 绘制不可编辑区域遮罩（分片外区域）。使用半透明底色叠加斜向线条提升识别度。
    /// </summary>
    /// <param name="context"></param>
    /// <param name="rect"></param>
    private static void DrawNonEditableMask(DrawingContext context, Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using (context.PushOpacity(0.18))
        {
            context.DrawRectangle(ThemeResources.GetBrush("Sem.Color.PrimaryContainer"), null, rect);
        }

        IPen stripePen = ThemeResources.GetPen("Sem.Color.OnSurfaceVariant", 2);
        const double stripeGap = 14;

        using (context.PushClip(rect))
        {
            double startX = rect.X - rect.Height;
            double endX = rect.Right + rect.Height;

            using (context.PushOpacity(0.20))
            {
                for (double x = startX; x < endX; x += stripeGap)
                {
                    context.DrawLine(stripePen, new Point(x, rect.Bottom), new Point(x + rect.Height, rect.Top));
                }
            }

            using (context.PushOpacity(0.10))
            {
                for (double x = startX + stripeGap * 0.5; x < endX; x += stripeGap)
                {
                    context.DrawLine(stripePen, new Point(x, rect.Bottom), new Point(x + rect.Height, rect.Top));
                }
            }
        }
    }

    #endregion

    #region 输入事件处理（转发给 GestureInterpreter）

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        ViewModel?.Gesture.OnPointerPressed(e, this);
        if (ViewConstants.EnableBenchMarkTest)
        {
            _fpsStopwatch.Restart();
            _frameCount = 0;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        ViewModel?.Gesture.OnPointerMoved(e, this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        ViewModel?.Gesture.OnPointerReleased(e, this);
        if (ViewConstants.EnableBenchMarkTest)
        {
            _fpsStopwatch.Stop();
            double elapsedSeconds = _fpsStopwatch.Elapsed.TotalSeconds;
            if (elapsedSeconds > 0)
            {
                double fps = _frameCount / elapsedSeconds;
                Debug.WriteLine($"拖拽滚动平均帧率: {fps:F2} FPS over {elapsedSeconds:F2} seconds.");
            }
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        ViewModel?.Gesture.OnPointerCancelled(e, this);
    }

    #endregion

    public void OnNext(UCommand cmd, bool isUndo)
    {
        switch (cmd)
        {
            case NoteCommand:
            case PartCommand:
            case SetCurveCommand:
            case PhonemizedNotification:
            case PitchExpCommand: // 音高控制点增删改时刷新画布
                InvalidateVisual();
                break;
        }
    }
}