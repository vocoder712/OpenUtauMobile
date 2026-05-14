using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

namespace OpenUtauMobile.Controls
{
    /// <summary>
    /// 被动式放大镜控件：
    /// 完全由外部控制 Source 来源、更新频率、焦点坐标以及是否显示。
    /// </summary>
    public class MagnifierControl : Control
    {
        // 依赖属性
        public static readonly DirectProperty<MagnifierControl, Visual?> SourceProperty =
            AvaloniaProperty.RegisterDirect<MagnifierControl, Visual?>(
                nameof(Source),
                o => o.Source,
                (o, v) => o.Source = v);

        public static readonly StyledProperty<double> MagnificationFactorProperty =
            AvaloniaProperty.Register<MagnifierControl, double>(
                nameof(MagnificationFactor), 2.0);

        public static readonly StyledProperty<Size> LensSizeProperty =
            AvaloniaProperty.Register<MagnifierControl, Size>(
                nameof(LensSize), new Size(200, 200));

        public static readonly StyledProperty<IBrush?> BackgroundProperty =
            AvaloniaProperty.Register<MagnifierControl, IBrush?>(
                nameof(Background));

        public static readonly StyledProperty<Rect> SourceRectProperty =
            AvaloniaProperty.Register<MagnifierControl, Rect>(
                nameof(SourceRect), new Rect(0, 0, 200, 200));

        // 字段与内部对象
        private Visual? _source;
        private readonly VisualBrush _visualBrush = new();
        private readonly TranslateTransform _translateTransform = new();
        private readonly ScaleTransform _scaleTransform = new();
        private readonly TransformGroup _transformGroup = new();

        private Point _focusPointInSource;

        public MagnifierControl()
        {
            _transformGroup.Children.Add(_scaleTransform);
            _transformGroup.Children.Add(_translateTransform);

            _visualBrush.Stretch = Stretch.Fill;
            _visualBrush.DestinationRect = RelativeRect.Fill;
            _visualBrush.TileMode = TileMode.None;

            Background ??= ThemeResources.GetBrush("Sem.Color.Scrim");
        }

        // 公共属性
        public Visual? Source
        {
            get => _source;
            set
            {
                if (SetAndRaise(SourceProperty, ref _source, value))
                {
                    // 当 Source 发生改变且放大镜处于激活(可见)状态时，立即刷新一次
                    if (IsVisible)
                    {
                        UpdateView(_focusPointInSource);
                    }
                }
            }
        }

        public double MagnificationFactor
        {
            get => GetValue(MagnificationFactorProperty);
            set => SetValue(MagnificationFactorProperty, value);
        }

        public Size LensSize
        {
            get => GetValue(LensSizeProperty);
            set => SetValue(LensSizeProperty, value);
        }

        public IBrush? Background
        {
            get => GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        public Rect SourceRect
        {
            get => GetValue(SourceRectProperty);
            private set => SetValue(SourceRectProperty, value);
        }

        /// <summary>
        /// 外部调用此方法，传入要放大的目标点（相对于 Source 的坐标），从而触发画面刷新
        /// </summary>
        /// <param name="focusPointInSource">想要观察的源控件坐标点</param>
        public void UpdateView(Point focusPointInSource)
        {
            // 如果放大镜被关闭（隐藏）或者没有指定来源，直接略过
            if (!IsVisible || _source == null)
                return;

            _focusPointInSource = focusPointInSource;

            double factor = MagnificationFactor;
            double halfWidth = LensSize.Width / factor / 2;
            double halfHeight = LensSize.Height / factor / 2;

            double x = _focusPointInSource.X - halfWidth;
            double y = _focusPointInSource.Y - halfHeight;

            // 越界限制（防止放大镜边缘看到控件外面的空白）
            if (_source is Control c)
            {
                x = Math.Clamp(x, 0, Math.Max(0, c.Bounds.Width - LensSize.Width / factor));
                y = Math.Clamp(y, 0, Math.Max(0, c.Bounds.Height - LensSize.Height / factor));
            }

            SourceRect = new Rect(x, y, LensSize.Width / factor, LensSize.Height / factor);

            // 用 VisualBrush.SourceRect 指定“源控件中被采样”的绝对区域。
            // DestinationRect=Fill + Stretch=Fill 会把该区域拉伸到整个镜片。
            _visualBrush.SourceRect = new RelativeRect(SourceRect, RelativeUnit.Absolute);

            // 设置笔刷源
            _visualBrush.Visual = _source;

            InvalidateVisual();
        }

        // 实际绘制
        public override void Render(DrawingContext context)
        {
            base.Render(context);

            Rect rect = new(new Point(0, 0), LensSize);

            // 1. 画背景遮罩
            context.FillRectangle(Background ?? ThemeResources.GetBrush("Sem.Color.Scrim"), rect);

            // 2. 用 VisualBrush 画放大后的内容
            if (_source != null)
            {
                context.FillRectangle(_visualBrush, rect);
            }

            // 3. 画放大镜边框
            Pen pen = new(ThemeResources.GetBrush("Sem.Color.OnSurface"), 2);
            context.DrawRectangle(null, pen, rect);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(
                Math.Min(LensSize.Width, availableSize.Width),
                Math.Min(LensSize.Height, availableSize.Height));
        }
    }
}