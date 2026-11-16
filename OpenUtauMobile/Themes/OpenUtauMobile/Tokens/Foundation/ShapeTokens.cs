using Avalonia;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Foundation;

/// <summary>圆角刻度</summary>
public static class ShapeTokens
{
    /// <summary>MD3 全圆角令牌；有限大半径由绘制后端按边界归一化，避免浮点转换溢出。</summary>
    public static CornerRadius CornerFull => new(9999);
    public static CornerRadius CornerS => new(4);
    public static CornerRadius CornerM => new(8);
    public static CornerRadius CornerL => new(12);
    public static CornerRadius CornerXL => new(16);
    public static CornerRadius CornerXXXL => new(24);
    public static CornerRadius CornerXXXXXL => new(32);
}
