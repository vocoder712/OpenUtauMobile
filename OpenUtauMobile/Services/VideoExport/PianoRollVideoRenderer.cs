using System;
using System.Collections.Generic;
using OpenUtauMobile.Controls;
using SkiaSharp;

namespace OpenUtauMobile.Services.VideoExport;

/// <summary>
/// 钢琴卷帘视频的逐帧渲染器，用 SkiaSharp 直接绘制，不依赖 Avalonia 的可视树，因此可在后台线程运行。
/// <para>
/// 画面只有钢琴卷帘本身：左侧琴键列 + 右侧音符区，不含标尺、音素面板、走带控制等其他界面。
/// 绘制规则逐项对应编辑器里的控件：
/// PianoRollBackground（黑白键行、主音行、B–C / E–F 分隔线）、TickBackground（小节线与拍线）、
/// NotesCanvas（圆角音符、歌词、最终音高线）、PianoRollPlayPosCanvas（播放头与拖影）、
/// PianoKeysCanvas（琴键列与标签）。配色来自场景里的主题快照。
/// </para>
/// <para>
/// 与编辑器不同的只有两点：播放头会随时间推进，所以音符和音高线向左滚动；
/// 音高线只显示播放头已扫过的部分，正在发声的音符加描边、对应琴键按下。
/// </para>
/// <para>
/// 和声层：与主分片重叠的其它音轨会以淡色画在主音符后面。每条和声层有自己的镜头：
/// 和声音高落在主镜头视野内时与主镜头完全对齐（行与琴键对得上），超出视野时改为跟随自己的音高，
/// 这样过高或过低的和声也不会掉出画面。和声音符与音高线整段显示，没有扫过的截断，也没有播放头圆点；
/// 乐句开始前淡入，临近结束时淡出。
/// </para>
/// <para>所有横向位置按毫秒计算，滚动速度恒定，不受工程速度变化影响。帧必须按时间顺序调用 <see cref="Render"/>，镜头位置是逐帧累积的状态。</para>
/// </summary>
public sealed class PianoRollVideoRenderer : IDisposable
{
    /// <summary>播放头在音符区内的横向位置比例，与 PianoRollViewModel 的默认播放标记位置一致。</summary>
    private const float PlayheadRatio = 0.5f;

    /// <summary>镜头向目标音高靠拢的速度，值越大越快。</summary>
    private const double CameraSharpness = 8;

    /// <summary>音符开始前多少毫秒镜头就开始转向它。</summary>
    private const double CameraLookaheadMs = 150;

    /// <summary>和声层与主镜头对齐时的追随速度倍数，越大越贴近主镜头。</summary>
    private const double HarmonyAlignBoost = 3;

    /// <summary>和声音符的不透明度上限。</summary>
    private const float HarmonyNoteAlpha = 0.34f;

    /// <summary>和声音高线的不透明度上限。</summary>
    private const float HarmonyLineAlpha = 0.55f;

    /// <summary>和声乐句开始前的淡入时长，毫秒。</summary>
    private const double HarmonyFadeInMs = 500;

    /// <summary>和声乐句结束前开始淡出的提前量，毫秒。</summary>
    private const double HarmonyFadeOutLeadMs = 300;

    /// <summary>和声乐句结束后继续淡出的时长，毫秒。</summary>
    private const double HarmonyFadeOutTailMs = 200;

    /// <summary>编辑器中音符行高的设计值（dp），其余尺寸按它等比换算。</summary>
    private const float DesignKeyHeight = 32f;

    /// <summary>音符上下各内缩的设计值（NotesCanvas 的 Deflate(0, 4)）。</summary>
    private const float DesignNotePadding = 4f;

    /// <summary>音符圆角半径的设计值（NotesCanvas.NoteBodyCornerRadius）。</summary>
    private const float DesignNoteRadius = 8f;

