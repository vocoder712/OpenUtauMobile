namespace OpenUtauMobile.Services.Performance;

/// <summary>一次 UI 帧率采样结果。</summary>
public readonly record struct FrameRateMetrics(
    double FramesPerSecond,
    double AverageFrameTimeMilliseconds);

/// <summary>提供 UI 帧率数据并管理帧回调生命周期。</summary>
public interface IFrameRateProvider
{
    void Start();
    void Stop();
    FrameRateMetrics? Capture();
}

