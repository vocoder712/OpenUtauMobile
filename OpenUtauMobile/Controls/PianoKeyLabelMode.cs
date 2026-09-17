using System;
using OpenUtau.Core;

namespace OpenUtauMobile.Controls;

/// <summary>钢琴键标签的显示方式。</summary>
public enum PianoKeyLabelMode
{
    PitchLabels = 0,
    TonicPitchLabels = 1,
    NumberedNotation = 2,
}

public static class PianoKeyLabelFormatter
{
    public static PianoKeyLabelMode NormalizeMode(int value) => value is >= 0 and <= 2
        ? (PianoKeyLabelMode)value
        : PianoKeyLabelMode.TonicPitchLabels;

    public static int NormalizePitchClass(int value) => (value % 12 + 12) % 12;

    public static string GetLabel(int tone, int projectKey, PianoKeyLabelMode mode)
    {
        int relativePitchClass = NormalizePitchClass(tone - projectKey);
        return mode switch
        {
            PianoKeyLabelMode.PitchLabels => MusicMath.GetToneName(tone),
            PianoKeyLabelMode.TonicPitchLabels when relativePitchClass == 0 => MusicMath.GetToneName(tone),
            PianoKeyLabelMode.NumberedNotation => MusicMath.NumberedNotations[relativePitchClass],
            _ => string.Empty,
        };
    }
}

/// <summary>通知已打开的编辑器立即刷新钢琴键标签。</summary>
public sealed record PianoKeyLabelModeChangedEvent(PianoKeyLabelMode Mode);
