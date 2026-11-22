using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime.Generation;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime.Resources;
using Serilog;

namespace OpenUtauMobile.Services.VideoExport;

/// <summary>从工程分片建立 <see cref="PianoRollVideoScene"/> 快照。</summary>
public static class PianoRollVideoSceneBuilder
{
    /// <summary>音高采样间隔，与 RenderPhrase 及钢琴卷帘 NotesCanvas 的最终音高线一致。</summary>
    private const int PitchSampleInterval = 5;

    /// <summary>视频前后各留出的片头片尾，单位毫秒。</summary>
    private const double PaddingMs = 1000;

    /// <summary>视口横向最多覆盖 12 秒，快照在视频范围外再多留这么多，保证边缘也有网格与音符。</summary>
    private const double ViewMarginMs = 12_000;

    /// <summary>最多同时显示的和声层数，更多会让画面过于杂乱。</summary>
    private const int MaxHarmonyLayers = 3;

    /// <summary>和声乐句之间短于这个间隔就合并，避免短休止造成反复淡入淡出。</summary>
    private const double PhraseMergeGapMs = 600;

    /// <summary>网格线数量上限，防止极端速度或拍号造成无限循环。</summary>
    private const int MaxGridLines = 50_000;

    /// <summary>应在 UI 线程调用：读取分片、歌手立绘、主题配色与时间轴。</summary>
    /// <param name="project">当前工程。</param>
    /// <param name="part">要录制的人声分片。</param>
    /// <param name="includePortrait">是否读取立绘。</param>
    /// <param name="coverWholeProject">为真时画面一直持续到工程最后一个分片结束，使网格覆盖比人声更长的背景音乐。</param>
    public static PianoRollVideoScene Build(UProject project, UVoicePart part, bool includePortrait,
        bool coverWholeProject = false)
    {
        TimeAxis axis = project.timeAxis;
        UTrack track = project.tracks[part.trackNo];
        USinger? singer = track.Singer;

        double startMs = Math.Max(0, axis.TickPosToMsPos(part.position) - PaddingMs);
        int endTick = coverWholeProject ? Math.Max(part.End, project.EndTick) : part.End;
        double endMs = axis.TickPosToMsPos(endTick) + PaddingMs;
        double windowStart = startMs - ViewMarginMs;
        double windowEnd = endMs + ViewMarginMs;

        VideoNoteSet notes = new(CollectNotes(axis, part, double.NegativeInfinity, double.PositiveInfinity));
        List<VideoPitchSample> curve = BuildPitchCurve(project, [part]);
        List<VideoHarmonyLayer> harmonies = BuildHarmonies(project, part, windowStart, windowEnd);
        List<VideoGridLine> grid = BuildGridLines(project, startMs - ViewMarginMs, endMs + ViewMarginMs);

        byte[]? portrait = null;
        float opacity = 0;
        if (includePortrait && singer != null)
        {
            try
            {
                portrait = singer.LoadPortrait();
                opacity = singer.PortraitOpacity;
            }
            catch (Exception ex)
            {
                // 立绘读取失败不应让整次导出失败。
                Log.Warning(ex, "钢琴卷帘视频：读取歌手立绘失败");
            }
        }

        return new PianoRollVideoScene
        {
            Notes = notes,
            Curve = curve,
            Harmonies = harmonies,
            GridLines = grid,
            ProjectKey = project.key,
            StartMs = startMs,
            EndMs = endMs,
            // 与 NotesCanvas 一致：音符使用当前主题变体下的音轨强调色。
            AccentArgb = ToArgb(TrackPalette.GetTrackColor(track.TrackColor).AccentColor.Color),
            Palette = BuildPalette(),
            PortraitData = portrait is { Length: > 0 } ? portrait : null,
            PortraitOpacity = Math.Clamp(opacity, 0f, 1f),
        };
    }

    #region 音符、网格与和声

    /// <summary>取出分片内与 [fromMs, toMs] 有交集的音符，时间换算为毫秒。</summary>
    private static List<VideoNote> CollectNotes(TimeAxis axis, UVoicePart part, double fromMs, double toMs)
    {
        List<VideoNote> notes = [];
        lock (part)
        {
            foreach (UNote? note in part.notes)
            {
                if (note == null) continue;
                double start = axis.TickPosToMsPos(part.position + note.position);
                double end = axis.TickPosToMsPos(part.position + note.End);
                if (end < fromMs || start > toMs) continue;
                notes.Add(new VideoNote(start, end, note.AdjustedTone, note.lyric ?? string.Empty));
            }
        }

        return notes;
    }

