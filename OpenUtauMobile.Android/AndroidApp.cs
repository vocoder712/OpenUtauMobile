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
        // 基类及后续服务可能提前读取偏好，先确保路径已完成初始化。
        MainActivity.InitPathManager();
        AppBuilder configuredBuilder = base.CustomizeAppBuilder(builder);
        return MainActivity.ConfigureAppBuilder(configuredBuilder);
    }
}
