using Avalonia;
using Avalonia.Media;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens;

/// <summary>共享页签规格。</summary>
public static class TabItemTokens
{
    public static double ContainerHeight => 48d;
    public static double ActiveIndicatorHeight => 2d;
    public static double LabelSize => TypographyTokens.TitleSSize;
    public static Thickness HeaderInset => new(24, 0);
    public static CornerRadius ActiveIndicatorCorner => new(0);
    public static FontWeight LabelWeight => FontWeight.Medium;
}
