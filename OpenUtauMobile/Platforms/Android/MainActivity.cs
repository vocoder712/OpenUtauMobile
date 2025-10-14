using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace OpenUtauMobile
{
    // 可以多窗口。
    [Activity(Theme = "@style/Maui.SplashTheme",ScreenOrientation = ScreenOrientation.User , MainLauncher = true, LaunchMode = LaunchMode.Multiple, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop)
            {
                if (Window == null) return;
                // 状态栏背景色
                Window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#fe71a3"));
            }
        }
    }
}