    private readonly PianoRollVideoScene _scene;
    private readonly PianoRollVideoPalette _palette;
    private readonly int _width;
    private readonly int _height;
    private readonly float _keysWidth;
    private readonly float _keyHeight;
    private readonly float _visibleKeys;
    private readonly float _scale;
    private readonly float _playheadX;
    private readonly float _pxPerMs;
    private readonly SKColor _accent;
    private readonly SKBitmap? _portrait;
    private readonly Dictionary<char, SKTypeface> _typefaces = [];
    private readonly List<HarmonyState> _harmonies = [];
    private double _cameraKey;
    private bool _cameraReady;

    /// <summary>一条和声层的镜头状态。</summary>
    private sealed class HarmonyState(VideoHarmonyLayer layer)
    {
        public VideoHarmonyLayer Layer { get; } = layer;

        /// <summary>该层镜头中心，半音坐标。</summary>
        public double CameraKey { get; set; }

        public bool Ready { get; set; }

        /// <summary>当前是否与主镜头对齐。</summary>
        public bool Aligned { get; set; }
    }

    /// <summary>创建渲染器。</summary>
    /// <param name="scene">场景快照。</param>
    /// <param name="options">导出参数。</param>
    public PianoRollVideoRenderer(PianoRollVideoScene scene, PianoRollVideoOptions options)
    {
        _scene = scene;
        _palette = scene.Palette;
        (_width, _height) = options.ResolveSize();
        // 琴键列约占画面宽度的 7%，与手机编辑器里的比例接近。
        _keysWidth = MathF.Max(48, _width * 0.069f);
        // 竖屏显示更多半音行，横屏行更少、更宽松。
        _keyHeight = _height >= _width ? _height / 30f : _height / 16f;
        _visibleKeys = _height / _keyHeight;
        _scale = _keyHeight / DesignKeyHeight;
        _playheadX = _keysWidth + (_width - _keysWidth) * PlayheadRatio;
        // 横向按毫秒定比例：滚动速度恒定，网格线与音符的毫秒位置已在快照里按工程速度换算好。
        _pxPerMs = (float)((_width - _keysWidth) / (options.VisibleSeconds * 1000.0));
        foreach (VideoHarmonyLayer layer in scene.Harmonies) _harmonies.Add(new HarmonyState(layer));
        _accent = new SKColor(scene.AccentArgb);
        if (options.ShowPortrait && scene.PortraitData != null && scene.PortraitOpacity > 0)
        {
            _portrait = SKBitmap.Decode(scene.PortraitData);
        }
    }

    /// <summary>输出宽度。</summary>
    public int Width => _width;

    /// <summary>输出高度。</summary>
    public int Height => _height;

    /// <summary>绘制一帧。</summary>
    /// <param name="canvas">目标画布，尺寸应为 <see cref="Width"/>×<see cref="Height"/>。</param>
    /// <param name="nowMs">此帧的播放位置，从工程起点算起的毫秒。</param>
    /// <param name="dtSeconds">与上一帧的间隔，用于镜头平滑。</param>
    public void Render(SKCanvas canvas, double nowMs, double dtSeconds)
    {
        UpdateCamera(nowMs, dtSeconds);
        UpdateHarmonyCameras(nowMs, dtSeconds);
        float top = CameraTop();

        DrawRowBackground(canvas, top);
        canvas.Save();
        canvas.ClipRect(new SKRect(_keysWidth, 0, _width, _height));
        DrawBeforeProjectStart(canvas, nowMs);
        DrawGridLines(canvas, nowMs);
        DrawPortrait(canvas);
        DrawHarmonies(canvas, nowMs);
        DrawNotes(canvas, nowMs, top);
        DrawPitchCurve(canvas, nowMs, top);
        DrawPlayhead(canvas);
        canvas.Restore();
        DrawKeys(canvas, nowMs, top);
    }

    /// <summary>释放立绘位图与字体缓存。</summary>
    public void Dispose()
    {
        _portrait?.Dispose();
        foreach (SKTypeface typeface in _typefaces.Values) typeface.Dispose();
        _typefaces.Clear();
    }

