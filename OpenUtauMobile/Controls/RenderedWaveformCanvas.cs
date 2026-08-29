using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 在钢琴卷帘标尺空白区显示当前歌声分片已经渲染出的单侧波形。
/// </summary>
public sealed class RenderedWaveformCanvas : Control, ICmdSubscriber
{
    public static readonly StyledProperty<UVoicePart?> PartProperty =
        AvaloniaProperty.Register<RenderedWaveformCanvas, UVoicePart?>(nameof(Part));

    public static readonly StyledProperty<double> TickWidthProperty =
        AvaloniaProperty.Register<RenderedWaveformCanvas, double>(nameof(TickWidth));

    public static readonly StyledProperty<double> TickOffsetProperty =
        AvaloniaProperty.Register<RenderedWaveformCanvas, double>(nameof(TickOffset));

    public static readonly StyledProperty<IBrush?> WaveformBrushProperty =
        AvaloniaProperty.Register<RenderedWaveformCanvas, IBrush?>(nameof(WaveformBrush));

    private sealed class Envelope
    {
        public required float[] Peaks { get; init; }
        public required double StartMs { get; init; }
        public required double PeakRate { get; init; }
    }

    private CancellationTokenSource? _envelopeCancellation;
    private Envelope? _envelope;
    private UVoicePart? _envelopePart;
    private StreamGeometry? _geometry;
    private Envelope? _geometryEnvelope;
    private double _geometryStartTick;
    private double _geometryEndTick;
    private double _geometryTickWidth;
    private double _geometryHeight;

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

    public double TickOffset
    {
        get => GetValue(TickOffsetProperty);
        set => SetValue(TickOffsetProperty, value);
    }

    public IBrush? WaveformBrush
    {
        get => GetValue(WaveformBrushProperty);
        set => SetValue(WaveformBrushProperty, value);
    }

    static RenderedWaveformCanvas()
    {
        AffectsRender<RenderedWaveformCanvas>(
            PartProperty,
            TickWidthProperty,
            TickOffsetProperty,
            WaveformBrushProperty);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PartProperty)
        {
            QueueEnvelopeBuild();
        }
        else if (change.Property == TickWidthProperty)
        {
            InvalidateGeometry();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        DocManager.Inst.AddSubscriber(this);
        if (!ReferenceEquals(_envelopePart, Part))
        {
            QueueEnvelopeBuild();
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DocManager.Inst.RemoveSubscriber(this);
        CancelEnvelopeBuild();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_envelope == null || Part == null || WaveformBrush == null ||
            TickWidth <= 0 || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        EnsureGeometry(_envelope, Part);
        if (_geometry == null)
        {
            return;
        }

        double translateX = (_geometryStartTick - TickOffset) * TickWidth;
        using (context.PushClip(new Rect(Bounds.Size)))
        using (context.PushTransform(Matrix.CreateTranslation(translateX, 0)))
        {
            context.DrawGeometry(WaveformBrush, null, _geometry);
        }
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
        if (cmd is PartRenderedNotification notification &&
            ReferenceEquals(notification.part, Part))
        {
            QueueEnvelopeBuild();
        }
    }

    private async void QueueEnvelopeBuild()
    {
        CancelEnvelopeBuild();
        UVoicePart? part = Part;
        ISignalSource? mix = part?.Mix;
        if (part == null || mix == null)
        {
            _envelope = null;
            _envelopePart = part;
            InvalidateGeometry();
            InvalidateVisual();
            return;
        }

        TimeAxis timeAxis = DocManager.Inst.Project.timeAxis;
        double startMs = timeAxis.TickPosToMsPos(part.position);
        double endMs = timeAxis.TickPosToMsPos(part.End);
        CancellationTokenSource cancellation = new();
        _envelopeCancellation = cancellation;

        try
        {
            Envelope envelope = await Task.Run(
                () => BuildEnvelope(mix, startMs, endMs, cancellation.Token),
                cancellation.Token);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(Part, part))
            {
                _envelope = envelope;
                _envelopePart = part;
                InvalidateGeometry();
                InvalidateVisual();
            }
        }
        catch (OperationCanceledException)
        {
            // 快速切换分片或重复渲染时，旧包络任务按预期终止。
        }
        catch (Exception ex)
        {
            Log.Error(ex, "构建已渲染歌声波形包络失败");
        }
        finally
        {
            if (ReferenceEquals(_envelopeCancellation, cancellation))
            {
                _envelopeCancellation = null;
            }
            cancellation.Dispose();
        }
    }

