using OpenUtau.Core;
using OpenUtau.Core.Util;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Audio;

public static class TonePlayer
{
    /// <summary>
    /// 获取当前配置的钢琴键行为。
    /// </summary>
    private static PianoKeyBehavior GetBehavior()
    {
        int val = Preferences.Default.PianoKeyBehavior;
        if (val is >= 0 and <= 2)
        {
            return (PianoKeyBehavior)val;
        }
        return PianoKeyBehavior.SineWave;
    }

    public static void PlayNoteOn(int tone)
    {
        PianoKeyBehavior behavior = GetBehavior();

        switch (behavior)
        {
            case PianoKeyBehavior.Silent:
                // 无声模式，不播放任何声音
                break;

            case PianoKeyBehavior.SoundFont:
                // 使用 SoundFont 采样播放
                SoundFontPlayer sfPlayer = SoundFontPlayer.Instance;
                if (sfPlayer.IsReady)
                {
                    sfPlayer.PlayNote(tone);
                }
                else
                {
                    // SF2 未加载，回退到正弦波
                    FallbackPlayTone(tone);
                }

                break;

            case PianoKeyBehavior.SineWave:
            default:
                // 默认：正弦波
                FallbackPlayTone(tone);
                break;
        }
    }

    public static void PlayNoteOff(int tone)
    {
        PianoKeyBehavior behavior = GetBehavior();

        switch (behavior)
        {
            case PianoKeyBehavior.Silent:
                break;

            case PianoKeyBehavior.SoundFont:
                SoundFontPlayer sfPlayer = SoundFontPlayer.Instance;
                if (sfPlayer.IsReady)
                {
                    sfPlayer.StopNote(tone);
                }
                else
                {
                    FallbackEndTone(tone);
                }

                break;

            case PianoKeyBehavior.SineWave:
            default:
                FallbackEndTone(tone);
                break;
        }
    }

    public static void StopAll()
    {
        PianoKeyBehavior behavior = GetBehavior();

        switch (behavior)
        {
            case PianoKeyBehavior.Silent:
                break;

            case PianoKeyBehavior.SoundFont:
                SoundFontPlayer sfPlayer = SoundFontPlayer.Instance;
                if (sfPlayer.IsReady)
                {
                    sfPlayer.StopAllNotes();
                }
                else
                {
                    FallbackEndAll();
                }

                break;

            case PianoKeyBehavior.SineWave:
            default:
                FallbackEndAll();
                break;
        }
    }

    /// <summary>
    /// 正弦波发声回退。
    /// </summary>
    private static void FallbackPlayTone(int tone)
    {
        double freq = MusicMath.ToneToFreq(tone);
        PlaybackManager.Inst.PlayTone(freq);
    }

    /// <summary>
    /// 正弦波停声回退。
    /// </summary>
    private static void FallbackEndTone(int tone)
    {
        double freq = MusicMath.ToneToFreq(tone);
        PlaybackManager.Inst.EndTone(freq);
    }

    private static void FallbackEndAll()
    {
        PlaybackManager.Inst.EndAllTones();
    }
}