    #region 坐标

    /// <summary>音高转到“自上而下的半音坐标”，音符中心对应 MaxTone - 0.5 - tone，与 PianoRollPlaybackFollow 一致。</summary>
    private static double ToneToKey(double tone) => ViewConstants.MaxTone - 0.5 - tone;

    private float CameraTop() => TopFor(_cameraKey);

    /// <summary>镜头中心换算为视口顶边的半音坐标，限制在音域内。</summary>
    private float TopFor(double cameraKey) =>
        (float)Math.Clamp(cameraKey - _visibleKeys / 2.0, 0, ViewConstants.MaxTone - _visibleKeys);

    /// <summary>某一行半音的顶边 Y，对应 TickPitchToPoint 里 (MaxTone - 1 - tone - KeyOffset) * KeyHeight。</summary>
    private float RowTop(int tone, float top) => (float)((ViewConstants.MaxTone - 1 - tone - top) * _keyHeight);

    /// <summary>半音坐标（行中心对应整数 tone）转 Y。</summary>
    private float KeyToY(double key, float top) => (float)((key - top) * _keyHeight);

    private float TimeToX(double ms, double nowMs) => _playheadX + (float)((ms - nowMs) * _pxPerMs);

    private double LeftMs(double nowMs) => nowMs - (_playheadX - _keysWidth) / _pxPerMs;

    private double RightMs(double nowMs) => nowMs + (_width - _playheadX) / _pxPerMs;

    private static SKColor Color(uint argb) => new(argb);

    #endregion

    #region 镜头

    /// <summary>每帧把镜头向当前或即将到来的音符中心推进一步，休止时保持不动。</summary>
    private void UpdateCamera(double nowMs, double dtSeconds)
    {
        IReadOnlyList<VideoNote> notes = _scene.Notes.Notes;
        double? target = null;
        int index = _scene.Notes.FindFirstEndingAfter(nowMs);
        if (index < notes.Count && notes[index].StartMs <= nowMs + CameraLookaheadMs)
        {
            target = ToneToKey(notes[index].Tone);
        }

        if (!_cameraReady)
        {
            // 第一帧直接取第一个音符，避免片头从音域边缘滑入。
            double initial = notes.Count > 0 ? ToneToKey(notes[Math.Min(index, notes.Count - 1)].Tone) : ToneToKey(60);
            _cameraKey = target ?? initial;
            _cameraReady = true;
            return;
        }

        if (target == null) return;
        double alpha = 1 - Math.Exp(-CameraSharpness * Math.Max(0, dtSeconds));
        _cameraKey += (target.Value - _cameraKey) * alpha;
    }

    /// <summary>
    /// 每条和声层有独立镜头。目标音高落在主镜头视野内时，该层贴合主镜头，行与琴键对齐；
    /// 否则该层跟随自己的音高。进入视野用较宽的边距、离开视野用较窄的边距，避免在边界上来回切换。
    /// </summary>
    private void UpdateHarmonyCameras(double nowMs, double dtSeconds)
    {
        float mainTop = CameraTop();
        double dt = Math.Max(0, dtSeconds);
        foreach (HarmonyState h in _harmonies)
        {
            VideoNoteSet set = h.Layer.Notes;
            IReadOnlyList<VideoNote> notes = set.Notes;
            int index = set.FindFirstEndingAfter(nowMs);
            double? target = null;
            if (index < notes.Count && notes[index].StartMs <= nowMs + CameraLookaheadMs)
            {
                target = ToneToKey(notes[index].Tone);
            }

            if (!h.Ready)
            {
                double initial = target ?? ToneToKey(notes[Math.Min(index, notes.Count - 1)].Tone);
                h.Aligned = IsInView(initial, mainTop, 1.5);
                h.CameraKey = h.Aligned ? _cameraKey : initial;
                h.Ready = true;
                continue;
            }

            if (target == null)
            {
                // 没有目标时：已对齐的层继续贴着主镜头，未对齐的层保持不动。
                if (h.Aligned) h.CameraKey = _cameraKey;
                continue;
            }

            h.Aligned = IsInView(target.Value, mainTop, h.Aligned ? 0.25 : 1.5);
            double desired = h.Aligned ? _cameraKey : target.Value;
            double sharpness = h.Aligned ? CameraSharpness * HarmonyAlignBoost : CameraSharpness;
            double alpha = 1 - Math.Exp(-sharpness * dt);
            h.CameraKey += (desired - h.CameraKey) * alpha;
            if (h.Aligned && Math.Abs(h.CameraKey - _cameraKey) < 0.02) h.CameraKey = _cameraKey;
        }
    }

