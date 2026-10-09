using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenUtauMobile.Themes.OpenUtauMobile.Controls;

/// <summary>按钮视觉表面的阴影，与底色一起参与按压动画，外层命中区域不变。</summary>
public sealed class ButtonChrome : AvaloniaObject
{
    public static readonly AttachedProperty<BoxShadows> BoxShadowProperty =
        AvaloniaProperty.RegisterAttached<ButtonChrome, Button, BoxShadows>("BoxShadow");

    public static BoxShadows GetBoxShadow(Button button) => button.GetValue(BoxShadowProperty);
    public static void SetBoxShadow(Button button, BoxShadows value) => button.SetValue(BoxShadowProperty, value);
}