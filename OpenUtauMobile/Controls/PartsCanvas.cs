using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using DynamicData.Binding;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Controls.Gestures;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime.Generation;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime.Resources;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 编曲区分片画布。负责绘制所有 UPart（音符缩略图 / 波形缩略图 / 名称标签），
/// 并通过 ICmdSubscriber 订阅 DocManager 命令以维护内部绘制缓存的一致性。
/// </summary>
public partial class PartsCanvas : Control, ICmdSubscriber
{
    #region Avalonia 属性

    // 变换属性：像素/Tick (横向缩放)
    public static readonly StyledProperty<double> TickWidthProperty =
        AvaloniaProperty.Register<PartsCanvas, double>(nameof(TickWidth), 0.1);

    // 变换属性：轨道高度 (纵向缩放/固定高度)
    public static readonly StyledProperty<double> TrackHeightProperty =
        AvaloniaProperty.Register<PartsCanvas, double>(nameof(TrackHeight), 50.0);

    // 变换属性：X轴偏移 (平移/滚动)
    public static readonly StyledProperty<double> TickOffsetProperty =
        AvaloniaProperty.Register<PartsCanvas, double>(nameof(TickOffset));

    // 变换属性：Y轴偏移 (平移/滚动)
    public static readonly StyledProperty<double> TrackOffsetProperty =
        AvaloniaProperty.Register<PartsCanvas, double>(nameof(TrackOffset));

    // 选中的 Parts 列表
    public static readonly StyledProperty<ObservableCollectionExtended<UPart>> SelectedPartsProperty =
        AvaloniaProperty.Register<PartsCanvas, ObservableCollectionExtended<UPart>>(nameof(SelectedParts));

    // 编辑模式
    public static readonly StyledProperty<TrackEditMode> TrackEditModeProperty =
        AvaloniaProperty.Register<PartsCanvas, TrackEditMode>(nameof(TrackEditMode));

    public double TickWidth
    {
        get => GetValue(TickWidthProperty);
        set => SetValue(TickWidthProperty, value);
    }

    public double TrackHeight
    {
        get => GetValue(TrackHeightProperty);
        set => SetValue(TrackHeightProperty, value);
    }

    public double TickOffset
    {
        get => GetValue(TickOffsetProperty);
        set => SetValue(TickOffsetProperty, value);
    }

    public double TrackOffset
    {
        get => GetValue(TrackOffsetProperty);
        set => SetValue(TrackOffsetProperty, value);
    }

    public ObservableCollectionExtended<UPart> SelectedParts
    {
        get => GetValue(SelectedPartsProperty);
        set => SetValue(SelectedPartsProperty, value);
    }

    public TrackEditMode TrackEditMode
    {
        get => GetValue(TrackEditModeProperty);
        set => SetValue(TrackEditModeProperty, value);
    }

    #endregion

    #region 静态绘制资源

    // 名称标签字号（px）
    private const double LabelFontSize = 11;

    private const double PartCornerRadius = 6;

    private const double LabelHorizontalPadding = 3;
    private const double LabelVerticalPadding = 2;

    #endregion

    #region 内部缓存结构

    /// <summary>
    /// UVoicePart 的音符 tone 范围缓存。
    /// 仅与 part 内容相关，视口变化不使其失效。
    /// </summary>
    private struct ToneRangeEntry
    {
        public int MinTone;
        public int MaxTone;
    }

    private readonly Dictionary<UVoicePart, ToneRangeEntry>
        _toneRangeCache = []; // 缓存 UVoicePart 的音符 tone 范围，避免每帧扫描所有音符计算范围

    /// <summary>
    /// UWavePart 的波形位图缓存。
    /// 视口（TickOffset / TickWidth / TrackHeight）变化时标记为脏，下帧重绘。
    /// </summary>
    private sealed class WaveCache : IDisposable
    {
        public WriteableBitmap? Bitmap;

        // 与 Bitmap 同尺寸的 int[] 像素缓冲，复用以减少 GC 压力
        public int[] PixelData = [];
        public bool IsDirty = true;

        public int PartPosition;
        public int PartDuration;
        public int PeakColor;

        public void Dispose()
        {
            Bitmap?.Dispose();
            Bitmap = null;
        }
    }

    private readonly Dictionary<UWavePart, WaveCache> _waveCache = [];
    private readonly HashSet<UWavePart> _pendingPeakLoads = [];

    /// <summary>每个选中分片只缓存当前宽度的省略文本，避免滚动时反复排版或积累历史宽度。</summary>
    private sealed record SelectedLabelEntry(string Text, IBrush Brush, double MaxWidth, TextLayout Layout);

