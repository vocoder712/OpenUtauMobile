using Avalonia;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Foundation;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Semantic;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Components;

/// <summary>共享弹窗外框与操作区规格。</summary>
public static class DialogTokens
{
    public static double ControlMinSize => InteractionTokens.TouchTarget;
    public static Thickness ViewportMargin => new(24);
    public static double DefaultMaxHeight => 640d;
    public static double CompactMinHeight => 180d;
    public static double CompactMaxHeight => 320d;
    public static double RegularMinHeight => 280d;
    public static double ListMinHeight => 280d;
    public static double ListMaxHeight => 600d;
    public static double ExpandedMinHeight => 400d;
    public static double CompactMaxWidth => 360d;
    public static double RegularMaxWidth => 420d;
    public static double WideMaxWidth => 560d;
    public static double ExpandedViewportBreakpoint => 840d;
    public static double ExpandedHorizontalInset => 56d;
    public static double ScrimOpacity => 0.32;
    public static double SurfaceOpacity => 0.72;
    public static Thickness HeaderPadding => new(24, 8);
    public static Thickness BodyPadding => new(24, 8);
    public static Thickness FooterPadding => new(24, 16, 24, 24);
    public static double TitleSize => TypographyTokens.TitleLSize;
    public static CornerRadius CornerRadius => ShapeTokens.CornerXL;
    public static CornerRadius ActionCornerRadius => ButtonTokens.ActionCornerRadius;
    public static Thickness ActionPadding => ButtonTokens.ActionPadding;
}
