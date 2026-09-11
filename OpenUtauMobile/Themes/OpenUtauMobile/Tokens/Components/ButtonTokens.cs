using Avalonia;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Foundation;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Components;

/// <summary>按钮家族共享的内容留白、圆角和键盘焦点轮廓。</summary>
public static class ButtonTokens
{
    public static Thickness Padding => new(16, 10);
    public static CornerRadius CornerRadius => ShapeTokens.CornerM;
    public static Thickness FocusThickness => new(2);
    public static Thickness FocusInset => new(-3);
}
