using Avalonia;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Foundation;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Components;

/// <summary>按钮家族共享的内容留白、圆角和键盘焦点轮廓。</summary>
public static class ButtonTokens
{
    public static Thickness Padding => new(16, 10);
    public static CornerRadius CornerRadius => ShapeTokens.CornerM;
    // 有限大半径避免极大浮点数在渲染器中溢出。
    public static CornerRadius ActionCornerRadius => new(999);
    public static Thickness ActionPadding => new(24, 8);
    public static Thickness TextActionPadding => new(12, 8);
    public static double ActionMinHeight => 40d;
    public static Thickness FocusThickness => new(2);
    public static Thickness FocusInset => new(-3);
}
