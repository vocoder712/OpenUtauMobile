using System;

namespace OpenUtauMobile.Helpers;

/// <summary>放大镜连续倍率边界与设置界面的选档规则。</summary>
public static class MagnifierSettings
{
    public const double Minimum = 0.75;
    public const double Maximum = 2.0;
    public const double Default = 1.0;
    public const double Step = 0.25;

    public static double Normalize(double value)
    {
        return double.IsFinite(value) ? Math.Clamp(value, Minimum, Maximum) : Default;
    }

    public static double Snap(double value)
    {
        return Minimum + Math.Round((Normalize(value) - Minimum) / Step,
            MidpointRounding.AwayFromZero) * Step;
    }
}
