using System;
using System.Collections.Generic;
using System.Globalization;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtau.Core.Editing;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.ViewModels;

public enum BatchEditCategory
{
    Lyrics,
    Notes,
    Reset,
}

public enum BatchEditParameterKind
{
    None,
    Text,
    Integer,
    Decimal,
}

public static class BatchEditUiConstants
{
    public const int DefaultTransposeSemitones = 1;
    public const int DefaultRandomizeTuningCents = 10;
    public const double DefaultCrossfadeRatio = 0.5d;
    public const double MinimumPositiveValue = 1d;
    public const double MinimumCrossfadeRatio = 0d;
    public const double MaximumCrossfadeRatio = 1d;
    public const string DefaultTailLyric = "R";
    public const string DefaultBreathLyric = "br";
}

public sealed class BatchEditDescriptor
{
    public required string Id { get; init; }
    public required BatchEditCategory Category { get; init; }
    public required string TitleKey { get; init; }
    public required PackIconPhosphorIconsKind Icon { get; init; }
    public BatchEditParameterKind ParameterKind { get; init; }
    public string ParameterLabelKey { get; init; } = string.Empty;
    public Func<UProject, string> DefaultValueFactory { get; init; } = _ => string.Empty;
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public bool RequiresConfirmation { get; init; }
    public bool SupportsCancellation { get; init; }
    public required Func<string, BatchEdit> Factory { get; init; }
}

