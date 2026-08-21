namespace OpenUtauMobile.Services.Performance;

/// <summary>由平台层提供的系统级性能指标。</summary>
public sealed record PlatformPerformanceMetrics(
    long? AppMemoryBytes, // 应用占用的内存
    long? SystemMemoryTotalBytes, // 系统已用内存
    long? SystemMemoryAvailableBytes, // 系统总内存
    double? SystemCpuUsagePercent) // CPU 利用率
{
    public static PlatformPerformanceMetrics Empty { get; } = new(null, null, null, null);
}

