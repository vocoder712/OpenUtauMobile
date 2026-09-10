using Avalonia;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Foundation;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens;

/// <summary>共享卡片规格。</summary>
public static class CardTokens
{
    public static Thickness BorderThickness => new(1);
    public static Thickness Padding => LayoutTokens.InsetSM;
    public static CornerRadius CornerRadius => ShapeTokens.CornerM;
}
