using Avalonia;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Components;

/// <summary>MD3 Expressive FAB menu 的几何与弹簧规格，独立于普通 FAB。</summary>
public static class FabMenuTokens
{
    // AndroidX FabBaselineTokens / FabMenuBaselineTokens（v0_14_0）。
    public const double ButtonSize = 56;
    public const double ClosedRadius = 16;
    public const double OpenRadius = 28;
    public static CornerRadius ItemCornerRadius => new(OpenRadius);
    public const double ClosedIconSize = 24;
    public const double OpenIconSize = 20;
    public const double ItemHeight = 56;
    public const double ItemGap = 4;
    public const double ButtonGap = 8;
    public const double IconLabelGap = 8;
    public const double ItemInset = 24;
    public const double EdgeInset = 16;
    public const int MaximumActions = 6;
    // AndroidX ExpressiveMotionTokens：形状/宽度 fast spatial，透明度 fast effects，顺序 slow effects。
    public const double SpatialDamping = 0.6;
    public const double SpatialStiffness = 800;
    public const double EffectsDamping = 1;
    public const double EffectsStiffness = 3800;
    public const double StaggerStiffness = 800;
}