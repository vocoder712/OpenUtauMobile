using System;

namespace OpenUtauMobile.Services.Editor
{
    /// <summary>编曲区和桌面钢琴卷帘共用自动翻页模式与插值，输入期间由手势控制视口。</summary>
    public sealed class PlaybackViewportFollow
    {
        public const double MarginRatio = 0.05;
        private const double StopEpsilonTicks = 0.5;
        private const double LerpSharpness = 10;
        private const double MaxStepViewportRatio = 0.35;
        private bool _active;
        private double _target;

        public void Reset() => _active = false;

        public double Update(int mode, bool playing, bool inputActive, int tick, double offset, double visibleTicks, double seconds)
        {
            if (mode == 0 || !playing || !double.IsFinite(visibleTicks) || visibleTicks <= 0)
            {
                Reset();
                return offset;
            }
            if (inputActive) return offset;
            if (tick < offset + visibleTicks * MarginRatio || tick > offset + visibleTicks * (1 - MarginRatio))
            {
                _target = tick - visibleTicks * MarginRatio;
                _active = true;
            }
            if (!_active) return offset;
            double delta = _target - offset;
            if (mode == 1 || Math.Abs(delta) <= StopEpsilonTicks)
            {
                Reset();
                return _target;
            }
            double alpha = 1 - Math.Exp(-LerpSharpness * seconds);
            double step = Math.Clamp(delta * alpha, -visibleTicks * MaxStepViewportRatio, visibleTicks * MaxStepViewportRatio);
            double next = offset + step;
            if (Math.Abs(_target - next) <= StopEpsilonTicks) Reset();
            return next;
        }
    }
}
