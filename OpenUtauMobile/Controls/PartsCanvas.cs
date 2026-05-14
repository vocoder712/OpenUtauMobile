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
using NWaves.Signals;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Controls.Gestures;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 编曲区分片画布。负责绘制所有 UPart（音符缩略图 / 波形缩略图 / 名称标签），
/// 并通过 ICmdSubscriber 订阅 DocManager 命令以维护内部绘制缓存的一致性。
/// </summary>
public class PartsCanvas : Control, ICmdSubscriber
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

        public void Dispose()
        {
            Bitmap?.Dispose();
            Bitmap = null;
        }
    }

    private readonly Dictionary<UWavePart, WaveCache> _waveCache = [];

    #endregion

    #region 手势解释器

    private readonly GestureInterpreter _gesture = new();

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
        base.OnDataContextChanged(e);

        // DataContext 变化时重新绑定 DocManager 订阅，防止重复注册
        DocManager.Inst.RemoveSubscriber(this);
        if (DataContext is not null)
            DocManager.Inst.AddSubscriber(this);

        if (DataContext is not EditorViewModel vm) return;
        vm.RequestInvalidateVisual += InvalidateVisual;

        _gesture.Tap = pt => vm.OnGestureTap(pt);
        _gesture.DoubleTap = pt => vm.OnGestureDoubleTap(pt);
        _gesture.DragBegin = start => vm.OnGestureDragBegin(start);
        _gesture.DragUpdate = (start, step, total, _, ts) => vm.OnGestureDragUpdate(start, step, total, ts);
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

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        DocManager.Inst.RemoveSubscriber(this);
        foreach (WaveCache c in _waveCache.Values) c.Dispose();
        _waveCache.Clear();
        _toneRangeCache.Clear();
    }

    #endregion

    #region 属性/集合变更

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SelectedPartsProperty)
        {
            // 绑定/解绑集合变更事件
            if (change.OldValue is INotifyCollectionChanged oldList)
                oldList.CollectionChanged -= OnCollectionChanged;
            if (change.NewValue is INotifyCollectionChanged newList)
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

        TaskScheduler scheduler = TaskScheduler.FromCurrentSynchronizationContext();
        wp.Peaks.ContinueWith(_ =>
        {
            if (_waveCache.TryGetValue(wp, out WaveCache? cache))
                cache.IsDirty = true;
            InvalidateVisual();
        }, CancellationToken.None, TaskContinuationOptions.None, scheduler);
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
                InvalidateVisual();
                break;
            case TrackCommand:
                InvalidateVisual();
                break;
            case PartCommand { part: UWavePart wp }:
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
        IList<UPart> parts = DocManager.Inst.Project.parts;
        // 透明矩形确保控件能接收指针事件
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        if (parts.Count <= 0) return;

        foreach (UPart part in parts)
        {
            double x = (part.position - TickOffset) * TickWidth;
            double y = part.trackNo * TrackHeight - TrackOffset;
            double width = part.Duration * TickWidth;
            double height = TrackHeight;

            // 完全在视口外：跳过，不进行任何绘制
            if (x + width < 0 || x > Bounds.Width) continue;
            if (y + height < 0 || y > Bounds.Height) continue;

            Rect rect = new(x, y + 1, width, height - 2); // 分片在屏幕中的矩形区域，竖向上下各留 1px 间距分隔轨道
            SolidColorBrush brush = TrackPalette.GetTrackColor(DocManager.Inst.Project.tracks[part.trackNo].TrackColor)
                .AccentColor;
            bool selected = SelectedParts != null && SelectedParts.Contains(part); // 是否选中
            IPen? borderPen = selected ? ThemeResources.GetPen("Sem.Color.Primary", 2) : null;

            // 1. 背景色块 + 边框
            using (context.PushOpacity(selected ? 1 : 0.64))
            {
                context.DrawRectangle(brush, borderPen, rect);
            }

            // 2. 内容缩略图（音符 / 波形）
            switch (part)
            {
                case UVoicePart { notes.Count: > 0 } voicePart:
                    DrawNotePreview(context, voicePart, rect);
                    break;
                case UWavePart wavePart:
                    DrawWaveformCached(context, wavePart, rect);
                    break;
            }

            // 3. 名称标签（最顶层，覆盖在缩略图上）
            DrawPartLabel(context, part, rect, brush);

            // 4. 绘制拖拽手柄
            if (selected)
            {
                DrawResizeHandle(context, rect);
            }
        }
    }

    /// <summary>
    /// 在 part 矩形左上角绘制名称标签。
    /// </summary>
    private static void DrawPartLabel(DrawingContext context, UPart part, Rect rect, IBrush partBrush)
    {
        TextLayout textLayout = TextLayoutCache.Get(part.DisplayName, ThemeResources.GetBrush("Sem.Color.OnSurface"),
            LabelFontSize);
        // 文字加左右各 3px 内边距后仍超过 part 宽度时跳过
        if (textLayout.Width + 6 > rect.Width) return;

        using (context.PushClip(rect))
        using (context.PushTransform(Matrix.CreateTranslation(rect.X + 3, rect.Y + 2)))
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

    /// <summary>
    /// 在 part 矩形内绘制音符线段缩略图。
    /// </summary>
    private void DrawNotePreview(DrawingContext context, UVoicePart part, Rect rect)
    {
        IPen notePen = ThemeResources.GetPen("Sem.Color.OnSurface", 2);

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
        // 左哨兵向左偏移一个视口宽度，以包含跨越视口左边缘的长音符
        int viewWidthTick = (int)(rect.Width / TickWidth) + 1;
        int loTick = Math.Max(0, viewLeftTick - viewWidthTick);
        int hiTick = part.Duration;

        // SentinelNote.GetHashCode() == int.MinValue，确保哨兵落在同 position 真实音符之前
        SentinelNote loSentinel = new(loTick);
        SentinelNote hiSentinel = new(hiTick);

        using (context.PushClip(rect))
        using (context.PushTransform(Matrix.CreateTranslation(rect.X, rect.Y + 2)))
        {
            foreach (UNote note in part.notes.GetViewBetween(loSentinel, hiSentinel))
            {
                double noteX1 = note.position * TickWidth;

                // SortedSet 按 position 升序；起点超出 part 右侧则后续全部超出
                if (noteX1 >= partW) break;

                double noteX2 = note.End * TickWidth;
                // 音符右端仍在视口左侧（偏移量兜住的但实际不可见的音符）
                if (noteX2 <= viewLeftTick * TickWidth) continue;

                double noteY = (hi - note.tone) * scaleY;
                context.DrawLine(notePen, new Point(noteX1, noteY), new Point(noteX2, noteY));
            }
        }
    }

    /// <summary>
    /// 仅用于 <see cref="System.Collections.Generic.SortedSet{T}.GetViewBetween"/> 边界定位。
    /// <para>
    /// <see cref="UNote.CompareTo"/> 先比较 position，position 相同时比 GetHashCode()。
    /// 此类返回 int.MinValue，保证哨兵落在同 position 所有真实音符之前。
    /// </para>
    /// </summary>
    private sealed class SentinelNote : UNote
    {
        public SentinelNote(int position)
        {
            this.position = position;
        }

        public override int GetHashCode() => int.MinValue;
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

        if (!part.Peaks.IsCompletedSuccessfully) return;
        // Peaks.Result 注解为 non-null，但 Load() 存在 Task.FromResult<>(null) 路径
        if (ReferenceEquals(part.Peaks.Result, null)) return;

        WaveCache cache = GetOrCreateWaveCache(part);

        // 检测 part 属性变更（拖拽/裁剪等），自动标记脏
        if (!cache.IsDirty && (cache.PartPosition != part.position || cache.PartDuration != part.Duration))
            cache.IsDirty = true;

        if (cache.IsDirty)
        {
            RedrawWaveCache(cache, part);
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

        using (context.PushClip(rect))
            context.DrawImage(cache.Bitmap, srcRect, dstRect);
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
    private void RedrawWaveCache(WaveCache cache, UWavePart part)
    {
        DiscreteSignal[] peaks = part.Peaks.Result;
        WriteableBitmap bitmap = cache.Bitmap!;
        int[] data = cache.PixelData;
        int bmpW = bitmap.PixelSize.Width;
        int bmpH = bitmap.PixelSize.Height;
        int channelCount = peaks.Length;

        Array.Clear(data, 0, data.Length);

        TimeAxis timeAxis = DocManager.Inst.Project.timeAxis;
        double offsetMs = timeAxis.TickPosToMsPos(part.position);

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
            (int)(part.peaksSampleRate * (posMs - offsetMs) * 0.001),
            0, peaks[0].Length);

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
                (int)(part.peaksSampleRate * (nextPosMs - offsetMs) * 0.001),
                0, peaks[0].Length);

            if (nextSampleIndex > sampleIndex)
            {
                hasPeak = true;
                for (int ch = 0; ch < channelCount; ch++)
                {
                    float sMin = float.MaxValue, sMax = float.MinValue;
                    float[] samples = peaks[ch].Samples;
                    for (int k = sampleIndex; k < nextSampleIndex; k++)
                    {
                        float s = samples[k];
                        if (s < sMin) sMin = s;
                        if (s > sMax) sMax = s;
                    }
                    lastSMin[ch] = sMin;
                    lastSMax[ch] = sMax;
                }
            }
            else if (!hasPeak && posTick >= part.position && sampleIndex < peaks[0].Length)
            {
                hasPeak = true;
                for (int ch = 0; ch < channelCount; ch++)
                {
                    float s = peaks[ch].Samples[sampleIndex];
                    lastSMin[ch] = s;
                    lastSMax[ch] = s;
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
                    DrawPeak(data, bmpW, bmpH, x, y1, y2);
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
    }

    /// <summary>
    /// 在像素缓冲 <paramref name="data"/> 的第 <paramref name="x"/> 列，
    /// 从 y1 到 y2 写入白色像素
    /// </summary>
    private static void DrawPeak(int[] data, int width, int height, int x, int y1, int y2)
    {
        const int white = unchecked((int)0xFFFFFFFF); // TODO: 跟随主题变化
        if (y1 > y2) (y1, y2) = (y2, y1);
        y1 = Math.Clamp(y1, 0, height - 1);
        y2 = Math.Clamp(y2, 0, height - 1);
        for (int y = y1; y <= y2; y++)
            data[x + width * y] = white;
    }

    /// <summary>
    /// 在 Part 矩形右侧绘制调整时长手柄
    /// TODO: 实现从左侧调整
    /// </summary>
    private static void DrawResizeHandle(DrawingContext context, Rect rect)
    {
        if (rect.Width < ViewConstants.ResizeHandleVisualWidth) return; // Part 太窄时不绘制手柄
        const double hW = ViewConstants.ResizeHandleVisualWidth;
        const double pad = 1.0; // 手柄右边缘与 Part 右边缘的间距
        const double vPad = 4.0; // 手柄上下内边距

        Rect handleRect = new(
            rect.Right - hW - pad,
            rect.Y + vPad,
            hW,
            rect.Height - vPad * 2);

        if (handleRect.Height <= 0) return;

        // 背景圆角矩形
        using (context.PushClip(rect))
        {
            context.DrawRectangle(ThemeResources.GetBrush("Sem.Color.PrimaryContainer"), null, handleRect,
                radiusX: 3, radiusY: 3);

            double cx = handleRect.X + handleRect.Width / 2.0;
            double lineH = handleRect.Height * 0.40;
            double ly1 = handleRect.Y + (handleRect.Height - lineH) / 2.0;
            double ly2 = ly1 + lineH;

            context.DrawLine(ThemeResources.GetPen("Sem.Color.OnPrimary", 1.5),
                new Point(cx - 2.5, ly1), new Point(cx - 2.5, ly2));
            context.DrawLine(ThemeResources.GetPen("Sem.Color.OnPrimary", 1.5),
                new Point(cx + 2.5, ly1), new Point(cx + 2.5, ly2));
        }
    }
    #endregion

    #region 手势事件
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _gesture.OnPointerPressed(e, this);
    }

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
    #endregion
}