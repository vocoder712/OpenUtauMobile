using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace OpenUtauMobile.Services.Performance;

/// <summary>聚合共享运行时指标和平台指标，并管理采样生命周期。</summary>
public sealed class PerformanceMonitorService
{
    private static readonly TimeSpan SamplingInterval = TimeSpan.FromSeconds(1);
    private const double PercentScale = 100.0;

    private readonly object _stateLock = new object();
    private CancellationTokenSource? _samplingCancellation;
    private TimeSpan? _previousProcessCpuTime;
    private long? _previousSampleTimestamp;

    public static PerformanceMonitorService Instance { get; } = new PerformanceMonitorService();

    public bool IsEnabled { get; private set; }
    public PerformanceSnapshot? LatestSnapshot { get; private set; }

    public event Action<bool>? EnabledChanged;
    public event Action<PerformanceSnapshot>? SnapshotUpdated;

    private PerformanceMonitorService()
    {
    }

    public void SetEnabled(bool enabled)
    {
        CancellationTokenSource? cancellationToDispose = null;
        CancellationTokenSource? cancellationToStart = null;
        lock (_stateLock)
        {
            if (IsEnabled == enabled)
            {
                return;
            }

            IsEnabled = enabled;
            _previousProcessCpuTime = null;
            _previousSampleTimestamp = null;

            cancellationToDispose = _samplingCancellation;
            cancellationToStart = enabled ? new CancellationTokenSource() : null;
            _samplingCancellation = cancellationToStart;
        }

        cancellationToDispose?.Cancel();
        cancellationToDispose?.Dispose();
        PublishEnabledChanged(enabled);

        if (enabled)
        {
            CancellationToken cancellationToken = cancellationToStart!.Token;
            _ = Task.Run(() => SamplingLoopAsync(cancellationToken), cancellationToken);
        }
    }

    private async Task SamplingLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                PerformanceSnapshot snapshot = CaptureSnapshot();
                LatestSnapshot = snapshot;
                Dispatcher.UIThread.Post(() => SnapshotUpdated?.Invoke(snapshot));
                await Task.Delay(SamplingInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常停止采样。
        }
    }

    private PerformanceSnapshot CaptureSnapshot()
    {
        PlatformPerformanceMetrics platformMetrics;
        try
        {
            platformMetrics = ServiceHub.PlatformPerformanceProvider?.Capture()
                              ?? PlatformPerformanceMetrics.Empty;
        }
        catch
        {
            platformMetrics = PlatformPerformanceMetrics.Empty;
        }

        long? processMemoryBytes = CaptureProcessMemoryBytes();
        double? processCpuUsage = CaptureProcessCpuUsage();
        long? systemMemoryUsedBytes = null;
        if (platformMetrics.SystemMemoryTotalBytes is long totalBytes &&
            platformMetrics.SystemMemoryAvailableBytes is long availableBytes)
        {
            systemMemoryUsedBytes = Math.Max(0L, totalBytes - availableBytes);
        }

        IFrameRateProvider? frameRateProvider = ServiceHub.FrameRateProvider;
        return new PerformanceSnapshot(
            platformMetrics.AppMemoryBytes ?? processMemoryBytes,
            GC.GetTotalMemory(false),
            systemMemoryUsedBytes,
            platformMetrics.SystemMemoryTotalBytes,
            processCpuUsage,
            platformMetrics.SystemCpuUsagePercent,
            frameRateProvider?.FramesPerSecond,
            frameRateProvider?.AverageFrameTimeMilliseconds);
    }

    private static long? CaptureProcessMemoryBytes()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            return process.WorkingSet64;
        }
        catch
        {
            return null;
        }
    }

    private double? CaptureProcessCpuUsage()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            TimeSpan currentCpuTime = process.TotalProcessorTime;
            long currentTimestamp = Stopwatch.GetTimestamp();
            double? usage = null;

            if (_previousProcessCpuTime is TimeSpan previousCpuTime &&
                _previousSampleTimestamp is long previousTimestamp)
            {
                TimeSpan wallTime = Stopwatch.GetElapsedTime(previousTimestamp, currentTimestamp);
                double cpuSeconds = (currentCpuTime - previousCpuTime).TotalSeconds;
                double capacitySeconds = wallTime.TotalSeconds * Environment.ProcessorCount;
                if (capacitySeconds > 0.0)
                {
                    usage = Math.Clamp(cpuSeconds / capacitySeconds * PercentScale, 0.0, PercentScale);
                }
            }

            _previousProcessCpuTime = currentCpuTime;
            _previousSampleTimestamp = currentTimestamp;
            return usage;
        }
        catch
        {
            _previousProcessCpuTime = null;
            _previousSampleTimestamp = null;
            return null;
        }
    }

    private void PublishEnabledChanged(bool enabled)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            EnabledChanged?.Invoke(enabled);
            return;
        }

        Dispatcher.UIThread.Post(() => EnabledChanged?.Invoke(enabled));
    }
}
