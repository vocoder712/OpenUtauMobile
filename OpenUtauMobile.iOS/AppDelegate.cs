using System;
using System.Text;
using System.Threading.Tasks;
using Foundation;
using Avalonia;
using Avalonia.Media;
using Avalonia.iOS;
using Avalonia.ReactiveUI;
using OpenUtau.Core;
using OpenUtauMobile.Services;
using Serilog;
using UIKit;


namespace OpenUtauMobile.iOS;

// The UIApplicationDelegate for the application. This class is responsible for launching the 
// User Interface of the application, as well as listening (and optionally responding) to 
// application events from iOS.
[Register("AppDelegate")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // 注册编码提供程序以支持更多编码格式
        InitLogging();
        // TODO: iOS尚未实现音频输出
        ServiceHub.TryGetPlatformAccentFallback = TryGetPlatformAccentFallback;
        return base.CustomizeAppBuilder(builder)
            .UseReactiveUI();
    }

    private static void InitLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Debug()
            .WriteTo.Logger(lc => lc
                .MinimumLevel.Information() // 
                .WriteTo.File(PathManager.Inst.LogFilePath, rollingInterval: RollingInterval.Day, encoding: Encoding.UTF8)) // 写入日志文件
            .CreateLogger();
        AppDomain.CurrentDomain.UnhandledException += (_, args) => {
            Log.Error((Exception)args.ExceptionObject, "未经处理的异常！"); // 未处理异常
            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification((Exception)args.ExceptionObject));
        };
        TaskScheduler.UnobservedTaskException += (_, args) => {
            Log.Error(args.Exception, "未观察到的 Task 异常！"); // 未观察到的 Task 异常
            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(args.Exception));
            args.SetObserved();
        };
        Log.Information("==========开始记录日志==========");
    }

    private static (bool success, Color color, string source) TryGetPlatformAccentFallback()
    {
        UIColor accent = UIColor.SystemBlue;
        accent.GetRGBA(out nfloat r, out nfloat g, out nfloat b, out _);
        Color color = Color.FromRgb(
            (byte)Math.Clamp((int)(r * 255), 0, 255),
            (byte)Math.Clamp((int)(g * 255), 0, 255),
            (byte)Math.Clamp((int)(b * 255), 0, 255));
        return (true, color, "iOS.UIKit.SystemBlue");
    }
}