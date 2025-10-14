using OpenUtauMobile.Resources.Colors;
using OpenUtauMobile.Views;
using OpenUtauMobile.Views.Controls;
using Preferences = OpenUtau.Core.Util.Preferences;

namespace OpenUtauMobile
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            // 根据系统主题设置应用主题
            // 获取合并的资源字典
            var mergedDictionaries = Current?.Resources.MergedDictionaries;
            switch (Preferences.Default.Theme)
            {
                case 0: // 浅色主题
                    mergedDictionaries?.Add(new LightThemeColors());
                    if (Current != null)
                        Current.UserAppTheme = AppTheme.Light;
                    break;
                case 1: // 深色主题
                    mergedDictionaries?.Add(new DarkThemeColors());
                    if (Current != null)
                        Current.UserAppTheme = AppTheme.Dark;
                    break;
                case 2: // 跟随系统主题
                        // 根据系统主题添加相应的资源字典
                    switch (Current?.RequestedTheme)
                    {
                        case AppTheme.Dark:
                            mergedDictionaries?.Add(new DarkThemeColors());
                            break;
                        case AppTheme.Light:
                            mergedDictionaries?.Add(new LightThemeColors());
                            break;
                        case AppTheme.Unspecified:
                            mergedDictionaries?.Add(new DarkThemeColors());
                            break;
                        default:
                            break;
                    }
                    break;
                default:
                    mergedDictionaries?.Add(new DarkThemeColors());
                    break;
            }

        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            return new Window(new SplashScreenPage());
        }
    }
}