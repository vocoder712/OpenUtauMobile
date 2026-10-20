using Avalonia.Media;

namespace OpenUtauMobile.Controls.Tokens
{
    /// <summary>提示浮层的局部动效与 MD3 Snackbar 海拔。</summary>
    public static class ToastTokens
    {
        public static double EnterOffset => 24d;

        /// <summary>Level 3 的 6dp 海拔，以主阴影、半影和环境阴影表达。</summary>
        public static BoxShadows Elevation(Color color) => new(
            new BoxShadow { OffsetY = 3, Blur = 5, Spread = -1, Color = WithAlpha(color, 0.20) },
            [new BoxShadow { OffsetY = 6, Blur = 10, Color = WithAlpha(color, 0.14) },
             new BoxShadow { OffsetY = 1, Blur = 18, Color = WithAlpha(color, 0.12) }]);

        private static Color WithAlpha(Color color, double alpha) =>
            Color.FromArgb((byte)(color.A * alpha), color.R, color.G, color.B);
    }
}