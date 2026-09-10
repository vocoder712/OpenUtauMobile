using Avalonia;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens;

/// <summary>共享弹窗外框与操作区规格。</summary>
public static class DialogTokens
{
    public static double ControlMinSize => 48d;
    public static Thickness HeaderPadding => new(20, 12);
    public static double TitleSize => 22d;
    public static CornerRadius CornerRadius => new(16);
    public static CornerRadius ActionCornerRadius => new(4);
    public static Thickness ActionPadding => new(16, 12);
    public static double ActionHoverOpacity => 0.87;
    public static double ActionPressedOpacity => 0.56;
}