    private bool IsInView(double key, float top, double margin) => key >= top + margin && key <= top + _visibleKeys - margin;

    #endregion

    #region 背景与网格

    /// <summary>对应 PianoRollBackground：白键底色 + 黑键行 + 主音行（0.5 不透明度）+ B–C / E–F 分隔线。</summary>
    private void DrawRowBackground(SKCanvas canvas, float top)
    {
        canvas.Clear(Color(_palette.RowWhite));

        using SKPaint fill = new() { Style = SKPaintStyle.Fill };
        using SKPaint separator = new() { Color = Color(_palette.BeatLine), StrokeWidth = 1, IsAntialias = false };
        int topTone = (int)Math.Ceiling(ViewConstants.MaxTone - 1 - top);
        int bottomTone = (int)Math.Floor(ViewConstants.MaxTone - 1 - top - _visibleKeys);
        for (int tone = Math.Min(topTone, ViewConstants.MaxTone - 1); tone >= Math.Max(0, bottomTone); tone--)
        {
            float y = RowTop(tone, top);
            bool isBlack = IsBlackKey(tone);
            bool isTonic = IsTonic(tone);
            if (isBlack || isTonic)
            {
                fill.Color = isTonic
                    ? Color(_palette.Tonic).WithAlpha(128) // 主音行以 0.5 不透明度叠加
                    : Color(_palette.RowBlack);
                canvas.DrawRect(_keysWidth, y, _width - _keysWidth, _keyHeight, fill);
            }

            // 仅绘制 B–C 和 E–F 两类相邻白键边界。
            if (tone > 0 && !isBlack && !IsBlackKey(tone - 1))
            {
                float lineY = MathF.Round(y + _keyHeight) + 0.5f;
                canvas.DrawLine(_keysWidth, lineY, _width, lineY, separator);
            }
        }
    }

    /// <summary>工程起点（Tick 0）之前的区域画斜纹，提示这里没有时间轴。</summary>
    private void DrawBeforeProjectStart(SKCanvas canvas, double nowMs)
    {
        float zeroX = TimeToX(0, nowMs);
        if (zeroX <= _keysWidth) return;
        float right = MathF.Min(zeroX, _width);
        canvas.Save();
        canvas.ClipRect(new SKRect(_keysWidth, 0, right, _height));
        using SKPaint stripe = new()
        {
            Color = Color(_palette.Playhead).WithAlpha(26),
            StrokeWidth = 2 * _scale,
            IsAntialias = true,
        };
        float step = 12 * _scale;
        for (float x = _keysWidth - _height; x < right; x += step)
        {
            canvas.DrawLine(x, _height, x + _height, 0, stripe);
        }

        canvas.Restore();
    }

    /// <summary>对应 TickBackground：小节线用强调色，拍线用 OutlineVariant。</summary>
    private void DrawGridLines(SKCanvas canvas, double nowMs)
    {
        IReadOnlyList<VideoGridLine> lines = _scene.GridLines;
        double leftMs = LeftMs(nowMs);
        double rightMs = RightMs(nowMs);
        using SKPaint bar = new() { Color = Color(_palette.BarLine), StrokeWidth = 1.5f * _scale, IsAntialias = false };
        using SKPaint beat = new() { Color = Color(_palette.BeatLine), StrokeWidth = MathF.Max(1, _scale), IsAntialias = false };

        // 二分查找第一条不早于左边缘的网格线。
        int lo = 0;
        int hi = lines.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (lines[mid].Ms < leftMs) lo = mid + 1;
            else hi = mid;
        }

