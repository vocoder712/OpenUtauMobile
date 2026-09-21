using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtauMobile.Controls
{
    public sealed class RenderWaveformCanvas : Control
    {
        public static readonly StyledProperty<UVoicePart?> PartProperty =
            AvaloniaProperty.Register<RenderWaveformCanvas, UVoicePart?>(nameof(Part));
        public static readonly StyledProperty<double> TickWidthProperty =
            AvaloniaProperty.Register<RenderWaveformCanvas, double>(nameof(TickWidth));
        public static readonly StyledProperty<double> TickOffsetProperty =
            AvaloniaProperty.Register<RenderWaveformCanvas, double>(nameof(TickOffset));
        public static readonly StyledProperty<bool> ShowWaveformProperty =
            AvaloniaProperty.Register<RenderWaveformCanvas, bool>(nameof(ShowWaveform));
        public static readonly StyledProperty<IBrush?> ReadyBrushProperty =
            AvaloniaProperty.Register<RenderWaveformCanvas, IBrush?>(nameof(ReadyBrush));
        public static readonly StyledProperty<IBrush?> PendingBrushProperty =
            AvaloniaProperty.Register<RenderWaveformCanvas, IBrush?>(nameof(PendingBrush));

        public UVoicePart? Part { get => GetValue(PartProperty); set => SetValue(PartProperty, value); }
        public double TickWidth { get => GetValue(TickWidthProperty); set => SetValue(TickWidthProperty, value); }
        public double TickOffset { get => GetValue(TickOffsetProperty); set => SetValue(TickOffsetProperty, value); }
        public bool ShowWaveform { get => GetValue(ShowWaveformProperty); set => SetValue(ShowWaveformProperty, value); }
        public IBrush? ReadyBrush { get => GetValue(ReadyBrushProperty); set => SetValue(ReadyBrushProperty, value); }
        public IBrush? PendingBrush { get => GetValue(PendingBrushProperty); set => SetValue(PendingBrushProperty, value); }

        private sealed record PeakEntry(WeakReference<Frozen<float>> Source, RenderPeakPyramid Peaks);
        private readonly Dictionary<ulong, PeakEntry> _peaks = new();
        private readonly LinkedList<ulong> _cacheOrder = new();
        private readonly HashSet<ulong> _failed = new();
        private int _cacheBytes;
        private const int CacheBudget = 8 * 1024 * 1024;
        // MixPlanner 发布的 PCM 与播放通路统一使用 44100 Hz。
        private const double FramesPerMillisecond = 44.1;
        private IDisposable? _subscription;
        private CancellationTokenSource? _work;
        private RenderProjection? _projection;
        private StreamGeometry? _geometry;
        private bool _geometryDirty = true;
        private double[] _columnMs = [];
        private float[] _columnPeaks = [];

        static RenderWaveformCanvas()
        {
            AffectsRender<RenderWaveformCanvas>(PartProperty, TickWidthProperty, TickOffsetProperty,
                ShowWaveformProperty, ReadyBrushProperty, PendingBrushProperty);
        }

        public RenderWaveformCanvas()
        {
            ClipToBounds = true;
            IsHitTestVisible = false;
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _subscription = RenderView.Inst.Observe(projection =>
            {
                if (ReferenceEquals(projection.Part, Part))
                {
                    _projection = projection;
                    _failed.Clear();
                    HashSet<ulong> current = new();
                    foreach (PhraseView phrase in projection.Phrases)
                    {
                        current.Add(phrase.Hash);
                    }
                    foreach (ulong hash in new List<ulong>(_peaks.Keys))
                    {
                        if (!current.Contains(hash))
                        {
                            _cacheBytes -= _peaks[hash].Peaks.ByteSize;
                            _peaks.Remove(hash);
                            _cacheOrder.Remove(hash);
                        }
                    }
                    Dirty();
                }
            });
            Dirty();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _subscription?.Dispose();
            _subscription = null;
            Reset();
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == PartProperty || change.Property == ShowWaveformProperty)
            {
                Reset();
            }
            if (change.Property == TickWidthProperty || change.Property == TickOffsetProperty ||
                change.Property == BoundsProperty)
            {
                Dirty();
            }
        }

        private void Dirty()
        {
            _geometryDirty = true;
            InvalidateVisual();
        }

        private void Reset()
        {
            _work?.Cancel();
            _peaks.Clear();
            _cacheOrder.Clear();
            _failed.Clear();
            _cacheBytes = 0;
            _projection = null;
            _geometry = null;
            _columnMs = [];
            _columnPeaks = [];
            Dirty();
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            if (Part == null || TickWidth <= 0 || !double.IsFinite(TickWidth) ||
                !double.IsFinite(TickOffset) || Bounds.Width <= 0 || Bounds.Height <= 0)
            {
                return;
            }
            _projection ??= RenderView.Inst.Current(Part);
            TimeAxis axis = DocManager.Inst.Project.timeAxis;
            double leftMs = axis.TickPosToMsPos(TickOffset);
            double rightMs = axis.TickPosToMsPos(TickOffset + Bounds.Width / TickWidth);
            if (!ShowWaveform)
            {
                Pen outline = new(PendingBrush);
                foreach (PhraseView phrase in _projection.Phrases)
                {
                    if (phrase.Layout.EndMs <= leftMs || phrase.Layout.StartMs >= rightMs)
                    {
                        continue;
                    }
                    double left = (axis.MsPosToTickPos(phrase.Layout.StartMs) - TickOffset) * TickWidth;
                    double right = (axis.MsPosToTickPos(phrase.Layout.EndMs) - TickOffset) * TickWidth;
                    if (right > left && Bounds.Height > 4)
                    {
                        // 保留乐句的真实边界，由控件裁剪视口外区域，避免滚动时出现伪造的边框。
                        Rect rect = new(left, 2, right - left, Bounds.Height - 4);
                        context.DrawRectangle(phrase.Rendered ? ReadyBrush : null,
                            phrase.Rendered ? null : outline, rect);
                    }
                }
                return;
            }
            if (_geometryDirty)
            {
                BuildGeometry(axis, leftMs, rightMs);
            }
            if (_geometry != null)
            {
                context.DrawGeometry(ReadyBrush, null, _geometry);
            }
        }

        private void BuildGeometry(TimeAxis axis, double leftMs, double rightMs)
        {
            _geometryDirty = false;
            // 以逻辑像素为预算；高 DPI 不放大 CPU 工作量，GPU 负责最终栅格化。
            int columns = Math.Max(1, (int)Math.Ceiling(Bounds.Width));
            if (_columnPeaks.Length != columns)
            {
                _columnPeaks = new float[columns];
                _columnMs = new double[columns + 1];
            }
            Array.Clear(_columnPeaks);
            for (int x = 0; x <= columns; x++)
            {
                _columnMs[x] = axis.TickPosToMsPos(TickOffset + x / TickWidth);
            }
            foreach (PhraseView phrase in _projection!.Phrases)
            {
                if (!phrase.Rendered || phrase.Layout.EndMs <= leftMs || phrase.Layout.StartMs >= rightMs ||
                    !PlaybackManager.Inst.MixPlanner.TryGetPhrasePcm(Part!, phrase.Hash,
                        out (double posMs, double durMs, int channels, Frozen<float> pcm) placement) ||
                    placement.channels <= 0)
                {
                    continue;
                }
                if (!_peaks.TryGetValue(phrase.Hash, out PeakEntry? entry) ||
                    !entry.Source.TryGetTarget(out Frozen<float>? source) || !ReferenceEquals(source, placement.pcm))
                {
                    if (_work == null && _subscription != null && !_failed.Contains(phrase.Hash))
                    {
                        StartBuild(phrase.Hash, placement.pcm, placement.channels);
                    }
                    continue;
                }
                double startMs = phrase.Layout.StartMs;
                double endMs = Math.Min(phrase.Layout.EndMs,
                    startMs + placement.pcm.Length / placement.channels / FramesPerMillisecond);
                int first = (int)Math.Clamp(Math.Floor((axis.MsPosToTickPos(startMs) - TickOffset) * TickWidth), 0, columns);
                int end = (int)Math.Clamp(Math.Ceiling((axis.MsPosToTickPos(endMs) - TickOffset) * TickWidth), first, columns);
                for (int x = first; x < end; x++)
                {
                    float peak = entry.Peaks.Peak((Math.Max(startMs, _columnMs[x]) - startMs) * FramesPerMillisecond,
                        (Math.Min(endMs, _columnMs[x + 1]) - startMs) * FramesPerMillisecond);
                    // 重叠乐句取包络并集，避免每帧混音和扫描原始采样。
                    _columnPeaks[x] = Math.Max(_columnPeaks[x], peak);
                }
            }
            StreamGeometry geometry = new();
            using (StreamGeometryContext drawing = geometry.Open())
            {
                double baseline = Bounds.Height;
                // 只画从底边向上的半波形，宿主负责与其他控件的叠放关系。
                double height = Bounds.Height;
                int x = 0;
                while (x < columns)
                {
                    if (_columnPeaks[x] <= 0)
                    {
                        x++;
                        continue;
                    }
                    drawing.BeginFigure(new Point(x, baseline));
                    while (x < columns && _columnPeaks[x] > 0)
                    {
                        double y = baseline - _columnPeaks[x] * height;
                        drawing.LineTo(new Point(x, y));
                        drawing.LineTo(new Point(x + 1, y));
                        x++;
                    }
                    drawing.LineTo(new Point(x, baseline));
                    drawing.EndFigure(true);
                }
            }
            _geometry = geometry;
        }

        private void StartBuild(ulong hash, Frozen<float> pcm, int channels)
        {
            CancellationTokenSource work = new();
            _work = work;
            UVoicePart? part = Part;
            // 先占用任务槽，再退出当前绘制。后台任务即使同步完成，也不能在 Render 内请求重绘。
            Dispatcher.UIThread.Post(() => _ = BuildPeaksAsync(hash, pcm, channels, work, part));
        }

        private async Task BuildPeaksAsync(ulong hash, Frozen<float> pcm, int channels,
            CancellationTokenSource work, UVoicePart? part)
        {
            try
            {
                // 排队期间可能已经切换分片、关闭波形或卸载控件。
                work.Token.ThrowIfCancellationRequested();
                // 单个后台任务串行生成峰值，缩放只改变查询，不启动并行重采样任务。
                RenderPeakPyramid peaks = await Task.Run(() => RenderPeakPyramid.Build(pcm, channels, work.Token), work.Token);
                if (!work.IsCancellationRequested && ReferenceEquals(part, Part) && ShowWaveform && _subscription != null)
                {
                    if (_peaks.Remove(hash, out PeakEntry? previous))
                    {
                        _cacheBytes -= previous.Peaks.ByteSize;
                        _cacheOrder.Remove(hash);
                    }
                    while (_cacheBytes + peaks.ByteSize > CacheBudget && _cacheOrder.First != null)
                    {
                        ulong oldest = _cacheOrder.First.Value;
                        _cacheOrder.RemoveFirst();
                        if (_peaks.Remove(oldest, out PeakEntry? removed))
                        {
                            _cacheBytes -= removed.Peaks.ByteSize;
                        }
                    }
                    _peaks[hash] = new PeakEntry(new WeakReference<Frozen<float>>(pcm), peaks);
                    _cacheOrder.AddLast(hash);
                    _cacheBytes += peaks.ByteSize;
                }
            }
            catch (OperationCanceledException)
            {
                // 切换模式、分片或卸载时丢弃后台结果。
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to prepare rendered waveform peaks");
                if (!work.IsCancellationRequested)
                {
                    _failed.Add(hash);
                }
            }
            finally
            {
                _work = null;
                work.Dispose();
                if (_subscription != null)
                {
                    Dirty();
                }
            }
        }
    }
}
