namespace OpenUtauMobile.Services.Performance;

/// <summary>输出队列中尚未播放的数据时长；不含无法测得的外部设备传输延迟。</summary>
public interface IAudioLatencySource
{
    double? OutputLatencyMilliseconds { get; }
}