    /// <summary>
    /// 找出与主分片时间重叠、且未静音的其它音轨，把它们作为和声层。
    /// 离主音轨越近的音轨越优先，最多 <see cref="MaxHarmonyLayers"/> 条。
    /// </summary>
    private static List<VideoHarmonyLayer> BuildHarmonies(UProject project, UVoicePart main, double windowStart,
        double windowEnd)
    {
        TimeAxis axis = project.timeAxis;
        List<VideoHarmonyLayer> layers = [];
        IEnumerable<IGrouping<int, UVoicePart>> groups = project.parts
            .OfType<UVoicePart>()
            .Where(p => !ReferenceEquals(p, main)
                        && p.trackNo != main.trackNo
                        && p.trackNo >= 0 && p.trackNo < project.tracks.Count
                        && p.position < main.End && p.End > main.position)
            .GroupBy(p => p.trackNo)
            .Where(g => !project.tracks[g.Key].Muted)
            .OrderBy(g => Math.Abs(g.Key - main.trackNo))
            .ThenBy(g => g.Key)
            .Take(MaxHarmonyLayers);

        foreach (IGrouping<int, UVoicePart> group in groups)
        {
            List<UVoicePart> parts = [.. group.OrderBy(p => p.position)];
            List<VideoNote> notes = [];
            foreach (UVoicePart p in parts)
            {
                notes.AddRange(CollectNotes(axis, p, windowStart, windowEnd));
            }

            if (notes.Count == 0) continue;
            VideoNoteSet set = new(notes);
            List<VideoPitchSample> curve = BuildPitchCurve(project, parts)
                .Where(sample => sample.Ms >= windowStart && sample.Ms <= windowEnd)
                .ToList();
            layers.Add(new VideoHarmonyLayer
            {
                Notes = set,
                Curve = curve,
                Phrases = BuildPhrases(set),
                AccentArgb = ToArgb(TrackPalette.GetTrackColor(project.tracks[group.Key].TrackColor).AccentColor.Color),
            });
        }

        return layers;
    }

    /// <summary>把相邻且间隔很短的音符并成乐句。</summary>
    private static List<VideoPhrase> BuildPhrases(VideoNoteSet set)
    {
        List<VideoPhrase> phrases = [];
        double start = 0;
        double end = 0;
        bool open = false;
        foreach (VideoNote note in set.Notes)
        {
            if (open && note.StartMs - end <= PhraseMergeGapMs)
            {
                end = Math.Max(end, note.EndMs);
                continue;
            }

            if (open) phrases.Add(new VideoPhrase(start, end));
            start = note.StartMs;
            end = note.EndMs;
            open = true;
        }

        if (open) phrases.Add(new VideoPhrase(start, end));
        return phrases;
    }

    /// <summary>
    /// 沿工程的拍号与速度逐拍生成网格线，直接换算成毫秒，
    /// 这样渲染器可以用恒定的毫秒比例滚动，而网格线仍落在正确的拍点上。
    /// </summary>
    private static List<VideoGridLine> BuildGridLines(UProject project, double fromMs, double toMs)
    {
        TimeAxis axis = project.timeAxis;
        List<VideoGridLine> lines = [];
        int startTick = Math.Max(0, axis.MsPosToTickPos(Math.Max(0, fromMs)));
        axis.TickPosToBarBeat(startTick, out int bar, out int beat, out _);
        // 起点之前的拍点不计入 lines，所以另设迭代上限。
        for (int guard = 0; guard < MaxGridLines * 2 && lines.Count < MaxGridLines; guard++)
        {
            int tick = axis.BarBeatToTickPos(bar, beat);
            double ms = axis.TickPosToMsPos(tick);
            if (ms > toMs) break;
            if (ms >= fromMs) lines.Add(new VideoGridLine(ms, beat == 0));
            axis.NextBarBeat(bar, beat, out bar, out beat);
        }

        return lines;
    }

    #endregion

    #region 主题配色

    /// <summary>读取编辑器当前主题的语义色，取色规则与各个钢琴卷帘控件保持一致。</summary>
    private static PianoRollVideoPalette BuildPalette()
    {
        bool dark = ThemeResources.IsDarkMode;
        static uint Get(string key) => ToArgb(ThemeResources.GetColor(key));
        return new PianoRollVideoPalette(
            RowWhite: Get("Sem.Color.WhiteKey.Background"),
            RowBlack: Get("Sem.Color.BlackKey.Background"),
            Tonic: Get("Sem.Color.CenterKey"),
            BarLine: Get(dark ? "Sem.Color.Primary" : "Sem.Color.Secondary"), // 同 Sem.Pen.Grid.Bar
            BeatLine: Get("Sem.Color.OutlineVariant"),
            KeyWhite: Get("Sem.Color.WhiteKey"),
            KeyBlack: Get("Sem.Color.BlackKey"),
            KeyPressed: Get("Sem.Color.Primary"),
            // 标签色规则同 PianoKeysCanvas：深色主题的白键底色就是 Outline，需要深色文字。
            LabelWhite: Get(dark ? "Sem.Color.BlackKey" : "Sem.Color.OnSurface"),
            LabelBlack: Get("Sem.Color.WhiteKey"),
            LabelTonic: Get("Sem.Color.OnPrimaryContainer"),
            Lyric: Get("Sem.Color.OnSurface"),
            PitchPlayed: Get("Sem.Color.OnPrimaryContainer"),
            Playhead: Get("Sem.Color.Primary"));
    }

