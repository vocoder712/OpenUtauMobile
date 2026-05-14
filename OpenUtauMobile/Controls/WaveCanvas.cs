using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using NWaves.Signals;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Controls;

public class WaveCanvas : Control, ICmdSubscriber
{
    // 波形数据
    public static readonly StyledProperty<UWavePart?> WavePartProperty =
        AvaloniaProperty.Register<WaveCanvas, UWavePart?>(nameof(WavePart));

    // 变换属性：像素/Tick (横向缩放)
    public static readonly StyledProperty<double> TickWidthProperty =
        AvaloniaProperty.Register<WaveCanvas, double>(nameof(TickWidth), 0.1);

    // 变换属性：X轴偏移 (平移/滚动)
    public static readonly StyledProperty<double> TickOffsetProperty =
        AvaloniaProperty.Register<WaveCanvas, double>(nameof(TickOffset));

    public UWavePart? WavePart
    {
        get => GetValue(WavePartProperty);
        set => SetValue(WavePartProperty, value);
    }

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

    private PianoRollViewModel? ViewModel { get; set; }

    /// <summary>
    /// 波形缓存。
    /// 视口变化时标记为脏，下帧重绘。
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

    private WaveCache? _waveCache;

    static WaveCanvas()
    {
        AffectsRender<WaveCanvas>(
            WavePartProperty,
            TickWidthProperty,
            TickOffsetProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // 视口参数变化时标记波形缓存为脏
        if (change.Property == TickOffsetProperty ||
            change.Property == TickWidthProperty)
        {
            if (_waveCache != null)
                _waveCache.IsDirty = true;
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

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // 绘制透明背景以启用指针事件
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        DrawBound(context);

        if (WavePart == null || ViewModel == null) return;

        // Peaks 尚未加载完成时直接返回
        if (!WavePart.Peaks.IsCompletedSuccessfully) return;
        if (WavePart.Peaks.Result is null) return;

        _waveCache ??= new WaveCache();

        if (!_waveCache.IsDirty && (_waveCache.PartPosition != WavePart.position || _waveCache.PartDuration != WavePart.Duration))
            _waveCache.IsDirty = true;

        if (_waveCache.IsDirty)
        {
            RedrawWaveCache(WavePart);
            _waveCache.IsDirty = false;
        }

        if (_waveCache.Bitmap == null) return;

        // 与 PartsCanvas 对齐的 srcRect/dstRect 计算
        // 位图 x=0 对应视口左边缘（TickOffset）
        double partXInView = (WavePart.position - TickOffset) * TickWidth;

        // srcRect: 位图中绘制了波形的区域
        // - 左边缘：max(0, partXInView)（part 起点在视口内的像素位置，或 0 如果 part 从视口左侧开始）
        // - 右边缘：min(bitmapWidth, partXInView + partWidth)
        double partEndXInView = partXInView + WavePart.Duration * TickWidth;
        int srcLeft = (int)Math.Max(0, partXInView);
        int srcRight = (int)Math.Min(_waveCache.Bitmap.PixelSize.Width,
            Math.Min(partEndXInView, Bounds.Width));

        if (srcRight <= srcLeft) return;

        Rect srcRect = new(srcLeft, 0, srcRight - srcLeft, _waveCache.Bitmap.PixelSize.Height);
        Rect dstRect = new(srcLeft, 0, srcRight - srcLeft, Bounds.Height);

        context.DrawImage(_waveCache.Bitmap, srcRect, dstRect);
    }

    // TODO: 颜色有问题，看不见
    private void DrawBound(DrawingContext context)
    {
        if (ViewModel == null) return;
        if (WavePart == null)
        {
            context.DrawRectangle(ThemeResources.GetBrush("Sem.Color.GlassOverlay"), null, new Rect(Bounds.Size));
            return;
        }

        // 绘制遮罩：Part 的 position 左侧和 End 右侧区域不可编辑
        double partStartX = ViewModel.TickToPointX(WavePart.position);
        double partEndX = ViewModel.TickToPointX(WavePart.End);
        if (partStartX > 0)
        {
            double maskWidth = Math.Min(partStartX, Bounds.Width);
            using (context.PushOpacity(0.5))
            {
                context.DrawRectangle(ThemeResources.GetBrush("Sem.Color.GlassOverlay"), null,
                    new Rect(0, 0, maskWidth, Bounds.Height));
            }
        }

        if (partEndX < Bounds.Width)
        {
            double maskLeft = Math.Max(partEndX, 0);
            using (context.PushOpacity(0.5))
            {
                context.DrawRectangle(ThemeResources.GetBrush("Sem.Color.GlassOverlay"), null,
                    new Rect(maskLeft, 0, Bounds.Width - maskLeft, Bounds.Height));
            }
        }
    }

    /// <summary>
    /// 取得或创建 <see cref="WaveCache"/>。
    /// 位图宽度基于当前视口宽度和缓冲，支持任意Duration和TickWidth。
    /// 按128px步进扩容以平衡性能和内存。
    /// </summary>
    private WaveCache GetOrCreateWaveCache()
    {
        _waveCache ??= new WaveCache();

        // 位图需要容纳视口宽度加上缓冲，以支持平滑滚动
        // 缓冲大小 = 视口宽度的50%，最少256px
        double bufferWidth = Math.Max(256, Bounds.Width * 0.5);
        int neededW = 128 * ((int)((Bounds.Width + bufferWidth) / 128) + 1);
        int neededH = Math.Max(1, (int)Bounds.Height);

        if (_waveCache.Bitmap == null ||
            _waveCache.Bitmap.PixelSize.Width < neededW ||
            _waveCache.Bitmap.PixelSize.Height != neededH)
        {
            _waveCache.Dispose();
            PixelSize size = new(neededW, neededH);
            _waveCache.Bitmap = new WriteableBitmap(size, new Vector(96, 96),
                PixelFormat.Rgba8888, AlphaFormat.Unpremul);
            _waveCache.PixelData = new int[size.Width * size.Height];
        }

        return _waveCache;
    }

    /// <summary>
    /// 将 Peaks 数据写入波形缓存的位图。
    /// <para>
    /// - 位图坐标系以**视口左边缘**为 x=0（而非 part.position）
    /// - 位图 x 与绝对 tick 的关系：posTick = TickOffset + x / TickWidth
    /// - 只绘制 part 在视口内可见的部分，其余列保持透明
    /// </para>
    /// 支持单声道（振幅居中）和双声道（上下各占一半）。
    /// </summary>
    private void RedrawWaveCache(UWavePart part)
    {
        if (!part.Peaks.IsCompletedSuccessfully || ReferenceEquals(part.Peaks.Result, null))
            return;

        DiscreteSignal[] peaks = part.Peaks.Result;
        _ = GetOrCreateWaveCache(); // 确保位图已创建

        WriteableBitmap bitmap = _waveCache!.Bitmap!;
        int[] data = _waveCache!.PixelData;
        int bmpW = bitmap.PixelSize.Width;
        int bmpH = bitmap.PixelSize.Height;

        Array.Clear(data, 0, data.Length);

        TimeAxis timeAxis = DocManager.Inst.Project.timeAxis;
        double offsetMs = timeAxis.TickPosToMsPos(part.position);

        // 振幅到像素的缩放系数（上下各留 2px 内边距）
        double monoChnlAmp = (bmpH - 4.0) / 2.0; // 单声道：全高居中
        double stereoChnlAmp = (bmpH - 6.0) / 4.0; // 双声道：各占上下一半

        // 计算绘制起始列
        // 位图 x=0 对应视口左边缘（TickOffset）
        int x = 0;
        if (TickOffset <= part.position)
        {
            // Part 起点在视口内或右侧：从 part 起点对应的位图列开始
            x = (int)(TickWidth * (part.position - TickOffset));
        }

        // 初始 tick 与样本索引
        int posTick = (int)(TickOffset + x / TickWidth);
        double posMs = timeAxis.TickPosToMsPos(posTick);
        int sampleIndex = Math.Clamp(
            (int)(part.peaksSampleRate * (posMs - offsetMs) * 0.001),
            0, peaks[0].Length);

        float[] lastSMin = new float[peaks.Length];
        float[] lastSMax = new float[peaks.Length];
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
                for (int ch = 0; ch < peaks.Length; ch++)
                {
                    // 手写 min/max，避免 LINQ 产生额外分配
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
                for (int ch = 0; ch < peaks.Length; ch++)
                {
                    float s = peaks[ch].Samples[sampleIndex];
                    lastSMin[ch] = s;
                    lastSMax[ch] = s;
                }
            }

            if (hasPeak)
            {
                for (int ch = 0; ch < peaks.Length; ch++)
                {
                    double ySpan = peaks.Length == 1 ? monoChnlAmp : stereoChnlAmp;
                    double yOffset = ch == 1 ? monoChnlAmp : 0.0;

                    int y1 = (int)(ySpan * (1.0 - lastSMin[ch]) + yOffset) + 2;
                    int y2 = (int)(ySpan * (1.0 - lastSMax[ch]) + yOffset) + 2;
                    DrawPeak(data, bmpW, bmpH, x, y1, y2);
                }
            }

            x++;
            posTick = nextPosTick;
            sampleIndex = nextSampleIndex;
        }

        using ILockedFramebuffer frameBuffer = bitmap.Lock();
        Marshal.Copy(data, 0, frameBuffer.Address, data.Length);

        _waveCache.PartPosition = part.position;
        _waveCache.PartDuration = part.Duration;
    }

    /// <summary>
    /// 在像素缓冲 <paramref name="data"/> 的第 <paramref name="x"/> 列，
    /// 从 y1 到 y2 写入白色像素（越界自动夹紧）。
    /// </summary>
    private static void DrawPeak(int[] data, int width, int height, int x, int y1, int y2)
    {
        const int white = unchecked((int)0xFFFFFFFF);
        if (y1 > y2) (y1, y2) = (y2, y1);
        y1 = Math.Clamp(y1, 0, height - 1);
        y2 = Math.Clamp(y2, 0, height - 1);
        for (int y = y1; y <= y2; y++)
            data[x + width * y] = white;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is not PianoRollViewModel vm) return;

        ViewModel?.RequestInvalidateVisual -= InvalidateVisual;
        ViewModel = vm;
        ViewModel.RequestInvalidateVisual += InvalidateVisual;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        ViewModel?.Gesture.OnPointerPressed(e, this);
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
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        ViewModel?.Gesture.OnPointerCancelled(e, this);
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
        switch (cmd)
        {
            case PartCommand:
                _waveCache?.IsDirty = true;
                InvalidateVisual();
                break;
        }
    }
}