using Avalonia;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Foundation;

/// <summary>圆角刻度</summary>
public static class ShapeTokens
{
    public static CornerRadius CornerFull => new(double.MaxValue);
    public static CornerRadius CornerS => new(4);
    public static CornerRadius CornerM => new(8);
    public static CornerRadius CornerL => new(12);
    public static CornerRadius CornerXL => new(16);
    public static CornerRadius CornerXXXL => new(24);
    public static CornerRadius CornerXXXXXL => new(32);
}