/// <summary>
/// 移动端批量编辑的稳定目录。显式注册可保证 AOT 裁剪、显示顺序和参数元数据可控。
/// </summary>
public static class BatchEditCatalog
{
    public static IReadOnlyList<BatchEditDescriptor> Items { get; } =
    [
        NoParameter("romaji-to-hiragana", BatchEditCategory.Lyrics,
            "BatchEdit.Action.RomajiToHiragana", PackIconPhosphorIconsKind.Translate,
            _ => new RomajiToHiragana()),
        NoParameter("hiragana-to-romaji", BatchEditCategory.Lyrics,
            "BatchEdit.Action.HiraganaToRomaji", PackIconPhosphorIconsKind.Translate,
            _ => new HiraganaToRomaji()),
        NoParameter("japanese-vcv-to-cv", BatchEditCategory.Lyrics,
            "BatchEdit.Action.JapaneseVcvToCv", PackIconPhosphorIconsKind.TextAa,
            _ => new JapaneseVCVtoCV()),
        NoParameter("remove-tone-suffix", BatchEditCategory.Lyrics,
            "BatchEdit.Action.RemoveToneSuffix", PackIconPhosphorIconsKind.Eraser,
            _ => new RemoveToneSuffix()),
        NoParameter("remove-letter-suffix", BatchEditCategory.Lyrics,
            "BatchEdit.Action.RemoveLetterSuffix", PackIconPhosphorIconsKind.Eraser,
            _ => new RemoveLetterSuffix()),
        NoParameter("move-suffix-to-voice-color", BatchEditCategory.Lyrics,
            "BatchEdit.Action.MoveSuffixToVoiceColor", PackIconPhosphorIconsKind.Palette,
            _ => new MoveSuffixToVoiceColor()),
        NoParameter("remove-phonetic-hint", BatchEditCategory.Lyrics,
            "BatchEdit.Action.RemovePhoneticHint", PackIconPhosphorIconsKind.BracketsSquare,
            _ => new RemovePhoneticHint()),
        NoParameter("dash-to-plus", BatchEditCategory.Lyrics,
            "BatchEdit.Action.DashToPlus", PackIconPhosphorIconsKind.Plus,
            _ => new DashToPlus()),
        NoParameter("dash-to-plus-tilde", BatchEditCategory.Lyrics,
            "BatchEdit.Action.DashToPlusTilde", PackIconPhosphorIconsKind.PlusCircle,
            _ => new DashToPlusTilda()),
        NoParameter("insert-slur", BatchEditCategory.Lyrics,
            "BatchEdit.Action.InsertSlur", PackIconPhosphorIconsKind.WaveSine,
            _ => new InsertSlur()),

        TextParameter("add-tail-note", BatchEditCategory.Notes,
            "BatchEdit.Action.AddTailNote", "BatchEdit.Parameter.Lyric",
            PackIconPhosphorIconsKind.MusicNotesPlus, BatchEditUiConstants.DefaultTailLyric,
            value => new AddTailNote(value, "BatchEdit.Action.AddTailNote")),
        TextParameter("remove-tail-note", BatchEditCategory.Notes,
            "BatchEdit.Action.RemoveTailNote", "BatchEdit.Parameter.Lyric",
            PackIconPhosphorIconsKind.MusicNotesMinus, BatchEditUiConstants.DefaultTailLyric,
            value => new RemoveTailNote(value, "BatchEdit.Action.RemoveTailNote"), true),
        TextParameter("add-breath-note", BatchEditCategory.Notes,
            "BatchEdit.Action.AddBreathNote", "BatchEdit.Parameter.Lyric",
            PackIconPhosphorIconsKind.Wind, BatchEditUiConstants.DefaultBreathLyric,
            value => new AddBreathNote(value)),
        IntegerParameter("transpose", BatchEditCategory.Notes,
            "BatchEdit.Action.Transpose", "BatchEdit.Parameter.Semitones",
            PackIconPhosphorIconsKind.ArrowsDownUp, BatchEditUiConstants.DefaultTransposeSemitones,
            null, null, value => new Transpose(value, "BatchEdit.Action.Transpose")),
        new BatchEditDescriptor
        {
            Id = "quantize",
            Category = BatchEditCategory.Notes,
            TitleKey = "BatchEdit.Action.Quantize",
            Icon = PackIconPhosphorIconsKind.GridFour,
            ParameterKind = BatchEditParameterKind.Integer,
            ParameterLabelKey = "BatchEdit.Parameter.Ticks",
            DefaultValueFactory = project =>
                Math.Max((int)BatchEditUiConstants.MinimumPositiveValue, project.resolution / 4)
                    .ToString(CultureInfo.InvariantCulture),
            Minimum = BatchEditUiConstants.MinimumPositiveValue,
            Factory = value => new QuantizeNotes(int.Parse(value, CultureInfo.InvariantCulture)),
        },
        NoParameter("auto-legato", BatchEditCategory.Notes,
            "BatchEdit.Action.AutoLegato", PackIconPhosphorIconsKind.Link,
            _ => new AutoLegato()),
        NoParameter("fix-overlap", BatchEditCategory.Notes,
            "BatchEdit.Action.FixOverlap", PackIconPhosphorIconsKind.Intersect,
            _ => new FixOverlap(), true),
        NoParameter("common-note-copy", BatchEditCategory.Notes,
            "BatchEdit.Action.CommonNoteCopy", PackIconPhosphorIconsKind.Copy,
            _ => new CommonnoteCopy()),
        NoParameter("common-note-paste", BatchEditCategory.Notes,
            "BatchEdit.Action.CommonNotePaste", PackIconPhosphorIconsKind.Clipboard,
            _ => new CommonnotePaste()),
        NoParameter("hanzi-to-pinyin", BatchEditCategory.Notes,
            "BatchEdit.Action.HanziToPinyin", PackIconPhosphorIconsKind.Translate,
            _ => new HanziToPinyin()),
        DecimalParameter("lengthen-crossfade", BatchEditCategory.Notes,
            "BatchEdit.Action.LengthenCrossfade", "BatchEdit.Parameter.Ratio",
            PackIconPhosphorIconsKind.ArrowsOutLineHorizontal, BatchEditUiConstants.DefaultCrossfadeRatio,
            BatchEditUiConstants.MinimumCrossfadeRatio, BatchEditUiConstants.MaximumCrossfadeRatio,
            value => new LengthenCrossfade(value)),
        NoParameter("randomize-timing", BatchEditCategory.Notes,
            "BatchEdit.Action.RandomizeTiming", PackIconPhosphorIconsKind.Shuffle,
            _ => new RandomizeTiming()),
        NoParameter("randomize-phoneme-offset", BatchEditCategory.Notes,
            "BatchEdit.Action.RandomizePhonemeOffset", PackIconPhosphorIconsKind.ShuffleAngular,
            _ => new RandomizePhonemeOffset()),
        IntegerParameter("randomize-tuning", BatchEditCategory.Notes,
            "BatchEdit.Action.RandomizeTuning", "BatchEdit.Parameter.Cents",
            PackIconPhosphorIconsKind.WaveSine, BatchEditUiConstants.DefaultRandomizeTuningCents,
            BatchEditUiConstants.MinimumPositiveValue, null, value => new RandomizeTuning(value)),
        NoParameter("load-rendered-pitch", BatchEditCategory.Notes,
            "BatchEdit.Action.LoadRenderedPitch", PackIconPhosphorIconsKind.DownloadSimple,
            _ => new LoadRenderedPitch(), supportsCancellation: true),
        NoParameter("bake-pitch", BatchEditCategory.Notes,
            "BatchEdit.Action.BakePitch", PackIconPhosphorIconsKind.CookingPot,
            _ => new BakePitch(), true),
        NoParameter("refresh-real-curves", BatchEditCategory.Notes,
            "BatchEdit.Action.RefreshRealCurves", PackIconPhosphorIconsKind.ArrowsClockwise,
            _ => new RefreshRealCurves(), supportsCancellation: true),

        NoParameter("reset-pitch-bends", BatchEditCategory.Reset,
            "BatchEdit.Action.ResetPitchBends", PackIconPhosphorIconsKind.ArrowCounterClockwise,
            _ => new ResetPitchBends(), true),
        NoParameter("reset-expressions", BatchEditCategory.Reset,
            "BatchEdit.Action.ResetExpressions", PackIconPhosphorIconsKind.ArrowCounterClockwise,
            _ => new ResetAllExpressions(), true),
        NoParameter("clear-vibratos", BatchEditCategory.Reset,
            "BatchEdit.Action.ClearVibratos", PackIconPhosphorIconsKind.WaveSine,
            _ => new ClearVibratos(), true),
        NoParameter("reset-vibratos", BatchEditCategory.Reset,
            "BatchEdit.Action.ResetVibratos", PackIconPhosphorIconsKind.ArrowCounterClockwise,
            _ => new ResetVibratos(), true),
        NoParameter("clear-timings", BatchEditCategory.Reset,
            "BatchEdit.Action.ClearTimings", PackIconPhosphorIconsKind.Timer,
            _ => new ClearTimings(), true),
        NoParameter("reset-aliases", BatchEditCategory.Reset,
            "BatchEdit.Action.ResetAliases", PackIconPhosphorIconsKind.ArrowCounterClockwise,
            _ => new ResetAliases(), true),
        NoParameter("reset-all", BatchEditCategory.Reset,
            "BatchEdit.Action.ResetAll", PackIconPhosphorIconsKind.ArrowCounterClockwise,
            _ => new ResetAll(), true),
    ];

