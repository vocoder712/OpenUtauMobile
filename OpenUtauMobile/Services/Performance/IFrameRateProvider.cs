namespace OpenUtauMobile.Services.Performance;

/// <summary>提供 UI 帧率数据；具体渲染后端接入将在后续实现。</summary>
public interface IFrameRateProvider
{
    double? FramesPerSecond { get; }
    double? AverageFrameTimeMilliseconds { get; }
}

