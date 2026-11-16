using System;
using System.Linq;
using Avalonia.Threading;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services.Projects;

/// <summary>仅包含 USTX 持久化数据的独立副本，不持有活动歌手、音素器或渲染数据。</summary>
internal static class ProjectSaveSnapshot
{
    /// <summary>在一个 UI 调度回合捕获一致版本；返回后后台只能访问该副本。</summary>
    public static UProject Capture(UProject source, bool template)
    {
        Dispatcher.UIThread.VerifyAccess();
        UProject copy = new()
        {
            ustxVersion = Ustx.kUstxVersion,
            expressionGraphs = source.expressionGraphs == null ? null : [.. source.expressionGraphs.Select(graph => graph.Clone())],
            defaultExpressionGraphs = source.defaultExpressionGraphs == null ? null : new(source.defaultExpressionGraphs),
        };
        foreach (var pair in source.expressions) copy.expressions.Add(pair.Key, CopyExpression(pair.Value));
        // 保留现有模板语义：表达式和表达式图沿用，内容、轨道和时序使用新工程默认值。
        if (template)
        {
            copy.tracks = [.. copy.tracks.Select(CopyTrack)];
            return copy;
        }

        copy.name = source.name;
        copy.comment = source.comment;
        copy.outputDir = source.outputDir;
        copy.cacheDir = source.cacheDir;
#pragma warning disable CS0618
        copy.bpm = source.bpm;
        copy.beatPerBar = source.beatPerBar;
        copy.beatUnit = source.beatUnit;
#pragma warning restore CS0618
        copy.expSelectors = [.. source.expSelectors];
        copy.expPrimary = source.expPrimary;
        copy.expSecondary = source.expSecondary;
        copy.key = source.key;
        copy.timeSignatures = [.. source.timeSignatures.Select(value => new UTimeSignature(value.barPosition, value.beatPerBar, value.beatUnit))];
        copy.tempos = [.. source.tempos.Select(value => new UTempo(value.position, value.bpm))];
        copy.tracks = [.. source.tracks.Select(CopyTrack)];
        copy.parts = [.. source.parts.Select(CopyPart)];
        return copy;
    }

    /// <summary>复制表达式的原始字段，保留自定义默认值的空值语义。</summary>
    private static UExpressionDescriptor CopyExpression(UExpressionDescriptor source) => new()
    {
        name = source.name, abbr = source.abbr, type = source.type,
        min = source.min, max = source.max, defaultValue = source.defaultValue,
        _customDefaultValue = source._customDefaultValue,
        isFlag = source.isFlag, flag = source.flag,
        options = source.options == null ? null! : [.. source.options],
        skipOutputIfDefault = source.skipOutputIfDefault,
    };

    /// <summary>捕获 BeforeSave 所需的运行时标识，后台不再接触歌手实例。</summary>
    private static UTrack CopyTrack(UTrack source) => new()
    {
        singer = source.Singer?.Id!,
        phonemizer = source.Phonemizer.GetType().FullName!,
        RendererSettings = new URenderSettings
        {
            renderer = source.RendererSettings.renderer,
            resampler = source.RendererSettings.resampler,
            wavtool = source.RendererSettings.wavtool,
        },
        TrackName = source.TrackName, TrackColor = source.TrackColor,
        Mute = source.Mute, Solo = source.Solo,
        MixFx = source.MixFx?.Clone()!, Volume = source.Volume, Pan = source.Pan,
        TrackExpressions = [.. source.TrackExpressions.Select(CopyExpression)],
        ExpressionGraph = source.ExpressionGraph,
        VoiceColorNames = source.Singer is { Found: true } && source.VoiceColorExp?.options is { Length: > 0 } colors
            ? [.. colors] : [string.Empty],
    };

    /// <summary>复制片段持久化字段，音频片段只记录引用，不读取或加载波形。</summary>
    private static UPart CopyPart(UPart source)
    {
        UPart copy = source switch
        {
            UVoicePart voice => new UVoicePart
            {
                duration = voice.duration,
                notes = [.. voice.notes.Select(CopyNote)],
                curves = [.. voice.curves.Select(curve => new UCurve(curve.abbr) { xs = [.. curve.xs], ys = [.. curve.ys] })],
                maskedCurves = [.. voice.maskedCurves.Select(curve => curve.Clone())],
            },
            UWavePart wave => new UWavePart
            {
                FilePath = wave.FilePath, relativePath = wave.relativePath,
                fileDurationMs = wave.fileDurationMs, skip = wave.skip, trim = wave.trim,
                fadein = wave.fadein, fadeout = wave.fadeout,
            },
            _ => throw new NotSupportedException($"Unsupported project part: {source.GetType().Name}"),
        };
        copy.name = source.name;
        copy.comment = source.comment;
        copy.trackNo = source.trackNo;
        copy.position = source.position;
        return copy;
    }

    /// <summary>音符表达式只保留值，避免 Clone 引用活动表达式描述符。</summary>
    private static UNote CopyNote(UNote source) => new()
    {
        position = source.position, duration = source.duration, tone = source.tone,
        lyric = source.lyric, pitch = source.pitch.Clone(), vibrato = source.vibrato.Clone(),
        tuning = source.tuning, PhonemizerOverride = source.PhonemizerOverride,
        phonemeExpressions = [.. source.phonemeExpressions.Select(value => new UExpression
        {
            abbr = value.abbr, index = value.index, value = value.value,
        })],
        phonemeOverrides = [.. source.phonemeOverrides.Select(value => value.Clone())],
    };

    /// <summary>在后台执行原 BeforeSave 的纯数据归一化，不调用具有运行时副作用的保存入口。</summary>
    public static void Prepare(UProject copy, string path)
    {
        copy.FilePath = path;
        foreach (UPart part in copy.parts)
        {
            if (part is UWavePart wave)
                wave.relativePath = System.IO.Path.GetRelativePath(System.IO.Path.GetDirectoryName(path)!, wave.FilePath);
            else if (part is UVoicePart voice)
                foreach (UNote note in voice.notes)
                    note.phonemeExpressions = [.. note.phonemeExpressions.OrderBy(value => value.index).ThenBy(value => value.abbr)];
        }
        copy.voiceParts = [.. copy.parts.OfType<UVoicePart>().OrderBy(part => part.trackNo).ThenBy(part => part.position)];
        copy.waveParts = [.. copy.parts.OfType<UWavePart>().OrderBy(part => part.trackNo).ThenBy(part => part.position)];
    }
}
