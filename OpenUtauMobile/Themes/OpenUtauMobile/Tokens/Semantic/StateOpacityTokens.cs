namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Semantic;

/// <summary>禁用透明度与状态层透明度。</summary>
public static class StateOpacityTokens
{
    public static double Disabled => 0.38;
    public static double Hover => 0.08;
    public static double Focus => 0.12;
    public static double Pressed => 0.12;
    // 直接淡化内容的旧式交互反馈，不与叠加状态层混用。
    public static double ContentHover => 0.85;
    public static double ContentPressed => 0.70;
}