    private readonly Dictionary<UPart, SelectedLabelEntry> _selectedLabelCache = [];

    /// <summary>每个分片只保留当前标签尺寸的像素坐标轮廓，宽度变化时重建，避免逐帧分配。</summary>
    private readonly Dictionary<UPart, (Size Size, StreamGeometry Geometry)> _selectedLabelGeometryCache = [];

    /// <summary>释放随选区或画布生命周期结束的标签排版和轮廓缓存。</summary>
    private void ClearSelectedLabelCache()
    {
        foreach (SelectedLabelEntry entry in _selectedLabelCache.Values) entry.Layout.Dispose();
        _selectedLabelCache.Clear();
        _selectedLabelGeometryCache.Clear();
    }

    #endregion

    #region 手势解释器

    private readonly GestureInterpreter _gesture = new();
    private EditorViewModel? _boundViewModel;
    private bool _isAttached;

    #endregion

    // 静态构造
    static PartsCanvas()
    {
        // 以下属性变化时触发重绘
        AffectsRender<PartsCanvas>(
            TickWidthProperty, TrackHeightProperty,
            TickOffsetProperty, TrackOffsetProperty, SelectedPartsProperty);
    }

    #region 生命周期

    protected override void OnDataContextChanged(EventArgs e)
    {
        EndDesktopPointer(true);
        base.OnDataContextChanged(e);

        // 订阅只在可见树中有效；导航重建视图不能留下指向旧画布的事件。
        if (_boundViewModel != null) _boundViewModel.RequestInvalidateVisual -= InvalidateVisual;
        _boundViewModel = DataContext as EditorViewModel;
        DocManager.Inst.RemoveSubscriber(this);
        if (_isAttached && _boundViewModel != null)
        {
            DocManager.Inst.AddSubscriber(this);
            _boundViewModel.RequestInvalidateVisual += InvalidateVisual;
        }

        if (_boundViewModel is not { } vm)
        {
            _gesture.Tap = _gesture.DoubleTap = _gesture.DragBegin = null;
            _gesture.DragUpdate = null;
            _gesture.DragEnd = null;
            _gesture.PinchUpdate = null;
            _gesture.TwoFingerTap = _gesture.ThreeFingerTap = null;
            return;
        }

        _gesture.Tap = pt => vm.OnGestureTap(pt);
        _gesture.DoubleTap = pt => vm.OnGestureDoubleTap(pt);
        _gesture.DragBegin = start => vm.OnGestureDragBegin(start);
        _gesture.DragUpdate = (_, step, total, current, ts) => vm.OnGestureDragUpdate(current, step, total, ts);
        _gesture.DragEnd = (end, _, ts) => vm.OnGestureDragEnd(end, ts);
        _gesture.PinchUpdate = (scaleX, scaleY, center, panDelta) =>
            vm.OnGesturePinchUpdate(scaleX, scaleY, center, panDelta);
        _gesture.TwoFingerTap = () => vm.OnTwoFingerTap();
        _gesture.ThreeFingerTap = () => vm.OnThreeFingerTap();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (DataContext is EditorViewModel vm)
            vm.OnTrackAreaSizeChanged(e.NewSize.Width, e.NewSize.Height);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        DocManager.Inst.RemoveSubscriber(this);
        if (_boundViewModel != null)
        {
            DocManager.Inst.AddSubscriber(this);
            _boundViewModel.RequestInvalidateVisual -= InvalidateVisual;
            _boundViewModel.RequestInvalidateVisual += InvalidateVisual;
        }
        if (SelectedParts != null)
        {
            SelectedParts.CollectionChanged -= OnCollectionChanged;
            SelectedParts.CollectionChanged += OnCollectionChanged;
        }
        foreach (UPart part in DocManager.Inst.Project.parts) RegisterPeaksCallback(part);
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        EndDesktopPointer(true);
        _isAttached = false;
        if (_boundViewModel != null) _boundViewModel.RequestInvalidateVisual -= InvalidateVisual;
        if (SelectedParts != null) SelectedParts.CollectionChanged -= OnCollectionChanged;
        base.OnDetachedFromVisualTree(e);
        DocManager.Inst.RemoveSubscriber(this);
        foreach (WaveCache c in _waveCache.Values) c.Dispose();
        _waveCache.Clear();
        _pendingPeakLoads.Clear();
        ClearSelectedLabelCache();
        _toneRangeCache.Clear();
    }

    #endregion

