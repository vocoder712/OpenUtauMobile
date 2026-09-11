using Avalonia;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Foundation;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Components;

/// <summary>文本框与下拉框共用的输入规格。</summary>
public static class InputTokens
{
    public static Thickness BorderThickness => new(1);
    public static Thickness Padding => new(12, 8);
    public static CornerRadius CornerRadius => ShapeTokens.CornerS;
}