    private static BatchEditDescriptor NoParameter(
        string id,
        BatchEditCategory category,
        string titleKey,
        PackIconPhosphorIconsKind icon,
        Func<string, BatchEdit> factory,
        bool requiresConfirmation = false,
        bool supportsCancellation = false)
    {
        return new BatchEditDescriptor
        {
            Id = id,
            Category = category,
            TitleKey = titleKey,
            Icon = icon,
            RequiresConfirmation = requiresConfirmation,
            SupportsCancellation = supportsCancellation,
            Factory = factory,
        };
    }

    private static BatchEditDescriptor TextParameter(
        string id,
        BatchEditCategory category,
        string titleKey,
        string parameterLabelKey,
        PackIconPhosphorIconsKind icon,
        string defaultValue,
        Func<string, BatchEdit> factory,
        bool requiresConfirmation = false)
    {
        return new BatchEditDescriptor
        {
            Id = id,
            Category = category,
            TitleKey = titleKey,
            Icon = icon,
            ParameterKind = BatchEditParameterKind.Text,
            ParameterLabelKey = parameterLabelKey,
            DefaultValueFactory = _ => defaultValue,
            RequiresConfirmation = requiresConfirmation,
            Factory = factory,
        };
    }

    private static BatchEditDescriptor IntegerParameter(
        string id,
        BatchEditCategory category,
        string titleKey,
        string parameterLabelKey,
        PackIconPhosphorIconsKind icon,
        int defaultValue,
        double? minimum,
        double? maximum,
        Func<int, BatchEdit> factory)
    {
        return new BatchEditDescriptor
        {
            Id = id,
            Category = category,
            TitleKey = titleKey,
            Icon = icon,
            ParameterKind = BatchEditParameterKind.Integer,
            ParameterLabelKey = parameterLabelKey,
            DefaultValueFactory = _ => defaultValue.ToString(CultureInfo.InvariantCulture),
            Minimum = minimum,
            Maximum = maximum,
            Factory = value => factory(int.Parse(value, CultureInfo.InvariantCulture)),
        };
    }

    private static BatchEditDescriptor DecimalParameter(
        string id,
        BatchEditCategory category,
        string titleKey,
        string parameterLabelKey,
        PackIconPhosphorIconsKind icon,
        double defaultValue,
        double? minimum,
        double? maximum,
        Func<double, BatchEdit> factory)
    {
        return new BatchEditDescriptor
        {
            Id = id,
            Category = category,
            TitleKey = titleKey,
            Icon = icon,
            ParameterKind = BatchEditParameterKind.Decimal,
            ParameterLabelKey = parameterLabelKey,
            DefaultValueFactory = _ => defaultValue.ToString(CultureInfo.InvariantCulture),
            Minimum = minimum,
            Maximum = maximum,
            Factory = value => factory(double.Parse(value, CultureInfo.InvariantCulture)),
        };
    }
}