    #region 属性/集合变更

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedPartsProperty)
        {
            ClearSelectedLabelCache();
            // 绑定/解绑集合变更事件
            if (change.OldValue is INotifyCollectionChanged oldList)
                oldList.CollectionChanged -= OnCollectionChanged;
            if (_isAttached && change.NewValue is INotifyCollectionChanged newList)
                newList.CollectionChanged += OnCollectionChanged;

            InvalidateVisual();
        }

        // 视口参数变化时将所有波形缓存标记为脏
        if (change.Property == TickOffsetProperty ||
            change.Property == TickWidthProperty ||
            change.Property == TrackHeightProperty)
        {
            foreach (WaveCache c in _waveCache.Values) c.IsDirty = true;
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ClearSelectedLabelCache();
        if (e.OldItems != null)
        {
            foreach (object? item in e.OldItems)
            {
                if (item is UVoicePart vp)
                    _toneRangeCache.Remove(vp);
                if (item is UWavePart wp && _waveCache.Remove(wp, out WaveCache? cache))
                    cache.Dispose();
            }
        }

        if (e.NewItems != null)
            foreach (object? item in e.NewItems)
                RegisterPeaksCallback(item);

        InvalidateVisual();
    }

    /// <summary>
    /// 为尚未加载完成的 UWavePart 注册 Peaks Task 回调，
    /// 加载成功后在 UI 线程标记缓存脏并触发重绘。
    /// </summary>
    private void RegisterPeaksCallback(object? item)
    {
        if (item is not UWavePart wp) return;
        if (wp.Peaks.IsCompleted) return;
        if (!_pendingPeakLoads.Add(wp)) return;

        wp.Peaks.ContinueWith(_ =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _pendingPeakLoads.Remove(wp);
                if (_waveCache.TryGetValue(wp, out WaveCache? cache))
                    cache.IsDirty = true;
                InvalidateVisual();
            });
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    #endregion

    #region DocManager 命令订阅

    /// <summary>
    /// 接收 DocManager 命令通知。
    /// 任意 NoteCommand 完成后，在 UI 线程使对应 part 的 tone 范围缓存失效。
    /// </summary>
    public void OnNext(UCommand cmd, bool isUndo)
    {
        switch (cmd)
        {
            case LoadProjectNotification:
                ClearSelectedLabelCache();
                foreach (UPart part in DocManager.Inst.Project.parts)
                    RegisterPeaksCallback(part);
                InvalidateVisual();
                break;
            case TrackCommand:
                InvalidateVisual();
                break;
            case PartCommand { part: UWavePart wp }:
                RegisterPeaksCallback(wp);
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_waveCache.TryGetValue(wp, out WaveCache? wc))
                        wc.IsDirty = true;
                    InvalidateVisual();
                });
                break;
            case NoteCommand noteCmd:
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    _toneRangeCache.Remove(noteCmd.Part);
                    InvalidateVisual();
                });
                break;
        }
    }

    #endregion

    #region 绘图

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        List<UPart> parts = DocManager.Inst.Project.parts;
        // 透明矩形确保控件能接收指针事件
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));

        foreach (UPart part in parts)
        {
            double x = (part.position - TickOffset) * TickWidth;
            double y = part.trackNo * TrackHeight - TrackOffset;
            double width = part.Duration * TickWidth;
            double height = TrackHeight;

            // 完全在视口外：跳过，不进行任何绘制
            if (x + width < 0 || x > Bounds.Width) continue;
            if (y + height < 0 || y > Bounds.Height) continue;

            bool selected = SelectedParts != null && SelectedParts.Contains(part); // 是否选中
            PartLayout layout = PartLayout.Calculate(x, y, width, height, selected, Bounds.Width);
            Rect rect = layout.Body;
            if (rect.Width <= 0 || rect.Height <= 0) continue;
            SolidColorBrush brush = TrackPalette.GetTrackColor(DocManager.Inst.Project.tracks[part.trackNo].TrackColor)
                .AccentColor;

            // 1. 背景色块 + 边框
            using (context.PushOpacity(selected ? 1 : 0.64))
            {
                if (selected)
                    context.DrawRectangle(ThemeResources.GetBrush("Sem.Color.Primary"), null, layout.Frame,
                        radiusX: PartCornerRadius, radiusY: PartCornerRadius);
                if (layout.Fill.Width > 0)
                    context.DrawRectangle(brush, null, layout.Fill,
                        radiusX: PartCornerRadius, radiusY: PartCornerRadius);
            }

            // 2. 缩略图按完整主体绘制在宽侧边框之上，两侧的音符和波形不被背景内缩裁掉。
            using (context.PushClip(new RoundedRect(layout.Body, PartCornerRadius)))
            {
                switch (part)
                {
                    case UVoicePart { notes.Count: > 0 } voicePart:
                        DrawNotePreview(context, voicePart, rect);
                        break;
                    case UWavePart wavePart:
                        DrawWaveformCached(context, wavePart, rect);
                        break;
                }

                if (!selected) DrawPartLabel(context, part, rect, brush);
            }
        }
        // 选中边框附件统一置于分片内容之上；手柄最后绘制，与输入优先级保持一致。
        foreach (UPart part in parts)
        {
            if (SelectedParts == null || !SelectedParts.Contains(part)) continue;
            PartLayout layout = PartLayout.Calculate(
                (part.position - TickOffset) * TickWidth, part.trackNo * TrackHeight - TrackOffset,
                part.Duration * TickWidth, TrackHeight, true, Bounds.Width);
            if (layout.Body.Width <= 0 || layout.Body.Height <= 0 ||
                layout.Frame.Bottom <= 0 || layout.Frame.Top >= Bounds.Height) continue;
            DrawSelectedPartLabel(context, part, layout);
            DrawMoveHandle(context, layout);
            DrawResizeGripLines(context, layout);
        }
        if (_desktopBox is { } box) context.DrawRectangle(null, ThemeResources.GetPen("Sem.Color.Primary", 1), box);
    }

    /// <summary>
    /// 按实际 dp 尺寸创建左侧页签：左下角为直角，右侧为固定 45° 斜边。
    /// 斜边宽度仅由高度决定，不再将单位轮廓横向拉伸；右侧 move 仅水平镜像。
    /// </summary>
    /// <param name="width">完整页签宽度，包含斜边占用的宽度。</param>
    /// <param name="height">页签高度，同时决定 45° 斜边的横向宽度。</param>
    private static StreamGeometry CreateTabGeometry(double width, double height)
    {
        StreamGeometry geometry = new();
        using StreamGeometryContext path = geometry.Open();
        path.BeginFigure(new Point(0, 0), true);
        path.LineTo(new Point(width, 0));
        path.LineTo(new Point(width - PartLayout.GetTabSlopeWidth(height), height));
        path.LineTo(new Point(0, height));
        path.EndFigure(true);
        return geometry;
    }

    private static readonly StreamGeometry MoveTabGeometry = CreateTabGeometry(PartLayout.MoveWidth, PartLayout.MoveHeight);

    /// <summary>复用当前分片标签的实际尺寸轮廓，保证标签长度变化时斜率不变。</summary>
    private StreamGeometry GetLabelTabGeometry(UPart part, Size size)
    {
        if (_selectedLabelGeometryCache.TryGetValue(part, out var cached) && cached.Size == size)
            return cached.Geometry;
        StreamGeometry geometry = CreateTabGeometry(size.Width, size.Height);
        _selectedLabelGeometryCache[part] = (size, geometry);
        return geometry;
    }

    /// <summary>用给定坐标变换绘制页签轮廓，使用边框的 Primary 色。</summary>
    private static void DrawBorderHandle(DrawingContext context, StreamGeometry geometry, Matrix transform)
    {
        using (context.PushTransform(transform))
            context.DrawGeometry(ThemeResources.GetBrush("Sem.Color.Primary"), null, geometry);
    }

    /// <summary>
    /// 裁剪绘制顶部移动手柄，保证窄分片和离屏分片不出现外溢。
    /// </summary>
    private void DrawMoveHandle(DrawingContext context, PartLayout layout)
    {
        if (layout.MoveClip.Width <= 0 || layout.MoveClip.Height <= 0 ||
            layout.MoveClip.Bottom <= 0 || layout.MoveClip.Top >= Bounds.Height) return;
        using (context.PushClip(layout.MoveClip))
        {
            // 只镜像和平移完整轮廓，窄分片通过 MoveClip 裁剪，斜边不参与横向拉伸。
            DrawBorderHandle(context, MoveTabGeometry, new Matrix(-1, 0, 0, 1,
                layout.MoveHandle.Right, layout.MoveHandle.Top));
            // 只在斜坡右侧的平直区域居中绘制横线，裁剪仍使用真实可见范围。
            double slopeWidth = PartLayout.GetTabSlopeWidth(layout.MoveHandle.Height);
            DrawGripLines(context, new Rect(layout.MoveHandle.Left + slopeWidth, layout.MoveHandle.Top,
                layout.MoveHandle.Width - slopeWidth, layout.MoveHandle.Height), horizontal: true);
        }
    }

    /// <summary>在指定范围内绘制两条 OnPrimary 手柄线；顶部为横线，两侧为竖线。</summary>
    private static void DrawGripLines(DrawingContext context, Rect bounds, bool horizontal)
    {
        double along = horizontal ? bounds.Width : bounds.Height;
        double across = horizontal ? bounds.Height : bounds.Width;
        if (along <= 2 || across <= 2) return;
        double length = Math.Min(24, along * 0.5);
        double halfGap = Math.Min(2, across / 4);
        Point center = bounds.Center;
        IPen pen = ThemeResources.GetPen("Sem.Color.OnPrimary", 1.5);
        if (horizontal)
        {
            context.DrawLine(pen, new Point(center.X - length / 2, center.Y - halfGap),
                new Point(center.X + length / 2, center.Y - halfGap));
            context.DrawLine(pen, new Point(center.X - length / 2, center.Y + halfGap),
                new Point(center.X + length / 2, center.Y + halfGap));
        }
        else
        {
            context.DrawLine(pen, new Point(center.X - halfGap, center.Y - length / 2),
                new Point(center.X - halfGap, center.Y + length / 2));
            context.DrawLine(pen, new Point(center.X + halfGap, center.Y - length / 2),
                new Point(center.X + halfGap, center.Y + length / 2));
        }
    }

    /// <summary>在左右宽边框内绘制竖向抓握线，以整个轨道的 Frame 中心为竖直中心。</summary>
    private static void DrawResizeGripLines(DrawingContext context, PartLayout layout)
    {
        using (context.PushClip(new RoundedRect(layout.Frame, PartCornerRadius)))
        {
            DrawGripLines(context, new Rect(layout.Frame.Left, layout.Frame.Top, PartLayout.SideBorder,
                layout.Frame.Height), horizontal: false);
            DrawGripLines(context, new Rect(layout.Frame.Right - PartLayout.SideBorder, layout.Frame.Top,
                PartLayout.SideBorder, layout.Frame.Height), horizontal: false);
        }
    }

    /// <summary>
    /// 在 part 矩形左上角绘制名称标签。
    /// </summary>
    private void DrawPartLabel(DrawingContext context, UPart part, Rect rect, IBrush partBrush)
    {
        TextLayout textLayout = TextLayoutCache.Get(part.DisplayName, ThemeResources.GetBrush("Sem.Color.OnSurface"),
            LabelFontSize);
        double labelLeft = Math.Max(rect.Left + LabelHorizontalPadding, LabelHorizontalPadding);
        double labelRight = Math.Min(rect.Right - LabelHorizontalPadding, Bounds.Width - LabelHorizontalPadding);

        if (labelLeft + textLayout.Width > labelRight)
        {
            return;
        }

        using (context.PushClip(new RoundedRect(rect, PartCornerRadius)))
        using (context.PushTransform(Matrix.CreateTranslation(labelLeft, rect.Y + LabelVerticalPadding)))
        {
            using (context.PushOpacity(0.6))
            {
                // 半透明色块遮住缩略图，使文字清晰可读
                context.DrawRectangle(
                    partBrush,
                    null,
                    new Rect(0, 0, textLayout.Width, textLayout.Height));
            }

            textLayout.Draw(context, new Point());
        }
    }

    /// <summary>获取宽度受限的单行页签文本；空间不足使用省略号，不改变字号或手柄位置。</summary>
    private TextLayout GetSelectedLabelLayout(UPart part, double maxWidth)
    {
        IBrush brush = ThemeResources.GetBrush("Sem.Color.OnPrimary");
        TextLayout full = TextLayoutCache.Get(part.DisplayName, brush, LabelFontSize);
        if (full.Width <= maxWidth) return full;
        if (_selectedLabelCache.TryGetValue(part, out SelectedLabelEntry? cached))
        {
            if (cached.Text == part.DisplayName && ReferenceEquals(cached.Brush, brush) && cached.MaxWidth == maxWidth)
                return cached.Layout;
            cached.Layout.Dispose();
        }
        TextLayout trimmed = new(part.DisplayName, new Typeface(FontFamily.Default), LabelFontSize, brush,
            textWrapping: TextWrapping.NoWrap, textTrimming: TextTrimming.CharacterEllipsis,
            maxWidth: maxWidth, maxLines: 1);
        _selectedLabelCache[part] = new SelectedLabelEntry(part.DisplayName, brush, maxWidth, trimmed);
        return trimmed;
    }

    /// <summary>在顶边内侧绘制 Primary 页签，文字使用配套 OnPrimary，标签先于手柄让出横向空间。</summary>
    private void DrawSelectedPartLabel(DrawingContext context, UPart part, PartLayout layout)
    {
        if (layout.LabelSlot.Width <= 0 || layout.LabelSlot.Height <= 0) return;
        // 先根据文字行高确定页签高度，再为固定 45° 斜边预留同等宽度。
        TextLayout full = TextLayoutCache.Get(part.DisplayName, ThemeResources.GetBrush("Sem.Color.OnPrimary"), LabelFontSize);
        double tabHeight = Math.Min(layout.LabelSlot.Height, full.Height + LabelVerticalPadding * 2);
        double slopeWidth = PartLayout.GetTabSlopeWidth(tabHeight);
        double maxTextWidth = layout.LabelSlot.Width - slopeWidth - LabelHorizontalPadding * 2;
        if (maxTextWidth < LabelFontSize || layout.LabelSlot.Height <= 0) return;
        TextLayout text = GetSelectedLabelLayout(part, maxTextWidth);
        // 左边缘和左下角保持直角；右侧留出斜坡并接回顶边，宽度随实际文字收缩。
        Rect tab = new(layout.LabelSlot.Left, layout.LabelSlot.Top,
            Math.Min(layout.LabelSlot.Width, text.Width + LabelHorizontalPadding * 2 + slopeWidth), tabHeight);
        using (context.PushClip(new RoundedRect(layout.Frame, PartCornerRadius)))
        using (context.PushClip(tab))
        {
            DrawBorderHandle(context, GetLabelTabGeometry(part, tab.Size), Matrix.CreateTranslation(tab.Left, tab.Top));
            text.Draw(context, new Point(tab.Left + LabelHorizontalPadding, layout.Body.Top + LabelVerticalPadding));
        }
    }

    /// <summary>
    /// 在 part 矩形内绘制音符线段缩略图。
    /// </summary>
    private void DrawNotePreview(DrawingContext context, UVoicePart part, Rect rect)
    {
        IPen notePen = ThemeResources.GetPen("Sem.Color.OnSurfaceVariant", 2);

        // 快速剪枝：检查 rect 是否在视口范围内完全可见
        if (rect.Right <= 0 || rect.Left >= Bounds.Width) return;
        if (rect.Bottom <= 0 || rect.Top >= Bounds.Height) return;
        // tone 范围：缓存命中直接用，miss 时全量扫描一次并写入
        if (!_toneRangeCache.TryGetValue(part, out ToneRangeEntry range))
        {
            int minTone = int.MaxValue, maxTone = int.MinValue;
            foreach (UNote n in part.notes)
            {
                if (n.tone < minTone) minTone = n.tone;
                if (n.tone > maxTone) maxTone = n.tone;
            }

            range = new ToneRangeEntry { MinTone = minTone, MaxTone = maxTone };
            _toneRangeCache[part] = range;
        }

        int lo = range.MinTone, hi = range.MaxTone;
        // 至少保持 52 个半音音域，防止单一音高的音符撑满全部高度
        if (hi - lo < 52)
        {
            int pad = (52 - (hi - lo)) / 2;
            lo -= pad;
            hi += pad;
        }

        int toneSpan = hi - lo;
        if (toneSpan <= 0) return;

        double scaleY = (rect.Height - 4) / toneSpan; // 上下各留 2px 内边距
        double partW = rect.Width;

        // 视口左边缘在 part 内部坐标系（x=0 = part.position）下对应的 tick
        int viewLeftTick = (int)(TickOffset - part.position);
        double viewLeftX = viewLeftTick * TickWidth;
        // 视口右边缘在 part 内部坐标系下的 x，用于提前终止有序音符遍历
        double viewRightX = (TickOffset + Bounds.Width / TickWidth - part.position) * TickWidth;

        using (context.PushClip(new RoundedRect(rect, PartCornerRadius)))
        using (context.PushTransform(Matrix.CreateTranslation(rect.X, rect.Y + 2)))
        {
            foreach (UNote note in part.notes)
            {
                double noteX1 = note.position * TickWidth;

                // SortedSet 按 position 升序；起点超出 part 右侧则后续全部超出
                if (noteX1 >= partW) break;
                // 音符起点超出视口右侧则后续全部不可见
                if (noteX1 >= viewRightX) break;

                double noteX2 = note.End * TickWidth;
                // 音符右端仍在视口左侧。
                if (noteX2 <= viewLeftX) continue;

                double noteY = (hi - note.tone) * scaleY;
                context.DrawLine(notePen, new Point(noteX1, noteY), new Point(noteX2, noteY));
            }
        }
    }

    // ── 波形缩略图（UWavePart）

    /// <summary>
    /// 绘制 UWavePart 的波形缩略图。
    /// </summary>
    private void DrawWaveformCached(DrawingContext context, UWavePart part, Rect rect)
    {
        // 快速剪枝：检查 rect 是否在视口范围内完全可见
        // 若 rect 完全在视口外，直接返回，避免后续缓存查询和绘制操作
        if (rect.Right <= 0 || rect.Left >= Bounds.Width) return;
        if (rect.Bottom <= 0 || rect.Top >= Bounds.Height) return;

        if (!part.Peaks.IsCompletedSuccessfully)
        {
            RegisterPeaksCallback(part);
            return;
        }
        // Peaks.Result 注解为 non-null，但 Load() 存在 Task.FromResult<>(null) 路径
        if (ReferenceEquals(part.Peaks.Result, null)) return;

        WaveCache cache = GetOrCreateWaveCache(part);
        int peakColor = PackRgba8888(ThemeResources.GetColor("Sem.Color.OnSurfaceVariant"));

        // 检测 part 属性变更（拖拽/裁剪等），自动标记脏
        if (!cache.IsDirty &&
            (cache.PartPosition != part.position ||
             cache.PartDuration != part.Duration ||
             cache.PeakColor != peakColor))
        {
            cache.IsDirty = true;
        }

        if (cache.IsDirty)
        {
            RedrawWaveCache(cache, part, peakColor);
            cache.IsDirty = false;
        }

        if (cache.Bitmap == null) return;

        double partXInView = (part.position - TickOffset) * TickWidth;
        int srcLeft = (int)Math.Max(0, partXInView);
        int srcRight = (int)Math.Min(cache.Bitmap.PixelSize.Width,
            Math.Min(rect.Right, Bounds.Width));
        if (srcRight <= srcLeft) return;

        Rect srcRect = new(srcLeft, 0, srcRight - srcLeft, cache.Bitmap.PixelSize.Height);
        Rect dstRect = new(srcLeft, rect.Y, srcRight - srcLeft, rect.Height);

        using (context.PushClip(new RoundedRect(rect, PartCornerRadius)))
        {
            context.DrawImage(cache.Bitmap, srcRect, dstRect);
        }
    }

    /// <summary>
    /// 取得或创建 <see cref="WaveCache"/>。
    /// 位图宽度基于视口宽度（而非 part 宽度），按 128px 步进扩容，高度等于 TrackHeight；尺寸不足时重建。
    /// </summary>
    private WaveCache GetOrCreateWaveCache(UWavePart part)
    {
        if (!_waveCache.TryGetValue(part, out WaveCache? cache))
        {
            cache = new WaveCache();
            _waveCache[part] = cache;
        }

        int neededW = 128 * ((int)(Bounds.Width / 128) + 1);
        int neededH = Math.Max(1, (int)TrackHeight);

        if (cache.Bitmap == null ||
            cache.Bitmap.PixelSize.Width < neededW ||
            cache.Bitmap.PixelSize.Height != neededH)
        {
            cache.Dispose();
            PixelSize size = new(neededW, neededH);
            cache.Bitmap = new WriteableBitmap(size, new Vector(96, 96),
                PixelFormat.Rgba8888,
                AlphaFormat.Unpremul);
            cache.PixelData = new int[size.Width * size.Height];
            cache.IsDirty = true;
        }

        return cache;
    }

    /// <summary>
    /// 将 Peaks 数据写入 <see cref="WaveCache.Bitmap"/>。
    /// </summary>
    private void RedrawWaveCache(WaveCache cache, UWavePart part, int peakColor)
    {
        WavePeaks peaks = part.Peaks.Result;
        WriteableBitmap bitmap = cache.Bitmap!;
        int[] data = cache.PixelData;
        int bmpW = bitmap.PixelSize.Width;
        int bmpH = bitmap.PixelSize.Height;
        int channelCount = peaks.Channels;

        Array.Clear(data, 0, data.Length);

        TimeAxis timeAxis = DocManager.Inst.Project.timeAxis;
        double partStartMs = timeAxis.TickPosToMsPos(part.position);
        double skipMs = part.GetSkipMs(DocManager.Inst.Project);

        // 振幅到像素的缩放系数（上下各留 2px 内边距）
        double monoChnlAmp = (bmpH - 4.0) / 2.0; // 单声道：全高居中
        double stereoChnlAmp = (bmpH - 6.0) / 4.0; // 双声道：各占上下一半

        // 计算绘制起始列（与桌面版 DrawWaveform 对齐）
        // 位图 x=0 对应视口左边缘（TickOffset）
        int x = 0;
        if (TickOffset <= part.position)
            x = (int)(TickWidth * (part.position - TickOffset));

        // 初始 tick 与样本索引
        int posTick = (int)(TickOffset + x / TickWidth);
        double posMs = timeAxis.TickPosToMsPos(posTick);
        int sampleIndex = Math.Clamp(
            (int)(peaks.SampleRate * (skipMs + posMs - partStartMs) * 0.001),
            0, peaks.Frames);

        float[] lastSMin = new float[channelCount];
        float[] lastSMax = new float[channelCount];
        bool hasPeak = false;

        while (x < bmpW)
        {
            // 超出 part 范围则停止
            if (posTick >= part.position + part.Duration)
                break;

            int nextPosTick = (int)(TickOffset + (x + 1) / TickWidth);
            double nextPosMs = timeAxis.TickPosToMsPos(nextPosTick);
            int nextSampleIndex = Math.Clamp(
                (int)(peaks.SampleRate * (skipMs + nextPosMs - partStartMs) * 0.001),
                0, peaks.Frames);

            if (nextSampleIndex > sampleIndex)
            {
                hasPeak = true;
                for (int ch = 0; ch < channelCount; ch++)
                {
                    peaks.MinMax(ch, sampleIndex, nextSampleIndex, out float sMin, out float sMax);
                    lastSMin[ch] = Math.Clamp(sMin, -1, 1);
                    lastSMax[ch] = Math.Clamp(sMax, -1, 1);
                }
            }
            else if (!hasPeak && posTick >= part.position && sampleIndex < peaks.Frames)
            {
                hasPeak = true;
                for (int ch = 0; ch < channelCount; ch++)
                {
                    peaks.MinMax(ch, sampleIndex, sampleIndex + 1, out float sMin, out float sMax);
                    lastSMin[ch] = Math.Clamp(sMin, -1, 1);
                    lastSMax[ch] = Math.Clamp(sMax, -1, 1);
                }
            }

            if (hasPeak)
            {
                for (int ch = 0; ch < channelCount; ch++)
                {
                    double ySpan = channelCount == 1 ? monoChnlAmp : stereoChnlAmp;
                    double yOffset = ch == 1 ? monoChnlAmp : 0.0;

                    int y1 = (int)(ySpan * (1.0 - lastSMin[ch]) + yOffset) + 2;
                    int y2 = (int)(ySpan * (1.0 - lastSMax[ch]) + yOffset) + 2;
                    DrawPeak(data, bmpW, bmpH, x, y1, y2, peakColor);
                }
            }

            x++;
            posTick = nextPosTick;
            posMs = nextPosMs;
            sampleIndex = nextSampleIndex;
        }

        using ILockedFramebuffer frameBuffer = bitmap.Lock();
        Marshal.Copy(data, 0, frameBuffer.Address, data.Length);

        cache.PartPosition = part.position;
        cache.PartDuration = part.Duration;
        cache.PeakColor = peakColor;
    }

    /// <summary>
    /// 在像素缓冲 <paramref name="data"/> 的第 <paramref name="x"/> 列，
    /// 从 y1 到 y2 写入主题颜色像素。
    /// </summary>
    private static void DrawPeak(int[] data, int width, int height, int x, int y1, int y2, int peakColor)
    {
        if (y1 > y2)
        {
            (y1, y2) = (y2, y1);
        }

        y1 = Math.Clamp(y1, 0, height - 1);
        y2 = Math.Clamp(y2, 0, height - 1);
        for (int y = y1; y <= y2; y++)
        {
            data[x + width * y] = peakColor;
        }
    }

    /// <summary>
    /// 将 Avalonia 颜色转换为 Rgba8888 位图使用的本机整数布局。
    /// </summary>
    private static int PackRgba8888(Color color)
    {
        uint packed = ((uint)color.A << 24) |
                      ((uint)color.B << 16) |
                      ((uint)color.G << 8) |
                      color.R;
        return unchecked((int)packed);
    }

    #endregion

    #region 手势事件
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!HandleDesktopPressed(e)) _gesture.OnPointerPressed(e, this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!HandleDesktopMoved(e)) _gesture.OnPointerMoved(e, this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_desktopPointer == e.Pointer) { EndDesktopPointer(false, e.GetPosition(this), e.Timestamp); e.Handled = true; }
        else if (DataContext is not EditorViewModel { UseDesktopInput: true } || e.Pointer.Type != PointerType.Mouse) _gesture.OnPointerReleased(e, this);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (_desktopPointer == e.Pointer) EndDesktopPointer(true);
        _gesture.OnPointerCancelled(e, this);
    }
    #endregion
}