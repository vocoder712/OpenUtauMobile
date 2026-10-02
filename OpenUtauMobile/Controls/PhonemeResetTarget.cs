using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Controls.Tokens;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;

namespace OpenUtauMobile.Controls
{
    /// <summary>两种触摸音素编辑共用拖入释放重置目标与动效。</summary>
    internal sealed class PhonemeResetTarget
    {
        private readonly Control _owner;
        private DispatcherTimer? _resetTargetAnimTimer;
        private double _resetTargetAnimProgress;
        private double _resetTargetAnimStartProgress;
        private double _resetTargetAnimTargetProgress;
        private DateTime _resetTargetAnimStartTime;
        private bool _active;
        private readonly Geometry _resetIconGeometry;
        private readonly SolidColorBrush _resetTargetBackgroundBrush = new(Colors.Transparent);
        private readonly SolidColorBrush _resetTargetIconBrush = new(Colors.Transparent);
        private Color _resetTargetIdleBackgroundColor;
        private Color _resetTargetActiveBackgroundColor;
        private Color _resetTargetIdleIconColor;
        private Color _resetTargetActiveIconColor;


        public PhonemeResetTarget(Control owner)
        {
            _owner = owner;
            PackIconPhosphorIcons icon = new() { Kind = PackIconPhosphorIconsKind.ArrowCounterClockwise };
            _resetIconGeometry = icon.Data ?? throw new InvalidOperationException("Reset icon geometry was not initialized.");
        }
        public bool IsActive
        {
            get => _active;
            set
            {
                if (_active == value) return;
                _active = value;
                StartResetTargetAnimation(value ? 1 : 0);
            }
        }
        public void Begin() { End(); CacheResetTargetColors(); }
        public void End() { _active = false; _resetTargetAnimProgress = 0; _resetTargetAnimTimer?.Stop(); }
        private void StartResetTargetAnimation(double target)
        {
            _resetTargetAnimStartProgress = _resetTargetAnimProgress;
            _resetTargetAnimTargetProgress = target;
            _resetTargetAnimStartTime = DateTime.UtcNow;

            if (_resetTargetAnimTimer == null)
            {
                _resetTargetAnimTimer = new DispatcherTimer
                {
                    Interval = PhonemeCanvasTokens.FrameInterval
                };
                _resetTargetAnimTimer.Tick += OnResetTargetAnimTimerTick;
            }
            _resetTargetAnimTimer.Start();
        }

        private void OnResetTargetAnimTimerTick(object? sender, EventArgs e)
        {
            double elapsed = (DateTime.UtcNow - _resetTargetAnimStartTime).TotalMilliseconds;
            double duration = PhonemeCanvasTokens.ResetAnimationDuration.TotalMilliseconds;
            double t = Math.Clamp(elapsed / duration, 0.0, 1.0);
            double eased = 1.0 - Math.Pow(1.0 - t, 3);
            _resetTargetAnimProgress = _resetTargetAnimStartProgress
                + (_resetTargetAnimTargetProgress - _resetTargetAnimStartProgress) * eased;
            _owner.InvalidateVisual();

            if (t >= 1.0)
            {
                _resetTargetAnimProgress = _resetTargetAnimTargetProgress;
                _resetTargetAnimTimer?.Stop();
            }
        }

        public void Render(DrawingContext context)
        {
            double progress = _resetTargetAnimProgress;
            double targetSize = Interpolate(
                PhonemeCanvasTokens.ResetTargetSize,
                PhonemeCanvasTokens.ResetTargetActiveSize,
                progress);
            double iconSize = Interpolate(
                PhonemeCanvasTokens.ResetTargetIconSize,
                PhonemeCanvasTokens.ResetTargetIconActiveSize,
                progress);
            Point center = GetResetTargetCenter();

            _resetTargetBackgroundBrush.Color = InterpolateColor(
                _resetTargetIdleBackgroundColor,
                _resetTargetActiveBackgroundColor,
                progress);
            _resetTargetIconBrush.Color = InterpolateColor(
                _resetTargetIdleIconColor,
                _resetTargetActiveIconColor,
                progress);
            context.DrawEllipse(
                _resetTargetBackgroundBrush,
                null,
                center,
                targetSize * 0.5,
                targetSize * 0.5);

            Rect iconBounds = _resetIconGeometry.Bounds;
            double iconScale = iconSize / Math.Max(iconBounds.Width, iconBounds.Height);
            Matrix iconTransform = Matrix.CreateTranslation(-iconBounds.Center.X, -iconBounds.Center.Y)
                * Matrix.CreateScale(iconScale, iconScale)
                * Matrix.CreateTranslation(center.X, center.Y);
            using (context.PushTransform(iconTransform))
            {
                context.DrawGeometry(_resetTargetIconBrush, null, _resetIconGeometry);
            }
        }

        private static double Interpolate(double from, double to, double progress)
        {
            return from + (to - from) * progress;
        }

        private static Color InterpolateColor(Color from, Color to, double progress)
        {
            byte alpha = (byte)Math.Round(Interpolate(from.A, to.A, progress));
            byte red = (byte)Math.Round(Interpolate(from.R, to.R, progress));
            byte green = (byte)Math.Round(Interpolate(from.G, to.G, progress));
            byte blue = (byte)Math.Round(Interpolate(from.B, to.B, progress));
            return Color.FromArgb(alpha, red, green, blue);
        }

        private void CacheResetTargetColors()
        {
            _resetTargetIdleBackgroundColor = ThemeResources.GetColor("Sem.Color.ErrorContainer");
            _resetTargetActiveBackgroundColor = ThemeResources.GetColor("Sem.Color.Error");
            _resetTargetIdleIconColor = ThemeResources.GetColor("Sem.Color.OnErrorContainer");
            _resetTargetActiveIconColor = ThemeResources.GetColor("Sem.Color.OnError");
        }

        private Point GetResetTargetCenter()
        {
            double halfHitSize = PhonemeCanvasTokens.ResetTargetHitSize * 0.5;
            double targetInset = PhonemeCanvasTokens.ResetTargetOuterInset;
            return new Point(
                targetInset + halfHitSize,
                targetInset + halfHitSize);
        }

        public bool Contains(Point point)
        {
            double hitSize = PhonemeCanvasTokens.ResetTargetHitSize;
            Point center = GetResetTargetCenter();
            Rect hitRect = new(
                center.X - hitSize * 0.5,
                center.Y - hitSize * 0.5,
                hitSize,
                hitSize);
            return hitRect.Contains(point);
        }

    }
}
