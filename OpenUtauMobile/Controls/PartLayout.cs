using System;
using Avalonia;

namespace OpenUtauMobile.Controls;

/// <summary>
/// 分片的主体、边框和移动手柄几何，供绘制与指针命中共用。
/// </summary>
internal readonly record struct PartLayout(Rect Body, Rect Fill, Rect Frame, Rect MoveHandle, Rect MoveClip, Rect MoveHit)
{
    internal const double MoveWidth = 32;
    internal const double MoveHeight = 6;
    private const double ViewportInset = 8;
    private const double HitWidth = 40;
    private const double HitHeight = 24;

    /// <summary>
    /// 在画布逻辑坐标中计算分片布局；窄分片只裁剪外观，命中宽度向右保留。
    /// </summary>
    internal static PartLayout Calculate(double x, double y, double width, double height, bool selected, double viewportWidth)
    {
        width = Math.Max(0, width);
        Rect body = new(x, y + (selected ? 6 : 4), width, Math.Max(0, height - 8));
        Rect frame = selected ? new Rect(x, y, width, Math.Max(0, height)) : body;
        double sideInset = selected ? Math.Min(2, width / 2) : 0;
        Rect fill = new(body.X + sideInset, body.Y, Math.Max(0, width - sideInset * 2), body.Height);
        if (!selected || width <= 0 || body.Height <= 0) return new PartLayout(body, fill, frame, default, default, default);

        double left = Math.Max(x, ViewportInset);
        double right = Math.Min(x + width, viewportWidth - ViewportInset);
        if (right <= left) return new PartLayout(body, fill, frame, default, default, default);

        // 能完整显示时保持居中；不足一个手柄宽度时从可见左侧开始裁剪。
        double handleLeft = right - left >= MoveWidth
            ? Math.Clamp(x + width / 2 - MoveWidth / 2, left, right - MoveWidth)
            : left;
        double handleHeight = Math.Min(MoveHeight, body.Height);
        Rect handle = new(handleLeft, body.Top, MoveWidth, handleHeight);
        Rect clip = new(handleLeft, body.Top, Math.Min(MoveWidth, right - handleLeft), handleHeight);
        Rect hit = new(handleLeft, frame.Top, HitWidth, Math.Min(HitHeight, frame.Height));
        return new PartLayout(body, fill, frame, handle, clip, hit);
    }
}