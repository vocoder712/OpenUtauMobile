namespace OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Components;

/// <summary>浮动工具栏的容器尺寸与锚点布局契约，供模板和布局共同使用。</summary>
public static class FloatingToolbarTokens
{
    /// <summary>工具栏横截面、图标按钮命中区及独立入口共用的紧凑尺寸。</summary>
    public const double ItemSize = 40;

    /// <summary>工具栏与锚点、主面板与溢出面板之间的间隔。</summary>
    public const double AnchorGap = 8;

    /// <summary>工具栏距离宿主视口边缘的留白，同时保留阴影空间。</summary>
    public const double ViewportInset = 8;

    /// <summary>溢出面板的首选最小宽度，窄视口下随宿主收缩。</summary>
    public const double OverflowMinWidth = 112;

    /// <summary>溢出面板的首选最大宽度，长操作名称换行。</summary>
    public const double OverflowMaxWidth = 280;
}
