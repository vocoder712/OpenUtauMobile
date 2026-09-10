using Avalonia;

namespace OpenUtauMobile.Controls.Tokens;

/// <summary>批量编辑的控件尺寸；业务间距由局部样式持有。</summary>
public static class BatchEditTokens
{
    public static double PopupMinWidth => 280d;
    public static double PopupMinHeight => 420d;
    public static double PopupMaxHeight => 680d;
    public static double ItemIconSize => 40d;
    public static double ParameterMinWidth => 96d;
    public static double ActionIconButtonSize => 56d;
    public static double ActionIconVisualSize => 56d;
    public static double ActionIconSize => 24d;
    public static double PinIconButtonSize => 48d;
    public static double PinIconVisualSize => 32d;
    public static double PinIconSize => 24d;
    public static CornerRadius PinButtonCornerRadius => new(double.MaxValue);
}
