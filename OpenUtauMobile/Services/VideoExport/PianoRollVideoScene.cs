using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenUtauMobile.Services.VideoExport;

/// <summary>视频中的一个音符，时间为从工程起点算起的毫秒。</summary>
/// <param name="StartMs">起点，毫秒。</param>
/// <param name="EndMs">终点，毫秒，不含。</param>
/// <param name="Tone">音高，即 AdjustedTone（含微调的小数半音），与钢琴卷帘同一坐标。</param>
/// <param name="Lyric">歌词。</param>
public readonly record struct VideoNote(double StartMs, double EndMs, float Tone, string Lyric);

/// <summary>最终音高采样；<see cref="Key"/> 为 NaN 表示乐句之间的断点。</summary>
/// <param name="Ms">从工程起点算起的毫秒。</param>
/// <param name="Key">音高，单位为半音，音符中心对应整数。</param>
public readonly record struct VideoPitchSample(double Ms, float Key);

/// <summary>节拍网格线。</summary>
/// <param name="Ms">位置，毫秒。</param>
/// <param name="IsBar">是否为小节线。</param>
public readonly record struct VideoGridLine(double Ms, bool IsBar);

/// <summary>一个乐句的时间范围，用于和声层的淡入淡出。</summary>
/// <param name="StartMs">第一个音符的起点。</param>
/// <param name="EndMs">最后一个音符的终点。</param>
public readonly record struct VideoPhrase(double StartMs, double EndMs);

/// <summary>
/// 按起点排序的音符集合，并预先算好“前缀最大终点”，
/// 这样即使音符有重叠，也能用二分查找准确找到第一个尚未结束的音符。
/// </summary>
public sealed class VideoNoteSet
{
    private readonly double[] _prefixMaxEnd;

    /// <summary>创建集合，输入顺序不限。</summary>
    public VideoNoteSet(IEnumerable<VideoNote> notes)
    {
        Notes = [.. notes.OrderBy(n => n.StartMs).ThenBy(n => n.Tone)];
        _prefixMaxEnd = new double[Notes.Count];
        double max = double.NegativeInfinity;
        for (int i = 0; i < Notes.Count; i++)
        {
            max = Math.Max(max, Notes[i].EndMs);
            _prefixMaxEnd[i] = max;
        }
    }

    /// <summary>按起点排序的音符。</summary>
    public IReadOnlyList<VideoNote> Notes { get; }

    /// <summary>首个终点晚于 <paramref name="ms"/> 的音符下标；都已结束则返回 Count。</summary>
    public int FindFirstEndingAfter(double ms)
    {
        int lo = 0;
        int hi = _prefixMaxEnd.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_prefixMaxEnd[mid] <= ms) lo = mid + 1;
            else hi = mid;
        }

        return lo;
    }
}

/// <summary>
/// 视频中的一条和声层：来自与主分片时间重叠的其它音轨。
/// 它有自己的镜头，淡淡地画在主音轨后面。
/// </summary>
public sealed class VideoHarmonyLayer
{
    /// <summary>该音轨在主分片时间范围内的音符。</summary>
    public required VideoNoteSet Notes { get; init; }

    /// <summary>该音轨的最终音高线采样，含 NaN 断点。</summary>
    public required IReadOnlyList<VideoPitchSample> Curve { get; init; }

    /// <summary>乐句时间范围，按起点排序；间隔很短的乐句已经合并。</summary>
    public required IReadOnlyList<VideoPhrase> Phrases { get; init; }

    /// <summary>该音轨的强调色，0xAARRGGBB。</summary>
    public required uint AccentArgb { get; init; }
}

/// <summary>
/// 钢琴卷帘视频使用的配色，取自编辑器当前主题的语义色，格式均为 0xAARRGGBB。
/// 字段与 PianoRollBackground / PianoKeysCanvas / NotesCanvas / TickBackground 所用的资源一一对应。
/// </summary>
/// <param name="RowWhite">白键行背景，Sem.Color.WhiteKey.Background。</param>
/// <param name="RowBlack">黑键行背景，Sem.Color.BlackKey.Background。</param>
/// <param name="Tonic">工程主音，Sem.Color.CenterKey；行背景以 0.5 不透明度叠加，琴键列则直接填充。</param>
/// <param name="BarLine">小节线。</param>
/// <param name="BeatLine">拍线与白键分隔线，Sem.Color.OutlineVariant。</param>
/// <param name="KeyWhite">琴键列的白键，Sem.Color.WhiteKey。</param>
/// <param name="KeyBlack">琴键列的黑键，Sem.Color.BlackKey。</param>
/// <param name="KeyPressed">琴键被按下时的颜色，Sem.Color.Primary。</param>
/// <param name="LabelWhite">白键上的标签色。</param>
/// <param name="LabelBlack">黑键上的标签色。</param>
/// <param name="LabelTonic">主音键上的标签色，Sem.Color.OnPrimaryContainer。</param>
/// <param name="Lyric">歌词文字，Sem.Color.OnSurface。</param>
/// <param name="PitchPlayed">播放头已扫过的音高线，Sem.Color.OnPrimaryContainer。</param>
/// <param name="Playhead">播放头竖线，Sem.Color.Primary。</param>
public sealed record PianoRollVideoPalette(
    uint RowWhite,
    uint RowBlack,
    uint Tonic,
    uint BarLine,
    uint BeatLine,
    uint KeyWhite,
    uint KeyBlack,
    uint KeyPressed,
    uint LabelWhite,
    uint LabelBlack,
    uint LabelTonic,
    uint Lyric,
    uint PitchPlayed,
    uint Playhead);

/// <summary>
/// 渲染一段钢琴卷帘视频所需的不可变快照。
/// 快照在 UI 线程建立，之后的逐帧渲染只读它，不再触碰工程对象，避免和编辑线程竞争。
/// 所有时间都已换算为毫秒，渲染器因此不依赖工程的速度变化，横向滚动速度恒定。
/// </summary>
public sealed class PianoRollVideoScene
{
    /// <summary>主分片的音符。</summary>
    public required VideoNoteSet Notes { get; init; }

    /// <summary>主分片的音高采样，含 NaN 断点。</summary>
    public required IReadOnlyList<VideoPitchSample> Curve { get; init; }

    /// <summary>和声层，可能为空。</summary>
    public required IReadOnlyList<VideoHarmonyLayer> Harmonies { get; init; }

    /// <summary>按时间排序的拍线与小节线，已按工程拍号与速度换算为毫秒。</summary>
    public required IReadOnlyList<VideoGridLine> GridLines { get; init; }

    /// <summary>工程主音（0 到 11），决定哪一行用主音色。</summary>
    public required int ProjectKey { get; init; }

    /// <summary>视频起点，毫秒。</summary>
    public required double StartMs { get; init; }

    /// <summary>视频终点，毫秒。</summary>
    public required double EndMs { get; init; }

    /// <summary>主音轨的音符颜色，当前主题下该音轨的强调色，0xAARRGGBB。</summary>
    public required uint AccentArgb { get; init; }

    /// <summary>编辑器当前主题配色。</summary>
    public required PianoRollVideoPalette Palette { get; init; }

    /// <summary>歌手立绘的原始编码数据，没有立绘时为空。</summary>
    public byte[]? PortraitData { get; init; }

    /// <summary>立绘不透明度，0 到 1。</summary>
    public float PortraitOpacity { get; init; }
}
