using System;

namespace OpenUtauMobile.Services.VideoExport;

/// <summary>钢琴卷帘视频的画幅预设。</summary>
public enum PianoRollVideoAspect
{
    /// <summary>横屏 1920×1080。</summary>
    Landscape,

    /// <summary>竖屏 1080×1920。</summary>
    Portrait,

    /// <summary>使用自定义宽高。</summary>
    Custom,
}

/// <summary>钢琴卷帘视频导出参数。</summary>
/// <param name="Aspect">画幅预设。</param>
/// <param name="CustomWidth">自定义宽度，仅在 <see cref="PianoRollVideoAspect.Custom"/> 时生效。</param>
/// <param name="CustomHeight">自定义高度，仅在 <see cref="PianoRollVideoAspect.Custom"/> 时生效。</param>
/// <param name="Fps">帧率，限定为 24 到 60。</param>
/// <param name="BitrateKbps">视频码率，单位 kbps。</param>
/// <param name="ShowPortrait">歌手有立绘时是否绘制立绘；默认关闭，画面只保留钢琴卷帘。</param>
/// <param name="IncludeAudio">是否把工程混音封装进视频。</param>
/// <param name="VisibleSeconds">横向视口覆盖的秒数，决定音符滚动的快慢。</param>
public sealed record PianoRollVideoOptions(
    PianoRollVideoAspect Aspect = PianoRollVideoAspect.Portrait,
    int CustomWidth = 1080,
    int CustomHeight = 1920,
    int Fps = 30,
    int BitrateKbps = 8000,
    bool ShowPortrait = false,
    bool IncludeAudio = true,
    double VisibleSeconds = 4.0)
{
    /// <summary>画面边长下限，过小的画面无法容纳琴键与音符。</summary>
    public const int MinSide = 240;

    /// <summary>画面边长上限，超过 4K 的软件光栅化在手机上不可用。</summary>
    public const int MaxSide = 3840;

    /// <summary>解析最终像素尺寸；H.264 要求宽高为偶数，这里向下取偶。</summary>
    public (int Width, int Height) ResolveSize()
    {
        (int w, int h) = Aspect switch
        {
            PianoRollVideoAspect.Landscape => (1920, 1080),
            PianoRollVideoAspect.Portrait => (1080, 1920),
            _ => (CustomWidth, CustomHeight),
        };
        return (Even(w), Even(h));

        static int Even(int value) => Math.Clamp(value, MinSide, MaxSide) & ~1;
    }

    /// <summary>限制过大的帧率与码率，避免编码器拒绝配置。</summary>
    public PianoRollVideoOptions Normalized() => this with
    {
        Fps = Math.Clamp(Fps, 24, 60),
        BitrateKbps = Math.Clamp(BitrateKbps, 1000, 50000),
        VisibleSeconds = Math.Clamp(VisibleSeconds, 1.5, 12.0),
    };
}
