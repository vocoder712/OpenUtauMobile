using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace OpenUtauMobile.Controls
{
    /// <summary>为编辑器固定侧栏提供统一的 MD3 Level 3 双层阴影。</summary>
    public class EditorRailSurface : Border
    {
        /// <summary>随主题变化的语义阴影色。</summary>
        public static readonly StyledProperty<Color> ShadowColorProperty =
            AvaloniaProperty.Register<EditorRailSurface, Color>(nameof(ShadowColor));

        /// <summary>获取或设置侧栏的语义阴影色。</summary>
        public Color ShadowColor
        {
            get => GetValue(ShadowColorProperty);
            set => SetValue(ShadowColorProperty, value);
        }

        /// <summary>在主题阴影色变化时同步更新主阴影与环境阴影。</summary>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == ShadowColorProperty)
            {
                Color color = ShadowColor;
                // MD3 Level 3：主阴影 (0,1,3,0)，环境阴影 (0,4,8,3)。
                BoxShadow = new BoxShadows(
                    new BoxShadow
                    {
                        OffsetY = 1,
                        Blur = 3,
                        Color = Color.FromArgb((byte)(color.A * 0.30), color.R, color.G, color.B)
                    },
                    [new BoxShadow
                    {
                        OffsetY = 4,
                        Blur = 8,
                        Spread = 3,
                        Color = Color.FromArgb((byte)(color.A * 0.15), color.R, color.G, color.B)
                    }]);
            }
        }
    }
}