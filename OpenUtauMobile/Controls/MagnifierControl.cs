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
        private Rect _sampleDestinationRect;

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
            double width = size.Width / factor;
            double height = size.Height / factor;
            // 完整采样框始终以目标点为中心，不因触及源控件边界而停住。
            SourceRect = new Rect(_focusPointInSource.X - width / 2,
                _focusPointInSource.Y - height / 2, width, height);
            Rect sampleRect = SourceRect.Intersect(new Rect(_source.Bounds.Size));
            _hasSample = sampleRect.Width > 0 && sampleRect.Height > 0;
            if (!_hasSample)
            {
                InvalidateVisual();
                return;
            }

            // 只采样有效交集，并保留其相对完整采样框的偏移与倍率。
            _visualBrush.SourceRect = new RelativeRect(sampleRect, RelativeUnit.Absolute);
            _sampleDestinationRect = new Rect(
                (sampleRect.X - SourceRect.X) * factor,
                (sampleRect.Y - SourceRect.Y) * factor,
                sampleRect.Width * factor, sampleRect.Height * factor);
            _visualBrush.DestinationRect = new RelativeRect(_sampleDestinationRect, RelativeUnit.Absolute);

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

            // 阴影在裁切外绘制，透明底保留镜片中超出源控件边界的空白。
            context.DrawRectangle(Brushes.Transparent, null, roundedRect, shadows);
            using (context.PushClip(roundedRect))
            {
                if (_source != null && _hasSample)
                {
                    // 背景与内容仅绘制到有效区域，边界外保持透明。
                    using (context.PushClip(_sampleDestinationRect))
                    {
                        context.DrawRectangle(Background, null, _sampleDestinationRect);
                        context.FillRectangle(_visualBrush, rect);
                    }
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
