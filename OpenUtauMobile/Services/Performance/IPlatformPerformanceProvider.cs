namespace OpenUtauMobile.Services.Performance;

/// <summary>读取平台相关的进程及系统性能指标。</summary>
public interface IPlatformPerformanceProvider
{
    PlatformPerformanceMetrics Capture();
}

