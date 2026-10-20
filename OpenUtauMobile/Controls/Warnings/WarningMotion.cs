using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;

namespace OpenUtauMobile.Controls.Warnings
{
    /// <summary>告警功能的局部动画时钟；取消后不再写入旧展示会话。</summary>
    internal static class WarningMotion
    {
        /// <summary>通用淡入与收束的三次缓出进度。</summary>
        public static double Ease(double t) => 1 - Math.Pow(1 - t, 3);
        /// <summary>带有限回弹的阻尼磁吸进度，终点精确收敛。</summary>
        public static double Spring(double t) => t >= 1 ? 1 : 1 - Math.Exp(-7 * t) * Math.Cos(10 * t);
        /// <summary>对位置、尺寸和形状参数使用相同的插值。</summary>
        public static double Lerp(double from, double to, double progress) => from + (to - from) * progress;

        /// <summary>卡片的中性表面在飞行中连续变为悬浮球的强调表面。</summary>
        public static Color MixColor(Color from, Color to, double progress) => Color.FromArgb(
            (byte)Lerp(from.A, to.A, progress), (byte)Lerp(from.R, to.R, progress),
            (byte)Lerp(from.G, to.G, progress), (byte)Lerp(from.B, to.B, progress));

        /// <summary>在调用方的界面同步上下文逐帧更新，不使用每帧布局重建或偏好写盘。</summary>
        public static async Task Run(int milliseconds, Action<double> frame, CancellationToken token)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                double progress = Math.Min(1, clock.Elapsed.TotalMilliseconds / milliseconds);
                frame(progress);
                if (progress >= 1) return;
                await Task.Delay(16, token);
            }
        }
    }
}