    private static Envelope BuildEnvelope(
        ISignalSource mix,
        double startMs,
        double endMs,
        CancellationToken cancellationToken)
    {
        double durationSeconds = Math.Max(0, endMs - startMs) / 1000.0;
        double peakRate = ViewConstants.RenderedWaveformPeakRate;
        if (durationSeconds * peakRate > ViewConstants.RenderedWaveformMaxEnvelopePointCount)
        {
            peakRate = ViewConstants.RenderedWaveformMaxEnvelopePointCount / durationSeconds;
        }

        int peakCount = Math.Max(1, (int)Math.Ceiling(durationSeconds * peakRate));
        float[] peaks = new float[peakCount];
        long totalFrames = Math.Max(0,
            (long)Math.Ceiling(durationSeconds * ViewConstants.RenderedWaveformAudioSampleRate));
        int bufferLength = ViewConstants.RenderedWaveformMixBufferFrames *
            ViewConstants.RenderedWaveformChannelCount;
        float[] buffer = ArrayPool<float>.Shared.Rent(bufferLength);
        long startFrame = (long)Math.Floor(
            startMs * ViewConstants.RenderedWaveformAudioSampleRate / 1000.0);

        try
        {
            long frameOffset = 0;
            while (frameOffset < totalFrames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int frameCount = (int)Math.Min(
                    ViewConstants.RenderedWaveformMixBufferFrames,
                    totalFrames - frameOffset);
                int sampleCount = frameCount * ViewConstants.RenderedWaveformChannelCount;
                Array.Clear(buffer, 0, sampleCount);
                long samplePosition = (startFrame + frameOffset) *
                    ViewConstants.RenderedWaveformChannelCount;
                mix.Mix((int)Math.Clamp(samplePosition, int.MinValue, int.MaxValue), buffer, 0, sampleCount);

                for (int frame = 0; frame < frameCount; frame++)
                {
                    int sampleIndex = frame * ViewConstants.RenderedWaveformChannelCount;
                    float amplitude = Math.Max(
                        Math.Abs(buffer[sampleIndex]),
                        Math.Abs(buffer[sampleIndex + 1]));
                    int peakIndex = Math.Min(
                        peakCount - 1,
                        (int)((frameOffset + frame) * peakRate /
                            ViewConstants.RenderedWaveformAudioSampleRate));
                    if (amplitude > peaks[peakIndex])
                    {
                        peaks[peakIndex] = amplitude;
                    }
                }
                frameOffset += frameCount;
            }
        }
        finally
        {
            ArrayPool<float>.Shared.Return(buffer);
        }

        return new Envelope
        {
            Peaks = peaks,
            StartMs = startMs,
            PeakRate = peakRate,
        };
    }

    private void EnsureGeometry(Envelope envelope, UVoicePart part)
    {
        double visibleEndTick = TickOffset + Bounds.Width / TickWidth;
        bool cacheContainsViewport = ReferenceEquals(_geometryEnvelope, envelope) &&
            Math.Abs(_geometryTickWidth - TickWidth) < double.Epsilon &&
            Math.Abs(_geometryHeight - Bounds.Height) < double.Epsilon &&
            TickOffset >= _geometryStartTick && visibleEndTick <= _geometryEndTick;
        if (cacheContainsViewport)
        {
            return;
        }

        double cacheWidth = Math.Clamp(
            Bounds.Width * ViewConstants.RenderedWaveformCacheViewportFactor,
            ViewConstants.RenderedWaveformMinCacheWidth,
            ViewConstants.RenderedWaveformMaxCacheWidth);
        double overscanTicks = Math.Max(0, cacheWidth - Bounds.Width) / (2.0 * TickWidth);
        double startTick = Math.Max(part.position, TickOffset - overscanTicks);
        double endTick = Math.Min(part.End, visibleEndTick + overscanTicks);
        if (endTick <= startTick)
        {
            InvalidateGeometry();
            return;
        }

        double geometryWidth = (endTick - startTick) * TickWidth;
        int columnCount = Math.Max(1, (int)Math.Ceiling(geometryWidth));
        TimeAxis timeAxis = DocManager.Inst.Project.timeAxis;
        StreamGeometry geometry = new();
        using (StreamGeometryContext geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(new Point(0, Bounds.Height), true);
            for (int column = 0; column <= columnCount; column++)
            {
                double tick = Math.Min(endTick, startTick + column / TickWidth);
                double nextTick = Math.Min(endTick, startTick + (column + 1.0) / TickWidth);
                double startMs = timeAxis.TickPosToMsPos(tick);
                double endMs = timeAxis.TickPosToMsPos(nextTick);
                float peak = GetPeak(envelope, startMs, endMs);
                double y = Bounds.Height * (1.0 - Math.Clamp(
                    peak,
                    ViewConstants.RenderedWaveformMinimumPeak,
                    1.0));
                geometryContext.LineTo(new Point(column, y));
            }
            geometryContext.LineTo(new Point(geometryWidth, Bounds.Height));
            geometryContext.EndFigure(true);
        }

        _geometry = geometry;
        _geometryEnvelope = envelope;
        _geometryStartTick = startTick;
        _geometryEndTick = endTick;
        _geometryTickWidth = TickWidth;
        _geometryHeight = Bounds.Height;
    }

    private static float GetPeak(Envelope envelope, double startMs, double endMs)
    {
        int startIndex = Math.Clamp(
            (int)Math.Floor((startMs - envelope.StartMs) * envelope.PeakRate / 1000.0),
            0,
            envelope.Peaks.Length - 1);
        int endIndex = Math.Clamp(
            (int)Math.Ceiling((endMs - envelope.StartMs) * envelope.PeakRate / 1000.0),
            startIndex + 1,
            envelope.Peaks.Length);
        float peak = 0;
        for (int index = startIndex; index < endIndex; index++)
        {
            peak = Math.Max(peak, envelope.Peaks[index]);
        }
        return peak;
    }

    private void CancelEnvelopeBuild()
    {
        _envelopeCancellation?.Cancel();
        _envelopeCancellation = null;
    }

    private void InvalidateGeometry()
    {
        _geometry = null;
        _geometryEnvelope = null;
    }
}
