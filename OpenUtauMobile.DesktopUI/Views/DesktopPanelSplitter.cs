using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;

namespace OpenUtauMobile.DesktopUI.Views
{
    /// <summary>桌面面板统一使用细分隔条和居中握柄。</summary>
    public sealed class DesktopPanelSplitter : GridSplitter
    {
        public DesktopPanelSplitter()
        {
            MinWidth = MinHeight = 6;
            Focusable = true;
            Template = new FuncControlTemplate<DesktopPanelSplitter>((splitter, _) =>
            {
                DesktopResizeGrip grip = new(splitter.ResizeDirection == GridResizeDirection.Columns);
                grip.Bind(DesktopResizeGrip.KeyboardFocusProperty, new Binding(nameof(IsKeyboardFocusWithin)) { Source = splitter });
                return grip;
            });
        }
    }

    internal sealed class DesktopResizeGrip : Control
    {
        private static readonly StyledProperty<IBrush?> GripBrushProperty = AvaloniaProperty.Register<DesktopResizeGrip, IBrush?>("GripBrush");
        private static readonly StyledProperty<IBrush?> SurfaceBrushProperty = AvaloniaProperty.Register<DesktopResizeGrip, IBrush?>("SurfaceBrush");
        private static readonly StyledProperty<IBrush?> ActiveBrushProperty = AvaloniaProperty.Register<DesktopResizeGrip, IBrush?>("ActiveBrush");
        internal static readonly StyledProperty<bool> KeyboardFocusProperty = AvaloniaProperty.Register<DesktopResizeGrip, bool>("KeyboardFocus");
        static DesktopResizeGrip() => AffectsRender<DesktopResizeGrip>(GripBrushProperty, SurfaceBrushProperty, ActiveBrushProperty, IsPointerOverProperty, KeyboardFocusProperty);
        private readonly bool _vertical;
        public DesktopResizeGrip(bool vertical = false)
        {
            _vertical = vertical;
            Bind(GripBrushProperty, this.GetResourceObservable("Sem.Color.Outline"));
            Bind(SurfaceBrushProperty, this.GetResourceObservable("Sem.Color.SurfaceContainer"));
            Bind(ActiveBrushProperty, this.GetResourceObservable("Sem.Color.Primary"));
            Cursor = new Cursor(vertical ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);
        }
        public override void Render(DrawingContext context)
        {
            if (GetValue(SurfaceBrushProperty) is { } surface) context.FillRectangle(surface, new Rect(Bounds.Size));
            IPen pen = new Pen(GetValue(IsPointerOver || GetValue(KeyboardFocusProperty) ? ActiveBrushProperty : GripBrushProperty), 1);
            double x = Bounds.Width / 2;
            double y = Bounds.Height / 2;
            for (int offset = -1; offset <= 1; offset += 2)
            {
                Point start = _vertical ? new(x + offset, y - 12) : new(x - 12, y + offset);
                Point end = _vertical ? new(x + offset, y + 12) : new(x + 12, y + offset);
                context.DrawLine(pen, start, end);
            }
        }
    }
}