        for (int i = lo; i < lines.Count && lines[i].Ms <= rightMs; i++)
        {
            float x = MathF.Round(TimeToX(lines[i].Ms, nowMs)) + 0.5f;
            canvas.DrawLine(x, 0, x, _height, lines[i].IsBar ? bar : beat);
        }
    }

    private void DrawPortrait(SKCanvas canvas)
    {
        if (_portrait == null) return;
        float areaWidth = _width - _keysWidth;
        float scale = MathF.Min(_height * 0.92f / _portrait.Height, areaWidth * 0.9f / _portrait.Width);
        float w = _portrait.Width * scale;
        float h = _portrait.Height * scale;
        // 竖屏居中，横屏靠右，与编辑器里立绘靠右的摆放习惯一致。
        float left = _height >= _width ? _keysWidth + (areaWidth - w) / 2 : _width - w;
        using SKPaint paint = new()
        {
            IsAntialias = true,
            Color = SKColors.White.WithAlpha((byte)(_scene.PortraitOpacity * 255)),
        };
        canvas.DrawBitmap(_portrait, new SKRect(left, _height - h, left + w, _height), paint);
    }

    #endregion

    #region 音符

    /// <summary>对应 NotesCanvas.RenderNoteBody：0.88 不透明度的圆角矩形，歌词画在音符上方一行。</summary>
    private void DrawNotes(SKCanvas canvas, double nowMs, float top)
    {
        IReadOnlyList<VideoNote> notes = _scene.Notes.Notes;
        double leftMs = LeftMs(nowMs);
        double rightMs = RightMs(nowMs);
        float pad = DesignNotePadding * _scale;
        float radius = DesignNoteRadius * _scale;
        using SKPaint body = new() { IsAntialias = true, Color = _accent.WithAlpha((byte)(0.88f * 255)) };
        using SKPaint outline = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f * _scale,
            Color = Color(_palette.Playhead),
        };
        using SKPaint lyricPaint = new() { IsAntialias = true, Color = Color(_palette.Lyric) };
        using SKFont font = new() { Size = _keyHeight * 0.85f };

        for (int i = _scene.Notes.FindFirstEndingAfter(leftMs); i < notes.Count; i++)
        {
            VideoNote note = notes[i];
            if (note.StartMs > rightMs) break;
            float x0 = TimeToX(note.StartMs, nowMs) + 1;
            float x1 = TimeToX(note.EndMs, nowMs);
            float rowTop = (float)((ViewConstants.MaxTone - 1 - note.Tone - top) * _keyHeight);
            SKRect rect = new(x0, rowTop + pad, MathF.Max(x1, x0 + 3), rowTop + _keyHeight - pad);
            canvas.DrawRoundRect(rect, radius, radius, body);

            // 正在发声的音符用编辑器“选中”样式的描边标出。
            if (nowMs >= note.StartMs && nowMs < note.EndMs)
            {
                canvas.DrawRoundRect(rect, radius, radius, outline);
            }

            DrawLyric(canvas, note, x0, x1 - x0, rowTop, font, lyricPaint);
        }
    }

    /// <summary>
    /// 歌词位置与省略规则同 NotesCanvas：画在音符所在行的上一行，左侧留 5dp；
    /// 放不下时缩成“首字..”，仍放不下就不画。
    /// </summary>
    private void DrawLyric(SKCanvas canvas, VideoNote note, float x0, float noteWidth, float rowTop, SKFont font,
        SKPaint paint)
    {
        if (note.Lyric.Length == 0) return;
        font.Typeface = GetTypeface(note.Lyric[0]);
        float padding = 5 * _scale;
        string text = note.Lyric;
        if (font.MeasureText(text) + padding > noteWidth)
        {
            text = note.Lyric[0] + "..";
            if (font.MeasureText(text) + padding > noteWidth) return;
        }

        SKFontMetrics metrics = font.Metrics;
        float boxTop = rowTop - _keyHeight;
        float baseline = boxTop + (_keyHeight - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent;
        canvas.DrawText(text, x0 + padding, MathF.Round(baseline), font, paint);
    }

    /// <summary>歌词可能含中日文，按首字符向系统字体管理器请求回退字体并缓存。</summary>
    private SKTypeface GetTypeface(char first)
    {
        if (_typefaces.TryGetValue(first, out SKTypeface? cached)) return cached;
        SKTypeface typeface = SKFontManager.Default.MatchCharacter(first) ?? SKTypeface.Default;
        _typefaces[first] = typeface;
        return typeface;
    }

    #endregion

    #region 音高曲线

    /// <summary>
    /// 对应 NotesCanvas.RenderFinalPitch 的最终音高折线（包含滑入、颤音和抖动），
    /// 但只画播放头已经扫过的部分：播放头右侧留空，随播放头经过才逐段显现，并在播放头处画一个小圆点。
    /// </summary>
    private void DrawPitchCurve(SKCanvas canvas, double nowMs, float top)
    {
        IReadOnlyList<VideoPitchSample> curve = _scene.Curve;
        if (curve.Count == 0) return;
        int first = Math.Max(FindLastSampleAtOrBefore(curve, LeftMs(nowMs)), 0);
        int last = FindLastSampleAtOrBefore(curve, nowMs);
        if (last < first) return;

        using SKPath played = new();
        bool pen = false;
        SKPoint head = default;
        bool hasHead = false;
        for (int i = first; i <= last; i++)
        {
            VideoPitchSample sample = curve[i];
            if (float.IsNaN(sample.Key))
            {
                pen = false;
                continue;
            }

            SKPoint p = new(TimeToX(sample.Ms, nowMs), KeyToY(ToneToKey(sample.Key), top));
            if (pen) played.LineTo(p); else played.MoveTo(p);
            pen = true;
            head = p;
            hasHead = true;
        }

        using SKPaint line = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.2f * _scale,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = Color(_palette.PitchPlayed),
        };
        canvas.DrawPath(played, line);

        // 休止处（最近一个采样是断点或已离开很久）不画圆点。
        if (!hasHead || !pen || nowMs - curve[last].Ms > 30) return;
        using SKPaint dot = new() { IsAntialias = true, Color = Color(_palette.PitchPlayed) };
        canvas.DrawCircle(head, 4 * _scale, dot);
    }

    /// <summary>二分查找最后一个时间不晚于给定值的采样，没有则返回 -1。</summary>
    private static int FindLastSampleAtOrBefore(IReadOnlyList<VideoPitchSample> curve, double ms)
    {
        int lo = 0;
        int hi = curve.Count - 1;
        int result = -1;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (curve[mid].Ms <= ms)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return result;
    }

    #endregion

    #region 和声层

    /// <summary>从后往前依次绘制和声层，位于主音符之后。</summary>
    private void DrawHarmonies(SKCanvas canvas, double nowMs)
    {
        for (int i = _harmonies.Count - 1; i >= 0; i--)
        {
            HarmonyState h = _harmonies[i];
            float fade = HarmonyEnvelope(h.Layer.Phrases, nowMs);
            if (fade < 0.01f) continue;
            float top = TopFor(h.CameraKey);
            DrawHarmonyNotes(canvas, h, nowMs, top, fade);
            DrawHarmonyCurve(canvas, h, nowMs, top, fade);
        }
    }

    /// <summary>
    /// 和声层的整体不透明度：乐句开始前 <see cref="HarmonyFadeInMs"/> 内淡入，
    /// 结束前 <see cref="HarmonyFadeOutLeadMs"/> 起淡出，结束后再多淡出一小段；返回 0 到 1。
    /// </summary>
    private static float HarmonyEnvelope(IReadOnlyList<VideoPhrase> phrases, double nowMs)
    {
        float best = 0;
        foreach (VideoPhrase phrase in phrases)
        {
            if (nowMs < phrase.StartMs - HarmonyFadeInMs) break; // 乐句按起点排序，后面的更晚。
            if (nowMs > phrase.EndMs + HarmonyFadeOutTailMs) continue;
            double fadeIn = Math.Clamp((nowMs - (phrase.StartMs - HarmonyFadeInMs)) / HarmonyFadeInMs, 0, 1);
            double fadeOutSpan = HarmonyFadeOutLeadMs + HarmonyFadeOutTailMs;
            double fadeOut = Math.Clamp((phrase.EndMs + HarmonyFadeOutTailMs - nowMs) / fadeOutSpan, 0, 1);
            best = MathF.Max(best, (float)Smooth(Math.Min(fadeIn, fadeOut)));
        }

        return best;

        static double Smooth(double t) => t * t * (3 - 2 * t);
    }

    /// <summary>和声音符：只有淡色圆角矩形，没有歌词，也没有“正在发声”的描边。</summary>
    private void DrawHarmonyNotes(SKCanvas canvas, HarmonyState h, double nowMs, float top, float fade)
    {
        IReadOnlyList<VideoNote> notes = h.Layer.Notes.Notes;
        double leftMs = LeftMs(nowMs);
        double rightMs = RightMs(nowMs);
        float pad = DesignNotePadding * _scale;
        float radius = DesignNoteRadius * _scale;
        using SKPaint body = new()
        {
            IsAntialias = true,
            Color = Color(h.Layer.AccentArgb).WithAlpha((byte)(HarmonyNoteAlpha * fade * 255)),
        };
        for (int i = h.Layer.Notes.FindFirstEndingAfter(leftMs); i < notes.Count; i++)
        {
            VideoNote note = notes[i];
            if (note.StartMs > rightMs) break;
            float x0 = TimeToX(note.StartMs, nowMs) + 1;
            float x1 = TimeToX(note.EndMs, nowMs);
            float rowTop = (float)((ViewConstants.MaxTone - 1 - note.Tone - top) * _keyHeight);
            canvas.DrawRoundRect(new SKRect(x0, rowTop + pad, MathF.Max(x1, x0 + 3), rowTop + _keyHeight - pad),
                radius, radius, body);
        }
    }

    /// <summary>
    /// 和声音高线：整个视口范围内的线都显示，不随播放头截断，播放头处也没有圆点，
    /// 就是一条从左到右的淡色线，随画面滚动。
    /// </summary>
    private void DrawHarmonyCurve(SKCanvas canvas, HarmonyState h, double nowMs, float top, float fade)
    {
        IReadOnlyList<VideoPitchSample> curve = h.Layer.Curve;
        if (curve.Count == 0) return;
        int first = Math.Max(FindLastSampleAtOrBefore(curve, LeftMs(nowMs)), 0);
        int last = Math.Min(FindLastSampleAtOrBefore(curve, RightMs(nowMs)) + 1, curve.Count - 1);
        if (last < first) return;

        using SKPath path = new();
        bool pen = false;
        for (int i = first; i <= last; i++)
        {
            VideoPitchSample sample = curve[i];
            if (float.IsNaN(sample.Key))
            {
                pen = false;
                continue;
            }

            SKPoint p = new(TimeToX(sample.Ms, nowMs), KeyToY(ToneToKey(sample.Key), top));
            if (pen) path.LineTo(p); else path.MoveTo(p);
            pen = true;
        }

        using SKPaint line = new()
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.8f * _scale,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            Color = Color(h.Layer.AccentArgb).WithAlpha((byte)(HarmonyLineAlpha * fade * 255)),
        };
        canvas.DrawPath(path, line);
    }

    #endregion

    #region 播放头与琴键

    /// <summary>对应 PianoRollPlayPosCanvas：2dp 强调色竖线，左侧带 24dp 的渐淡拖影。</summary>
    private void DrawPlayhead(SKCanvas canvas)
    {
        float x = MathF.Round(_playheadX) + 0.5f;
        float shadowWidth = 24 * _scale;
        SKColor lineColor = Color(_palette.Playhead);
        using SKShader shade = SKShader.CreateLinearGradient(new SKPoint(x - shadowWidth, 0), new SKPoint(x, 0),
            [lineColor.WithAlpha(0), lineColor.WithAlpha((byte)(lineColor.Alpha * 0.45f))], null, SKShaderTileMode.Clamp);
        using SKPaint shadow = new() { Shader = shade };
        canvas.DrawRect(x - shadowWidth, 0, shadowWidth, _height, shadow);

        using SKPaint line = new() { Color = lineColor, StrokeWidth = 2 * _scale, IsAntialias = false };
        canvas.DrawLine(x, 0, x, _height, line);
    }

    /// <summary>对应 PianoKeysCanvas：按键状态选色、白键底部分隔线、主音标签；正在发声的音符对应的键视为按下。</summary>
    private void DrawKeys(SKCanvas canvas, double nowMs, float top)
    {
        int activeTone = -1;
        IReadOnlyList<VideoNote> notes = _scene.Notes.Notes;
        int index = _scene.Notes.FindFirstEndingAfter(nowMs);
        if (index < notes.Count && notes[index].StartMs <= nowMs) activeTone = (int)MathF.Round(notes[index].Tone);

        using SKPaint fill = new() { Style = SKPaintStyle.Fill };
        using SKPaint separator = new() { Color = Color(_palette.BeatLine), StrokeWidth = 1, IsAntialias = false };
        using SKPaint label = new() { IsAntialias = true };
        using SKFont font = new() { Size = 10 * _scale, Typeface = SKTypeface.Default };

        int topTone = (int)Math.Ceiling(ViewConstants.MaxTone - 1 - top);
        int bottomTone = (int)Math.Floor(ViewConstants.MaxTone - 1 - top - _visibleKeys);
        for (int tone = Math.Min(topTone, ViewConstants.MaxTone - 1); tone >= Math.Max(0, bottomTone); tone--)
        {
            float y = RowTop(tone, top);
            bool isActive = tone == activeTone;
            bool isBlack = IsBlackKey(tone);
            bool isTonic = IsTonic(tone);
            uint argb = isActive ? _palette.KeyPressed
                : isTonic ? _palette.Tonic
                : isBlack ? _palette.KeyBlack
                : _palette.KeyWhite;
            fill.Color = Color(argb);
            canvas.DrawRect(0, y, _keysWidth, _keyHeight, fill);

            // 分隔线：仅白键底部，避免黑键区域视觉噪声。
            if (!isBlack)
            {
                canvas.DrawLine(0, y + _keyHeight, _keysWidth, y + _keyHeight, separator);
            }

            if (isActive) continue;
            string name = PianoKeyLabelFormatter.GetLabel(tone, _scene.ProjectKey, PianoKeyLabelMode.TonicPitchLabels);
            if (name.Length == 0) continue;
            label.Color = Color(isTonic ? _palette.LabelTonic : isBlack ? _palette.LabelBlack : _palette.LabelWhite);
            SKFontMetrics metrics = font.Metrics;
            float textWidth = font.MeasureText(name);
            float baseline = y + (_keyHeight - (metrics.Descent - metrics.Ascent)) / 2 - metrics.Ascent;
            float textX = _keysWidth - textWidth - 2 * _scale;
            if (textX >= 0) canvas.DrawText(name, textX, baseline, font, label);
        }
    }

    #endregion

    #region 音名工具

    private static bool IsBlackKey(int tone) => (tone % 12) is 1 or 3 or 6 or 8 or 10;

    private bool IsTonic(int tone) => PianoKeyLabelFormatter.NormalizePitchClass(tone) ==
        PianoKeyLabelFormatter.NormalizePitchClass(_scene.ProjectKey);

    #endregion
}
