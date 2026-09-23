using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace OpenUtauMobile.Controls.Gestures;

/* 
 * 视口变幻抽象层
 * 1. 鼠标滚轮、触控板滚动、触控板捏合缩放、触控板双指拖拽等都映射为 ViewportInput，传入 IEditorViewport。
 * 2. IEditorViewport 只处理逻辑像素的平移和缩放，不关心设备类型和原生事件。
 * 3. IViewportInputPlatform 由平台实现，负责将原生事件映射为 ViewportInput 并传入 IEditorViewport。
 */

public enum ViewportInputKind
{
    Wheel,
    ScrollPixels,
    Magnify,
}

public enum ViewportInputPhase
{
    Update,
    End,
    Cancel,
}

public enum ViewportAxes
{
    Both,
    Horizontal,
    Vertical,
}

/// <summary>平台边界：滚轮单位、逻辑像素、步进缩放比例必须分别传入；位置相对宿主窗口。</summary>
public readonly record struct ViewportInput(
    ViewportInputKind Kind, Vector Delta, double Scale, Point Position, KeyModifiers Modifiers,
    ViewportInputPhase Phase = ViewportInputPhase.Update);

/// <summary>平台只补齐原生事件；返回 true 时由平台消费，避免再次走 Avalonia 路由。</summary>
public interface IViewportInputPlatform
{
    IDisposable Attach(TopLevel root, Func<ViewportInput, bool> dispatch);
}

/// <summary>
/// 主要分为编曲走带和钢琴卷帘两种视口；前者不支持垂直缩放，后者支持。
/// </summary>
public interface IEditorViewport
{
    bool CanNavigateViewport { get; }
    /// <summary>
    /// 是否需要支持垂直缩放；走带编曲不需要
    /// </summary>
    bool SupportsVerticalZoom { get; }
    /// <summary>
    /// 开始视口输入
    /// </summary>
    void BeginViewportInput();
    /// <summary>
    /// 平移视口
    /// </summary>
    /// <param name="pixels"></param>
    /// <returns></returns>
    Vector PanViewport(Vector pixels);
    /// <summary>
    /// 缩放视口
    /// </summary>
    /// <param name="scaleX"></param>
    /// <param name="scaleY"></param>
    /// <param name="anchor"></param>
    void ZoomViewport(double scaleX, double scaleY, Point anchor);
    /// <summary>
    /// 结束视口输入
    /// </summary>
    /// <param name="hadZoomInput">是否进行了缩放</param>
    /// <param name="interrupted">是否被中断</param>
    void EndViewportInput(bool hadZoomInput, bool interrupted);
}

/// <summary>所有平台共用的轴规则；不依据小数增量猜测设备类型。</summary>
public static class ViewportInputMapping
{
    public static (Vector Pan, Vector Zoom) Map(ViewportInput input, ViewportAxes axes, bool verticalZoom, bool zoomOnWheel = false)
    {
        bool shift = input.Modifiers.HasFlag(KeyModifiers.Shift);
        bool zoom = input.Kind == ViewportInputKind.Magnify || input.Modifiers.HasFlag(KeyModifiers.Control) ||
            input.Kind == ViewportInputKind.Wheel && zoomOnWheel;
        Vector delta = input.Delta;
        if (zoom)
        {
            double amount = input.Kind == ViewportInputKind.Magnify
                ? Math.Log(input.Scale)
                : (delta.Y != 0 ? delta.Y : delta.X) * 0.12;
            bool y = verticalZoom && (axes == ViewportAxes.Vertical || axes == ViewportAxes.Both && shift);
            return (default, y ? new Vector(0, amount) : new Vector(amount, 0));
        }
        if (shift) delta = new Vector(delta.Y, delta.X);
        if (axes == ViewportAxes.Horizontal) delta = new Vector(delta.X != 0 ? delta.X : delta.Y, 0);
        if (axes == ViewportAxes.Vertical) delta = new Vector(0, delta.Y != 0 ? delta.Y : delta.X);
        return (input.Kind == ViewportInputKind.Wheel ? delta * 48 : delta, default);
    }
}
