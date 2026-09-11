using System;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Foundation;

/// <summary>已使用的过渡时长</summary>
public static class MotionTokens
{
    public static TimeSpan DurationShort => TimeSpan.FromMilliseconds(150);
    public static TimeSpan DurationExit => TimeSpan.FromMilliseconds(167);
    public static TimeSpan DurationMedium => TimeSpan.FromMilliseconds(200);
    public static TimeSpan DurationProgress => TimeSpan.FromMilliseconds(500);
    public static TimeSpan DurationBase => TimeSpan.Parse("00:00:00.300");
    public static TimeSpan DurationLargeMove => TimeSpan.Parse("00:00:00.375");
    public static TimeSpan DurationMedium1 => TimeSpan.Parse("00:00:00.250");
}