    private static uint ToArgb(Color color) => (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);

    #endregion

    #region 最终音高曲线

    /// <summary>
    /// 直接由音符数据计算最终音高线，算法与 <c>RenderPhrase</c> 的音高构建一致：
    /// 先铺平音符音高，再叠加颤音，再叠加音高锚点（相对前一个音符的基准），最后叠加 PITD 曲线。
    /// 不读取 <c>UVoicePart.renderPhrases</c>：乐句由后台异步构建，导出时可能为空或已过期，
    /// 一旦取不到就只剩一条平线，所以这里自己算，结果只取决于音符本身。
    /// </summary>
    private static List<VideoPitchSample> BuildPitchCurve(UProject project, IEnumerable<UVoicePart> parts)
    {
        TimeAxis axis = project.timeAxis;
        List<TickSample> ticks = [];
        int lastTick = int.MinValue;
        foreach (UVoicePart part in parts.OrderBy(p => p.position))
        {
            UNote[] notes;
            UCurve? pitd;
            lock (part)
            {
                notes = [.. part.notes.OrderBy(n => n.position)];
                pitd = part.curves.Find(c => c.abbr == OpenUtau.Core.Format.Ustx.PITD);
            }

            if (notes.Length == 0) continue;

            // 间隔不超过半个四分音符的音符视为同一乐句，乐句之间断开曲线。
            int maxGap = Math.Max(1, project.resolution / 2);
            int runStart = 0;
            for (int i = 1; i <= notes.Length; i++)
            {
                if (i < notes.Length && notes[i].position - notes[i - 1].End <= maxGap) continue;
                AppendRun(project, axis, part, notes, runStart, i, pitd, ticks, ref lastTick);
                runStart = i;
            }
        }

        List<VideoPitchSample> samples = new(ticks.Count);
        foreach (TickSample t in ticks)
        {
            samples.Add(new VideoPitchSample(axis.TickPosToMsPos(t.Tick), t.Key));
        }

        return samples;
    }

    /// <summary>以 Tick 为单位的中间采样，换算成毫秒之前使用。</summary>
    private readonly record struct TickSample(int Tick, float Key);

