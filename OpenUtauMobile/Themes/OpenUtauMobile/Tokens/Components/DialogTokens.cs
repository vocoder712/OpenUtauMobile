using Avalonia;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Foundation;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Semantic;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Components;

/// <summary>共享弹窗外框与操作区规格。</summary>
public static class DialogTokens
{
    public static double ControlMinSize => InteractionTokens.TouchTarget;
    public static Thickness HeaderPadding => new(16, 8);
    public static double TitleSize => TypographyTokens.TitleLSize;
    public static CornerRadius CornerRadius => ShapeTokens.CornerXL;
    public static CornerRadius ActionCornerRadius => ShapeTokens.CornerS;
    public static Thickness ActionPadding => new(16, 12);
    public static double ActionHoverOpacity => 0.87;
    public static double ActionPressedOpacity => 0.56;
}
