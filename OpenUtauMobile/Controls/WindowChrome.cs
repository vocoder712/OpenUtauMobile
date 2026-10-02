using Avalonia;
using Avalonia.Controls.Chrome;

namespace OpenUtauMobile.Controls
{
    /// <summary>复用窗口装饰模板时可隐藏系统标题文本，保留原生标题与按钮。</summary>
    public sealed class WindowChrome : AvaloniaObject
    {
        public static readonly AttachedProperty<bool> ShowTitleProperty =
            AvaloniaProperty.RegisterAttached<WindowChrome, WindowDrawnDecorations, bool>("ShowTitle", true);
        public static readonly AttachedProperty<bool> ShowFullScreenButtonProperty =
            AvaloniaProperty.RegisterAttached<WindowChrome, WindowDrawnDecorations, bool>("ShowFullScreenButton", true);
        public static bool GetShowFullScreenButton(WindowDrawnDecorations decorations) => decorations.GetValue(ShowFullScreenButtonProperty);
        public static void SetShowFullScreenButton(WindowDrawnDecorations decorations, bool value) => decorations.SetValue(ShowFullScreenButtonProperty, value);
        public static bool GetShowTitle(WindowDrawnDecorations decorations) => decorations.GetValue(ShowTitleProperty);
        public static void SetShowTitle(WindowDrawnDecorations decorations, bool value) => decorations.SetValue(ShowTitleProperty, value);
    }
}
