using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using OpenUtauMobile.Helpers;

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
                nameof(MagnificationFactor), MagnifierSettings.Default,
                coerce: (_, value) => MagnifierSettings.Normalize(value));

        public static readonly StyledProperty<Size> LensSizeProperty =
            AvaloniaProperty.Register<MagnifierControl, Size>(
                nameof(LensSize), new Size(200, 200));

        public static readonly StyledProperty<IBrush?> BackgroundProperty =
            AvaloniaProperty.Register<MagnifierControl, IBrush?>(
                nameof(Background));

        public static readonly StyledProperty<IBrush?> BorderBrushProperty =
            AvaloniaProperty.Register<MagnifierControl, IBrush?>(nameof(BorderBrush));

        public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
            AvaloniaProperty.Register<MagnifierControl, CornerRadius>(nameof(CornerRadius));

        public static readonly StyledProperty<Color> ShadowColorProperty =
            AvaloniaProperty.Register<MagnifierControl, Color>(nameof(ShadowColor));

        public static readonly StyledProperty<Rect> SourceRectProperty =
            AvaloniaProperty.Register<MagnifierControl, Rect>(
                nameof(SourceRect), new Rect(0, 0, 200, 200));

        // 字段与内部对象
        private Visual? _source;
        private readonly VisualBrush _visualBrush = new();
        private bool _hasSample;

        private Point _focusPointInSource;

        static MagnifierControl()
        {
            AffectsRender<MagnifierControl>(BackgroundProperty, BorderBrushProperty,
                CornerRadiusProperty, ShadowColorProperty);
            AffectsMeasure<MagnifierControl>(LensSizeProperty);
        }

        public MagnifierControl()
        {
            _visualBrush.Stretch = Stretch.Fill;
            _visualBrush.TileMode = TileMode.None;
            IsHitTestVisible = false;
            Focusable = false;
        }

        // 公共属性
        public Visual? Source
        {
            get => _source;
            set
            {
                if (SetAndRaise(SourceProperty, ref _source, value))
                {
                    // 关闭时也同步释放笔刷引用，避免继续持有整个编辑器视觉树。
                    _visualBrush.Visual = value;
                    _hasSample = false;
                    InvalidateVisual();
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

        public IBrush? BorderBrush
        {
            get => GetValue(BorderBrushProperty);
            set => SetValue(BorderBrushProperty, value);
        }

        public CornerRadius CornerRadius
        {
            get => GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        public Color ShadowColor
        {
            get => GetValue(ShadowColorProperty);
            set => SetValue(ShadowColorProperty, value);
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
            Size size = Bounds.Size;
            double width = Math.Min(size.Width / factor, _source.Bounds.Width);
            double height = Math.Min(size.Height / factor, _source.Bounds.Height);
            _hasSample = width > 0 && height > 0;
            if (!_hasSample)
            {
                InvalidateVisual();
                return;
            }

            double x = Math.Clamp(_focusPointInSource.X - width / 2, 0, _source.Bounds.Width - width);
            double y = Math.Clamp(_focusPointInSource.Y - height / 2, 0, _source.Bounds.Height - height);
            SourceRect = new Rect(x, y, width, height);

            // 用 VisualBrush.SourceRect 指定“源控件中被采样”的绝对区域。
            // 目标区域按实际倍率计算，避免源区域不足时改变缩放比例。
            _visualBrush.SourceRect = new RelativeRect(SourceRect, RelativeUnit.Absolute);

            // 源区域小于镜片时居中留出主题背景，不把不足的内容拉伸到另一倍率。
            _visualBrush.DestinationRect = new RelativeRect(new Rect(
                (size.Width - width * factor) / 2,
                (size.Height - height * factor) / 2,
                width * factor, height * factor), RelativeUnit.Absolute);

            // 设置笔刷源
            _visualBrush.Visual = _source;

            InvalidateVisual();
        }

        // 实际绘制
        public override void Render(DrawingContext context)
        {
            base.Render(context);

            Rect rect = new(Bounds.Size);
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return;
            }

            RoundedRect roundedRect = new(rect, CornerRadius);
            BoxShadows shadows = new(new BoxShadow
            {
                OffsetY = 2,
                Blur = 6,
                Color = Color.FromArgb((byte)(ShadowColor.A * 0.18), ShadowColor.R, ShadowColor.G, ShadowColor.B)
            });

            // 阴影在裁切外绘制，镜片内容和背景共同使用圆角裁切。
            context.DrawRectangle(Background, null, roundedRect, shadows);
            using (context.PushClip(roundedRect))
            {
                if (_source != null && _hasSample)
                {
                    context.FillRectangle(_visualBrush, rect);
                }
            }

            // 将细边框内缩半个逻辑像素，避免外侧被布局边界裁掉。
            context.DrawRectangle(null, new Pen(BorderBrush, 1),
                new RoundedRect(rect.Deflate(0.5), Math.Max(0, CornerRadius.TopLeft - 0.5)));
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == MagnificationFactorProperty || change.Property == BoundsProperty ||
                change.Property == IsVisibleProperty)
            {
                UpdateView(_focusPointInSource);
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(
                Math.Min(LensSize.Width, availableSize.Width),
                Math.Min(LensSize.Height, availableSize.Height));
        }
    }
}
