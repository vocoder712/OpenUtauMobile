using System;
using Avalonia;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 分片在画布中的布局。所有矩形均使用 Avalonia 逻辑像素（dp），已经包含滚动和缩放。
/// </summary>
/// <remarks>
/// Body 决定缩略图的时间与音高映射；Fill 和 Frame 决定背景与边框；
/// MoveHandle 是未裁剪的完整外观，MoveClip 只限制绘制，MoveHit 独立决定触摸命中。
/// MoveWidth 决定移动页签宽度，SideBorder 同时决定侧边框宽度与移动页签高度。
/// </remarks>
internal readonly record struct PartLayout
{
    /// <summary>右侧移动页签的完整宽度，包含左侧接回顶边的斜坡。</summary>
    internal const double MoveWidth = 48;

    /// <summary>移动页签向主体内凸出的高度，与 resize 的侧边框宽度保持一致。</summary>
    internal const double MoveHeight = SideBorder;

    /// <summary>移动手柄与画布左边缘的最小绘制距离；右侧离屏时直接贴住画布右沿。</summary>
    private const double ViewportLeftInset = 8;

    /// <summary>移动手柄的最小命中宽度；实际值不小于完整手柄宽度。</summary>
    private const double MinMoveHitWidth = 40;

    /// <summary>从轨道顶部向下计算的最小命中高度；实际值也包含完整手柄深度。</summary>
    private const double MinMoveHitHeight = 24;

    /// <summary>未选中主体与轨道上下边缘的间距。</summary>
    private const double UnselectedInset = 4;

    /// <summary>选中上边框厚度，同时是主体相对轨道顶部的偏移。</summary>
    private const double TopBorder = 6;

    /// <summary>选中下边框厚度。</summary>
    private const double BottomBorder = 2;

    /// <summary>选中左右边框厚度，等于原 resize 手柄的 14dp 宽度；缩略图绘制在其上方。</summary>
    internal const double SideBorder = 14;

    /// <summary>按页签高度预留同等宽度，得到固定 45° 斜边，与文本长度和页签总宽度无关。</summary>
    internal static double GetTabSlopeWidth(double height) => Math.Max(0, height);

    /// <summary>标签页签与手柄之间的横向间距。</summary>
    private const double LabelGap = 4;

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
    /// 顶部右侧移动页签的完整视觉矩形。默认右沿贴着 Fill.Right，即右侧 resize 的左沿。
    /// 离屏后 clamp，Y 是 Body.Top；页签不得覆盖右侧 resize 边框。
    /// 即使可见分片很窄，仍保留完整宽度，以免把刘海压缩变形。
    /// </summary>
    internal Rect MoveHandle { get; private init; }

    /// <summary>
    /// 移动手柄实际允许绘制的矩形：完整外观与右侧 resize 左侧的分片范围、视口的交集。
    /// 左侧保留 8dp 安全区，右侧 resize 离屏后直接贴住视口右沿。
    /// 窄分片只裁剪右侧；本矩形不得用于缩放轮廓，也不得用于限制命中宽度。
    /// </summary>
    internal Rect MoveClip { get; private init; }

    /// <summary>
    /// 移动命中矩形：从 MoveHandle.Left、轨道顶部开始，覆盖完整手柄及最小触摸尺寸。
    /// 不随 MoveClip 变窄，额外宽度向右延伸；输入入口负责排除画布之外的点。
    /// </summary>
    internal Rect MoveHit { get; private init; }

    /// <summary>
    /// 选中左侧标签页签可占用的最大矩形：上沿连接顶部边框下沿，向下延伸至 Body.Bottom。
    /// 左沿从左侧 14dp resize 边框右侧开始，并为右侧移动页签让出空间；文本优先省略，不挤动手柄。
    /// 空矩形表示没有标签空间。
    /// </summary>
    internal Rect LabelSlot { get; private init; }

    /// <summary>
    /// 计算分片外框、内容、手柄和标签的画布坐标；所有尺寸均为 dp。
    /// </summary>
    /// <param name="x">完整分片左沿，通常为 (part.position - TickOffset) × TickWidth，可以为负。</param>
    /// <param name="y">所属轨道顶部，通常为 part.trackNo × TrackHeight - TrackOffset，可以为负。</param>
    /// <param name="width">完整分片时长 × TickWidth；不能传入裁剪后的可见宽度。</param>
    /// <param name="height">所属轨道的完整高度，不是主体高度。</param>
    /// <param name="selected">是否绘制 Primary 边框、手柄与标签页签。</param>
    /// <param name="viewportWidth">分片画布宽度，用于手柄 clamp 和标签避让。</param>
    internal static PartLayout Calculate(double x, double y, double width, double height, bool selected, double viewportWidth)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        Rect body = new(x, y + (selected ? TopBorder : UnselectedInset), width,
            Math.Max(0, height - TopBorder - BottomBorder));
        Rect frame = selected ? new Rect(x, y, width, height) : body;
        double sideInset = selected ? Math.Min(SideBorder, width / 2) : 0;
        Rect fill = new(body.X + sideInset, body.Y, Math.Max(0, width - sideInset * 2), body.Height);
        PartLayout layout = new() { Body = body, Fill = fill, Frame = frame };
        if (!selected || width <= 0 || body.Height <= 0) return layout;

        // ① 右侧 resize 可见时为它保留宽度；离屏后直接用视口右沿，不再留间距。
        double safeLeft = Math.Max(x, ViewportLeftInset);
        double safeRight = Math.Min(fill.Right, viewportWidth);
        if (safeRight > safeLeft)
        {
            // ② 默认接在右侧 resize 的左侧；离屏后贴着安全区右端，过窄时只裁剪，不压缩。
            double idealLeft = fill.Right - MoveWidth;
            double handleLeft = safeRight - safeLeft >= MoveWidth
                ? Math.Clamp(idealLeft, safeLeft, safeRight - MoveWidth)
                : safeLeft;
            layout = layout with
            {
                MoveHandle = new Rect(handleLeft, body.Top, MoveWidth, MoveHeight),
                MoveClip = new Rect(handleLeft, body.Top, Math.Min(MoveWidth, safeRight - handleLeft),
                    Math.Min(MoveHeight, body.Height)),
                // ③ 命中区覆盖完整外观，扩宽只向右，扩大深度时也自动跟随。
                MoveHit = new Rect(handleLeft, frame.Top, Math.Max(MinMoveHitWidth, MoveWidth),
                    Math.Min(height, Math.Max(MinMoveHitHeight, TopBorder + MoveHeight)))
            };
        }

        return layout with { LabelSlot = CalculateLabelSlot(layout, viewportWidth) };
    }

    /// <summary>
    /// 先给右侧移动页签保留宽度，左侧剩余空间再分配给标签，不再绕到手柄右侧。
    /// </summary>
    private static Rect CalculateLabelSlot(PartLayout layout, double viewportWidth)
    {
        double left = Math.Max(layout.Body.Left + SideBorder, 0);
        double right = Math.Min(layout.Body.Right, viewportWidth);
        if (layout.MoveClip.Width > 0) right = Math.Min(right, layout.MoveClip.Left - LabelGap);
        return right > left ? new Rect(left, layout.Body.Top, right - left, layout.Body.Height) : default;
    }
}