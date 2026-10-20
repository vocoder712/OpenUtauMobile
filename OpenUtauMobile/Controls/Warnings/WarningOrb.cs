using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Rendering;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Semantic;

namespace OpenUtauMobile.Controls.Warnings
{
    /// <summary>用同一轮廓绘制和命中悬浮球，透明圆角不拦截导航页。</summary>
    public sealed class WarningOrb : Button, ICustomHitTest
    {
        static WarningOrb()
        {
            AffectsRender<WarningOrb>(BackgroundProperty, ForegroundProperty, IsPressedProperty, IsPointerOverProperty, IsKeyboardFocusWithinProperty);
        }

        private double _dock;
        private double _roundness = 1;
        private double _glyphOpacity = 1;
        private PackIconPhosphorIcons? _glyph;
        /// <summary>贴边形状：负值连接左侧，正值连接右侧，零为自由圆形。</summary>
        public double Dock
        {
            get => _dock;
            set { _dock = Math.Clamp(value, -1, 1); InvalidateVisual(); }
        }
        /// <summary>从卡片圆角到半径圆角的过渡进度。</summary>
        public double Roundness
        {
            get => _roundness;
            set { _roundness = Math.Clamp(value, 0, 1); InvalidateVisual(); }
        }
        /// <summary>收束过程中独立显现图标，避免压缩正文。</summary>
        public double GlyphOpacity
        {
            get => _glyphOpacity;
            set { _glyphOpacity = value; UpdateGlyphOpacity(); }
        }

        /// <summary>建立功能局部绘制模板，沿用按钮的键盘与自动化语义。</summary>
        public WarningOrb()
        {
            Width = Height = 56;
            Padding = new Thickness(0);
            Template = new FuncControlTemplate<WarningOrb>((orb, _) =>
            {
                PackIconPhosphorIcons glyph = new()
                {
                    Kind = PackIconPhosphorIconsKind.Warning,
                    Width = IconTokens.Standard,
                    Height = IconTokens.Standard,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false,
                };
                glyph.Bind(ForegroundProperty, orb.GetObservable(ForegroundProperty));
                orb._glyph = glyph;
                orb.UpdateGlyphOpacity();
                return glyph;
            });
            this.Bind(BackgroundProperty, this.GetResourceObservable("Sem.Color.PrimaryContainer"));
            this.Bind(ForegroundProperty, this.GetResourceObservable("Sem.Color.OnPrimaryContainer"));
        }

        /// <summary>圆形、半圆连接矩形以及卡片收束共用一条连续曲线。</summary>
        private StreamGeometry Outline()
        {
            double w = Bounds.Width;
            double h = Bounds.Height;
            double r = WarningMotion.Lerp(12, Math.Min(w, h) / 2, Roundness);
            double left = r * (1 - Math.Max(0, -Dock));
            double right = r * (1 - Math.Max(0, Dock));
            const double k = 0.55228475;
            StreamGeometry geometry = new();
            using StreamGeometryContext path = geometry.Open();
            path.BeginFigure(new Point(left, 0), true);
            path.LineTo(new Point(w - right, 0));
            path.CubicBezierTo(new Point(w - right + k * right, 0), new Point(w, right - k * right), new Point(w, right));
            path.LineTo(new Point(w, h - right));
            path.CubicBezierTo(new Point(w, h - right + k * right), new Point(w - right + k * right, h), new Point(w - right, h));
            path.LineTo(new Point(left, h));
            path.CubicBezierTo(new Point(left - k * left, h), new Point(0, h - left + k * left), new Point(0, h - left));
            path.LineTo(new Point(0, left));
            path.CubicBezierTo(new Point(0, left - k * left), new Point(left - k * left, 0), new Point(left, 0));
            path.EndFigure(true);
            return geometry;
        }

        /// <summary>只接收实际轮廓内的起始指针，圆角外部仍透传。</summary>
        public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point) && Outline().FillContains(point);

        /// <summary>图形随实际位置变化；图标保持固定尺寸，不随卡片正文一起压缩。</summary>
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.DrawGeometry(Background, null, Outline());
            if (Classes.Contains(":focus-visible")) context.DrawGeometry(null, new Pen(Foreground, 2), Outline());
        }

        /// <summary>只改变库图标的透明度，收束和贴边均保持标准图标尺寸。</summary>
        private void UpdateGlyphOpacity()
        {
            if (_glyph != null) _glyph.Opacity = GlyphOpacity * (IsPressed || IsPointerOver ? 0.8 : 1);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsPressedProperty || change.Property == IsPointerOverProperty) UpdateGlyphOpacity();
        }

        // 指针点击由宿主与拖动阈值共同裁决；保留 Button 的键盘和自动化激活行为。
        protected override void OnPointerPressed(PointerPressedEventArgs e) { }
        protected override void OnPointerReleased(PointerReleasedEventArgs e) { }

        protected override void OnGotFocus(FocusChangedEventArgs e)
        {
            base.OnGotFocus(e);
            InvalidateVisual();
        }

        protected override void OnLostFocus(FocusChangedEventArgs e)
        {
            base.OnLostFocus(e);
            InvalidateVisual();
        }
    }
}
