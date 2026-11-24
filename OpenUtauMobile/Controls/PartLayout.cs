using System;
using Avalonia;
using Avalonia.Media.TextFormatting;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime.Resources;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 分片在画布中的布局。所有矩形均使用 Avalonia 逻辑像素（dp），已经包含滚动和缩放。
/// </summary>
/// <remarks>
/// Body 决定缩略图的时间与音高映射；Fill 和 Frame 决定背景与边框；
/// 选中标签同时是移动分片的拖动入口。
/// </remarks>
internal readonly record struct PartLayout
{
    internal const double LabelFontSize = 11;
    internal const double LabelHorizontalPadding = 3;
    internal const double LabelVerticalPadding = 2;

    /// <summary>有足够空间时，为拖动标签保留的最小宽度。</summary>
    private const double MinLabelWidth = 40;

    /// <summary>从轨道顶部向下计算的最小拖动命中高度。</summary>
    private const double MinMoveHitHeight = 24;

    /// <summary>未选中主体与轨道上下边缘的间距。</summary>
    private const double UnselectedInset = 4;

    /// <summary>选中上边框厚度，同时是主体相对轨道顶部的偏移。</summary>
    private const double TopBorder = 6;

    /// <summary>选中下边框厚度。</summary>
    private const double BottomBorder = 2;

    /// <summary>选中左右边框厚度，等于原 resize 手柄的 14dp 宽度；缩略图绘制在其上方。</summary>
    internal const double SideBorder = 14;

    /// <summary>
    /// 内容坐标矩形：宽度对应完整分片时长；选中上沿为轨道顶部 +6，未选中为 +4。
    /// 两种状态的高度均为轨道高度 -8。缩略图使用此矩形映射和裁剪，覆盖在左右边框上。
    /// </summary>
    internal Rect Body { get; private init; }

    /// <summary>
    /// 轨道色背景矩形。选中时在 Body 左右各内缩 14dp，高度不变。
    /// 只限制背景，不限制缩略图；缩略图仍在完整 Body 上绘制，不丢失两侧内容。
    /// </summary>
    internal Rect Fill { get; private init; }

    /// <summary>
    /// Primary 外框矩形。选中时覆盖完整轨道高度；上/下超出 Body 的 6/2dp 就是边框。
    /// Frame 与 Fill 之间的左右夹缝直接作为 resize 的视觉边框，不再额外绘制手柄。
    /// 未选中时等于 Body，绘制时不使用 Primary 填充。
    /// </summary>
    internal Rect Frame { get; private init; }

    /// <summary>
    /// 获取绘制和命中测试共用的完整标签文本布局。
    /// </summary>
    internal static TextLayout GetSelectedLabelText(string name) =>
        TextLayoutCache.Get(name, ThemeResources.GetBrush("Sem.Color.OnPrimary"), LabelFontSize);

    /// <summary>
    /// 将选中标签放在分片可见范围左上角。优先避开两侧边框，空间不足时利用边框顶部保留拖动入口。
    /// </summary>
    internal Rect GetLabelTab(double viewportWidth, TextLayout text)
    {
        double visibleLeft = Math.Max(Frame.Left, 0);
        double visibleRight = Math.Min(Frame.Right, viewportWidth);
        if (visibleRight <= visibleLeft || Body.Height <= 0) return default;

        double left = Math.Clamp(Fill.Left, visibleLeft, Math.Max(visibleLeft, visibleRight - MinLabelWidth));
        double right = Math.Clamp(Fill.Right, Math.Min(visibleRight, left + MinLabelWidth), visibleRight);
        double height = Math.Min(Body.Height, text.Height + LabelVerticalPadding * 2);
        double width = Math.Max(MinLabelWidth, text.Width + LabelHorizontalPadding * 2 + height);
        return new Rect(left, Body.Top, Math.Min(right - left, width), height);
    }

    /// <summary>
    /// 标签和相连的上边框均可开始拖动，命中高度限制在当前轨道内。
    /// </summary>
    internal Rect GetLabelMoveHit(Rect tab) => new(tab.Left, Frame.Top, tab.Width,
        Math.Min(Frame.Height, Math.Max(MinMoveHitHeight, tab.Bottom - Frame.Top)));

    /// <summary>
    /// 计算分片外框和内容的画布坐标；所有尺寸均为 dp。
    /// </summary>
    /// <param name="x">完整分片左沿，通常为 (part.position - TickOffset) × TickWidth，可以为负。</param>
    /// <param name="y">所属轨道顶部，通常为 part.trackNo × TrackHeight - TrackOffset，可以为负。</param>
    /// <param name="width">完整分片时长 × TickWidth；不能传入裁剪后的可见宽度。</param>
    /// <param name="height">所属轨道的完整高度，不是主体高度。</param>
    /// <param name="selected">是否绘制 Primary 边框。</param>
    internal static PartLayout Calculate(double x, double y, double width, double height, bool selected)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        Rect body = new(x, y + (selected ? TopBorder : UnselectedInset), width,
            Math.Max(0, height - TopBorder - BottomBorder));
        Rect frame = selected ? new Rect(x, y, width, height) : body;
        double sideInset = selected ? Math.Min(SideBorder, width / 2) : 0;
        Rect fill = new(body.X + sideInset, body.Y, Math.Max(0, width - sideInset * 2), body.Height);
        return new PartLayout { Body = body, Fill = fill, Frame = frame };
    }
}
