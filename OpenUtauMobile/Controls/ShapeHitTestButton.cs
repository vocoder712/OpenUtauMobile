using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering;

namespace OpenUtauMobile.Controls;

public enum ButtonHitTestShape
{
    Ellipse,
    TopLeftTriangle,
}

/// <summary>
/// 按可见形状限制指针命中范围的按钮。
/// </summary>
public class ShapeHitTestButton : Button, ICustomHitTest
{
    public static readonly StyledProperty<ButtonHitTestShape> HitTestShapeProperty =
        AvaloniaProperty.Register<ShapeHitTestButton, ButtonHitTestShape>(nameof(HitTestShape));

    public ButtonHitTestShape HitTestShape
    {
        get => GetValue(HitTestShapeProperty);
        set => SetValue(HitTestShapeProperty, value);
    }

    public bool HitTest(Point point)
    {
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        return HitTestShape switch
        {
            ButtonHitTestShape.TopLeftTriangle =>
                point.X >= 0 && point.Y >= 0 &&
                point.X / width + point.Y / height <= 1,
            _ => IsInsideEllipse(point, width, height),
        };
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Clip = HitTestShape switch
        {
            ButtonHitTestShape.TopLeftTriangle => CreateTopLeftTriangle(e.NewSize),
            _ => new EllipseGeometry(new Rect(e.NewSize)),
        };
    }

    private static bool IsInsideEllipse(Point point, double width, double height)
    {
        double normalizedX = (point.X - width / 2) / (width / 2);
        double normalizedY = (point.Y - height / 2) / (height / 2);
        return normalizedX * normalizedX + normalizedY * normalizedY <= 1;
    }

    private static StreamGeometry CreateTopLeftTriangle(Size size)
    {
        StreamGeometry geometry = new();
        using StreamGeometryContext context = geometry.Open();
        context.BeginFigure(new Point(0, 0), true);
        context.LineTo(new Point(size.Width, 0));
        context.LineTo(new Point(0, size.Height));
        context.EndFigure(true);
        return geometry;
    }
}
