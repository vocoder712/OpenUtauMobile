namespace OpenUtauMobile.Services.Performance;

/// <summary>由平台层提供的系统级性能指标。</summary>
public sealed record PlatformPerformanceMetrics(
    long? AppMemoryBytes,
    long? SystemMemoryTotalBytes,
    long? SystemMemoryAvailableBytes,
    double? SystemCpuUsagePercent)
{
    public static PlatformPerformanceMetrics Empty { get; } = new(null, null, null, null);
}

