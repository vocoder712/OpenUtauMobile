namespace OpenUtauMobile.Services.Performance;

/// <summary>一次性能采样的不可变结果。</summary>
public sealed record PerformanceSnapshot(
    long? AppMemoryBytes,
    long ManagedHeapBytes,
    long? SystemMemoryUsedBytes,
    long? SystemMemoryTotalBytes,
    double? AppCpuUsagePercent,
    double? SystemCpuUsagePercent,
    double? FramesPerSecond,
    double? AverageFrameTimeMilliseconds);

