using System;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Foundation;

namespace OpenUtauMobile.Controls.Tokens;

/// <summary>音素画布的重置目标、命中区域与动效参数。</summary>
public static class PhonemeCanvasTokens
{
    public static double ResetTargetHitSize => 48d;
    public static double ResetTargetSize => 48d;
    public static double ResetTargetActiveSize => 60d;
    public static double ResetTargetOuterInset => 8d;
    public static double ResetTargetIconSize => 24d;
    public static double ResetTargetIconActiveSize => 30d;
    public static TimeSpan SelectionAnimationDuration => TimeSpan.FromMilliseconds(130);
    public static TimeSpan FrameInterval => TimeSpan.FromMilliseconds(16);
    public static TimeSpan ResetAnimationDuration => MotionTokens.DurationShort;
}
