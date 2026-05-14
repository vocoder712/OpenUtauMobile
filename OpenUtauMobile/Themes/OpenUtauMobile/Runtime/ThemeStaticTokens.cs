using System;
using Avalonia;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

/// <summary>
/// base double
/// </summary>
public static class ThemeBaseLayoutTokens
{
    public static double SpaceXS => 4d;
    public static double SpaceS => 8d;
    public static double SpaceM => 16d;
    public static double SpaceL => 24d;
    public static double SpaceXL => 32d;
    public static double SpaceXXL => 48d;
    public static double SpaceXXXL => 56d;
    public static double SpaceXXXXL => 64d;

    public static double SizeM => 32d;
}

/// <summary>
/// base double
/// </summary>
public static class ThemeBaseTypographyTokens
{
    public static double DisplayLSize => 57d;
    public static double DisplayMSize => 45d;
    public static double DisplaySSize => 36d;

    public static double HeadlineLSize => 32d;
    public static double HeadlineMSize => 28d;
    public static double HeadlineSSize => 24d;

    public static double BodyLSize => 16d;
    public static double BodyMSize => 14d;
    public static double BodySSize => 12d;

    public static double LabelLSize => 14d;
    public static double LabelMSize => 12d;
    public static double LabelSSize => 11d;

    public static double SizeS => 12d;
    public static double SizeM => 14d;
    public static double SizeL => 18d;
}

/// <summary>
/// base timespan
/// </summary>
public static class ThemeBaseMotionTokens
{
    public static TimeSpan DurationBase => TimeSpan.Parse("00:00:00.300");
    public static TimeSpan DurationLargeMove => TimeSpan.Parse("00:00:00.375");

    public static TimeSpan DurationShort1 => TimeSpan.Parse("00:00:00.100");
    public static TimeSpan DurationShort2 => TimeSpan.Parse("00:00:00.150");
    public static TimeSpan DurationMedium1 => TimeSpan.Parse("00:00:00.250");
}

/// <summary>
/// base CornerRadius
/// </summary>
public static class ThemeBaseShapeTokens
{
    public static CornerRadius CornerNone => new(0);
    public static CornerRadius CornerXS => new(2);
    public static CornerRadius CornerS => new(4);
    public static CornerRadius CornerM => new(8);
    public static CornerRadius CornerL => new(12); // 一般卡片
    public static CornerRadius CornerXL => new(16);
    public static CornerRadius CornerXXL => new(20);
    public static CornerRadius CornerXXXL => new(24);
    public static CornerRadius CornerXXXXL => new(28);
    public static CornerRadius CornerXXXXXL => new(32);
    public static CornerRadius CornerFull => new(double.MaxValue);
}

/// <summary>
/// base double
/// </summary>
public static class ThemeBaseOpacityTokens
{
    // ── Opacity Levels (MD3 Specification) ──
    public static double LevelNone => 1.0;
    public static double LevelLow => 0.38;
    public static double LevelMedium => 0.56;
    public static double LevelHigh => 0.87;

    // ── Component State Opacity ──
    public static double StateDisabled => 0.38;
    public static double StateHover => 0.08;
    public static double StateFocus => 0.12;
    public static double StatePressed => 0.12;
    public static double StateDrag => 0.16;

    // ── Overlay Opacity ──
    public static double OverlayScrim => 0.32;
    public static double OverlayLightScrim => 0.12;
}

public static class ThemeSemLayoutTokens
{
    public static double SpaceXS => ThemeBaseLayoutTokens.SpaceXS;
    public static double SpaceSM => ThemeBaseLayoutTokens.SpaceS;
    public static double SpaceMD => ThemeBaseLayoutTokens.SpaceM;
    public static double SpaceLG => ThemeBaseLayoutTokens.SpaceL;
    public static double SpaceXL => ThemeBaseLayoutTokens.SpaceXL;
    public static double SpaceXXL => ThemeBaseLayoutTokens.SpaceXXL;
    public static double SpaceContainer => ThemeBaseLayoutTokens.SpaceM;
    public static double SpaceSection => ThemeBaseLayoutTokens.SpaceL;

    public static double SizeActionButton => 80d;
    public static double SizeDeleteButton => 34d;
    public static double SizeNavItemMin => 56d;
    public static double SizeNavToggleMin => 48d;
    public static double SizeHomeAction => SizeActionButton;

    public static Thickness BorderNone => new(0);
    public static Thickness BorderDefault => new(1);
    public static double StrokeLG => 3d;