    /// <summary>计算 [first, end) 这一段连续音符的音高并追加到 <paramref name="samples"/>。</summary>
    private static void AppendRun(UProject project, TimeAxis axis, UVoicePart part, UNote[] notes, int first, int end,
        UCurve? pitd, List<TickSample> samples, ref int lastTick)
    {
        const int interval = PitchSampleInterval;
        UNote firstNote = notes[first];
        UNote lastNote = notes[end - 1];

        // 音高锚点可以落在音符起点之前（滑入），曲线要从最早的锚点开始。
        int pitchStart = firstNote.position;
        if (firstNote.pitch.data.Count > 0)
        {
            double nodeMs = axis.TickPosToMsPos(part.position + firstNote.position);
            int firstPointTick = axis.MsPosToTickPos(nodeMs + firstNote.pitch.data[0].X) - part.position;
            pitchStart = Math.Min(pitchStart, firstPointTick);
        }

        int length = (lastNote.End - pitchStart) / interval + 1;
        if (length < 2) return;
        float[] pitches = new float[length];

        // 1. 铺平：每个音符的音高。
        int index = 0;
        for (int n = first; n < end; n++)
        {
            UNote note = notes[n];
            while (pitchStart + index * interval < note.End && index < length)
            {
                pitches[index] = note.AdjustedTone * 100;
                index++;
            }
        }

        index = Math.Max(1, index);
        while (index < length)
        {
            pitches[index] = pitches[index - 1];
            index++;
        }

        // 2. 颤音：覆盖音符范围内的采样。
        for (int n = first; n < end; n++)
        {
            UNote note = notes[n];
            if (note.vibrato.length <= 0 || note.duration <= 0) continue;
            double durationMs = axis.MsBetweenTickPos(part.position + note.position, part.position + note.End);
            if (durationMs <= 0) continue;
            int startIndex = Math.Max(0, (int)Math.Ceiling((float)(note.position - pitchStart) / interval));
            int endIndex = Math.Min(length, (note.End - pitchStart) / interval);
            float nPeriod = (float)(note.vibrato.period / durationMs);
            for (int i = startIndex; i < endIndex; i++)
            {
                float nPos = (float)(pitchStart + i * interval - note.position) / note.duration;
                pitches[i] = note.vibrato.Evaluate(nPos, nPeriod, note).Y * 100;
            }
        }

        // 3. 音高锚点：叠加相对基准音高的偏移。
        for (int n = first; n < end; n++)
        {
            UNote note = notes[n];
            UNote? prev = n > 0 ? notes[n - 1] : null;
            double nodePosMs = axis.TickPosToMsPos(part.position + note.position);
            List<PitchPoint> points = [];
            foreach (PitchPoint p in note.pitch.data)
            {
                points.Add(new PitchPoint(axis.MsPosToTickPos(nodePosMs + p.X) - part.position,
                    p.Y * 10 + note.AdjustedTone * 100, p.shape));
            }

            if (points.Count == 0)
            {
                points.Add(new PitchPoint(note.position, note.AdjustedTone * 100, PitchPointShape.io, true));
                points.Add(new PitchPoint(note.End, note.AdjustedTone * 100, PitchPointShape.io, true));
            }

            if (n == first && points[0].X > pitchStart)
            {
                points.Insert(0, new PitchPoint(pitchStart, points[0].Y, PitchPointShape.io, true));
            }
            else if (points[0].X > note.position)
            {
                points.Insert(0, new PitchPoint(note.position, points[0].Y, PitchPointShape.io, true));
            }

            if (points[^1].X < note.End)
            {
                points.Add(new PitchPoint(note.End, points[^1].Y, PitchPointShape.io, true));
            }

            index = Math.Max(0, (int)((points[0].X - pitchStart) / interval));
            for (int i = 0; i < points.Count - 1; i++)
            {
                PitchPoint point0 = points[i];
                PitchPoint point1 = points[i + 1];
                PitchPoint pointBefore = i == 0 ? points[i] : points[i - 1];
                PitchPoint pointAfter = i >= points.Count - 2 ? points[i + 1] : points[i + 2];
                bool spline = note.pitch.data.Count > 2 && point0.shape == PitchPointShape.sp && !point1.autoCompleted;
                CubicSplineSegment? curve = spline
                    ? new CubicSplineSegment(pointBefore.X, pointBefore.Y, point0.X, point0.Y, point1.X, point1.Y,
                        pointAfter.X, pointAfter.Y)
                    : null;
                int x = pitchStart + index * interval;
                while (x < point1.X && index < length)
                {
                    double pitch = curve != null
                        ? curve.GetY(x)
                        : MusicMath.InterpolateShape(point0.X, point1.X, point0.Y, point1.Y, x, point0.shape);
                    // 与前一个音符重叠的部分以前一个音符为基准，保证滑音从上一个音出发。
                    float basePitch = prev != null && x < prev.End ? prev.AdjustedTone * 100 : note.AdjustedTone * 100;
                    pitches[index] += (float)pitch - basePitch;
                    index++;
                    x += interval;
                }
            }
        }

        // 4. 音高偏移曲线 PITD。
        if (pitd != null && pitd.xs.Count > 0 && pitd.xs.Count == pitd.ys.Count)
        {
            for (int i = 0; i < length; i++)
            {
                pitches[i] += SampleCurve(pitd, pitchStart + i * interval);
            }
        }

        bool wroteAny = false;
        for (int i = 0; i < length; i++)
        {
            int tick = part.position + pitchStart + i * interval;
            // 相邻乐句的前导区可能重叠，保持 Tick 严格递增。
            if (tick <= lastTick) continue;
            samples.Add(new TickSample(tick, pitches[i] / 100f));
            lastTick = tick;
            wroteAny = true;
        }

        if (wroteAny)
        {
            samples.Add(new TickSample(lastTick + 1, float.NaN));
            lastTick++;
        }
    }

    /// <summary>按分片相对 Tick 采样曲线，节点之间线性插值，节点范围之外为 0（PITD 的默认值）。</summary>
    private static float SampleCurve(UCurve curve, int x)
    {
        int idx = curve.xs.BinarySearch(x);
        if (idx >= 0) return curve.ys[idx];
        idx = ~idx;
        if (idx > 0 && idx < curve.xs.Count)
        {
            return (float)MusicMath.Linear(curve.xs[idx - 1], curve.xs[idx], curve.ys[idx - 1], curve.ys[idx], x);
        }

        return 0;
    }

    #endregion
}
