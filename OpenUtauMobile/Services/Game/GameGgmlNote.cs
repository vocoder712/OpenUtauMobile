using System;
using System.Runtime.InteropServices;

namespace OpenUtauMobile.Services.Game;

/// <summary>
/// game_capi_note 的托管投影（POD，布局与原生结构体完全一致）。
/// 供 GameGgml.Infer 返回给上层。
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct GameGgmlNote
{
    /// <summary>起始时间（秒）。</summary>
    public readonly float OffsetSeconds;

    /// <summary>持续时间（秒）。</summary>
    public readonly float DurationSeconds;

    /// <summary>小数 MIDI 音高（仅 Voiced 有效）。</summary>
    public readonly float PitchMidi;

    /// <summary>1=有声部，0=休止/无音高。</summary>
    public readonly int Voiced;

    public GameGgmlNote(float offsetSeconds, float durationSeconds, float pitchMidi, int voiced)
    {
        this.OffsetSeconds = offsetSeconds;
        this.DurationSeconds = durationSeconds;
        this.PitchMidi = pitchMidi;
        this.Voiced = voiced;
    }
}