    public static Thickness InsetNone => new(0);
    public static Thickness InsetXXS => new(2);
    public static Thickness InsetXS => new(4);
    public static Thickness InsetS => new(8);
    public static Thickness InsetM => new(16);
    public static Thickness InsetL => new(20);
    public static Thickness InsetStartSM => new(8, 0, 0, 0);
    public static Thickness InsetActionButton => new(12);
    public static Thickness InsetCardComfortable => new(24, 16);
    public static Thickness InsetCardMedium => new(20, 12);
    public static Thickness InsetCardCompact => new(16, 8);
    public static Thickness InsetFooter => new(16, 24);
    public static Thickness InsetButtonComfortable => new(16, 12);
    public static Thickness InsetButtonMedium => new(14, 7);
    public static Thickness InsetButtonCompact => new(8, 4);
    public static Thickness InsetBottomLG => new(0, 0, 0, 16);
    public static Thickness InsetBottomMD => new(0, 0, 0, 12);
    public static Thickness InsetBottomSM => new(0, 0, 0, 6);
    public static Thickness InsetBottomXS => new(0, 0, 0, 4);
    public static Thickness InsetPageScroll => new(16, 16, 16, 24);
}

public static class ThemeSemTypographyTokens
{
    public static double DisplayLSize => ThemeBaseTypographyTokens.DisplayLSize;
    public static double DisplayMSize => ThemeBaseTypographyTokens.DisplayMSize;
    public static double DisplaySSize => ThemeBaseTypographyTokens.DisplaySSize;

    public static double HeadlineLSize => ThemeBaseTypographyTokens.HeadlineLSize;
    public static double HeadlineMSize => ThemeBaseTypographyTokens.HeadlineMSize;
    public static double HeadlineSSize => ThemeBaseTypographyTokens.HeadlineSSize;

    public static double BodyLSize => ThemeBaseTypographyTokens.BodyLSize;
    public static double BodyMSize => ThemeBaseTypographyTokens.BodyMSize;
    public static double BodySSize => ThemeBaseTypographyTokens.BodySSize;

    public static double LabelLSize => ThemeBaseTypographyTokens.LabelLSize;
    public static double LabelMSize => ThemeBaseTypographyTokens.LabelMSize;
    public static double LabelSSize => ThemeBaseTypographyTokens.LabelSSize;

    public static double Body => ThemeBaseTypographyTokens.SizeM;
    public static double TitlePageSize => 22d;
    public static double InfoLabelSize => LabelMSize;
    public static double InfoValueSize => BodyMSize;
    public static double LabelActionSize => 13d;
    public static double BodyPlaceholderLineHeight => 22d;
}

public static class ThemeSemShapeTokens
{
    public static CornerRadius CornerNone => ThemeBaseShapeTokens.CornerNone;
    public static CornerRadius CornerXS => ThemeBaseShapeTokens.CornerXS;
    public static CornerRadius CornerMedium => ThemeBaseShapeTokens.CornerM;

    public static CornerRadius CornerButton => ThemeBaseShapeTokens.CornerS;
    public static CornerRadius CornerCard => ThemeBaseShapeTokens.CornerM;
    public static CornerRadius CornerDialog => ThemeBaseShapeTokens.CornerXL;
    public static CornerRadius CornerContainer => new(8);
    public static CornerRadius CornerDeleteButton => new(17);
    public static CornerRadius CornerCardCompact => new(14);
}

public static class ThemeSemComponentTokens
{
    public static double ButtonMinHeight => 34d;
    public static Thickness ButtonPadding => new(14, 8);
    public static CornerRadius ButtonCornerRadius => ThemeSemShapeTokens.CornerButton;

    public static Thickness CardBorderThickness => ThemeSemLayoutTokens.BorderDefault;
    public static Thickness CardPadding => new(12);
    public static CornerRadius CardCornerRadius => ThemeSemShapeTokens.CornerCard;

    public static Thickness InputBorderThickness => ThemeSemLayoutTokens.BorderDefault;
    public static Thickness InputPadding => new(12, 8);
    public static CornerRadius InputCornerRadius => ThemeSemShapeTokens.CornerButton;

    public static double SliderTrackHeight => 4d;
    public static double SliderThumbSize => 20d;
    public static double SliderThumbCornerRadius => 10d;
    public static double SliderMinHeight => 48d;
}

public static class ThemeSemOpacityTokens
{
    // ── Semantic Opacity Levels ──
    public static double Full => ThemeBaseOpacityTokens.LevelNone;
    public static double Subdued => ThemeBaseOpacityTokens.LevelLow;
    public static double Medium => ThemeBaseOpacityTokens.LevelMedium;
    public static double Emphasized => ThemeBaseOpacityTokens.LevelHigh;

    // ── Component State Opacity ──
    public static double Disabled => ThemeBaseOpacityTokens.StateDisabled;
    public static double Hover => ThemeBaseOpacityTokens.StateHover;
    public static double Focus => ThemeBaseOpacityTokens.StateFocus;
    public static double Pressed => ThemeBaseOpacityTokens.StatePressed;
    public static double Drag => ThemeBaseOpacityTokens.StateDrag;

    // ── Overlay Opacity ──
    public static double Scrim => ThemeBaseOpacityTokens.OverlayScrim;
    public static double LightScrim => ThemeBaseOpacityTokens.OverlayLightScrim;
}