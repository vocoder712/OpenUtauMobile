using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;

namespace OpenUtauMobile.Android;

[Application]
public class AndroidApp : AvaloniaAndroidApplication<App>
{
    protected AndroidApp(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        AppBuilder configuredBuilder = base.CustomizeAppBuilder(builder);
        return MainActivity.ConfigureAppBuilder(configuredBuilder);
    }
}
