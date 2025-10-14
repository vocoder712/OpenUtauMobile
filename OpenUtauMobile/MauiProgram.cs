using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using OpenUtau.Core;
using OpenUtauMobile.Resources.Strings;
using OpenUtauMobile.Utils.Permission;
using Serilog;
using System.Text;
#if ANDROID
using OpenUtauMobile.Platforms.Android.Utils.Permission;
#endif
#if WINDOWS
using OpenUtauMobile.Platforms.Windows.Utils.Permission;
#endif
using OpenUtauMobile.Views;
using SkiaSharp.Views.Maui.Controls.Hosting;
using System.Globalization;

namespace OpenUtauMobile
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            // 注册编码提供程序
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit() // 使用MauiCommunityToolkit
                .UseSkiaSharp() // 使用SkiaSharp绘图
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                })
                ;

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            InitLogging(); //

            Log.Information("操作系统：" + DeviceInfo.Current.Platform.ToString()); // 操作系统
            Log.Information("操作系统版本：" + DeviceInfo.Current.VersionString); // 操作系统版本
            Log.Information("制造商：" + DeviceInfo.Current.Manufacturer); // 制造商
            Log.Information("设备型号：" + DeviceInfo.Current.Model); // 型号

            return builder.Build();
        }

        /// <summary>
        /// 初始化日志记录
        /// </summary>
        public static void InitLogging()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Debug()
                .WriteTo.Logger(lc => lc
                    .MinimumLevel.Information() // 
                    .WriteTo.File(PathManager.Inst.LogFilePath, rollingInterval: RollingInterval.Day, encoding: Encoding.UTF8)) // 写入日志文件
                //.WriteTo.Logger(lc => lc
                //    .MinimumLevel.ControlledBy(DebugViewModel.Sink.Inst.LevelSwitch)
                //    .WriteTo.Sink(DebugViewModel.Sink.Inst))
                .CreateLogger();
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler((sender, args) => {
                Log.Error((Exception)args.ExceptionObject, "未经处理的异常！"); // 未处理异常
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification((Exception)args.ExceptionObject));
            });
            Log.Information("==========开始记录日志==========");
        }
    }
}
