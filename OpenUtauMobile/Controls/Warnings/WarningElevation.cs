using Avalonia.Media;

namespace OpenUtauMobile.Controls.Warnings
{
    /// <summary>告警功能的 MD3 海拔阴影；Avalonia 用多层阴影表达 6dp 和 8dp 的物理海拔。</summary>
    internal static class WarningElevation
    {
        /// <summary>Level 3：FAB 的常态、聚焦及按下状态，以及正在拖动的填充卡片。</summary>
        public static BoxShadows Level3(Color color) => new(
            new BoxShadow { OffsetY = 3, Blur = 5, Spread = -1, Color = WithAlpha(color, 0.20) },
            [new BoxShadow { OffsetY = 6, Blur = 10, Color = WithAlpha(color, 0.14) },
             new BoxShadow { OffsetY = 1, Blur = 18, Color = WithAlpha(color, 0.12) }]);

        /// <summary>Level 4：鼠标悬停 FAB 时的 8dp 海拔。</summary>
        public static BoxShadows Level4(Color color) => new(
            new BoxShadow { OffsetY = 5, Blur = 5, Spread = -3, Color = WithAlpha(color, 0.20) },
            [new BoxShadow { OffsetY = 8, Blur = 10, Spread = 1, Color = WithAlpha(color, 0.14) },
             new BoxShadow { OffsetY = 3, Blur = 14, Spread = 2, Color = WithAlpha(color, 0.12) }]);

        private static Color WithAlpha(Color color, double alpha) =>
            Color.FromArgb((byte)(color.A * alpha), color.R, color.G, color.B);
    }
}
