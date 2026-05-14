using System;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Util;
using Android.Views;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtauMobile.Android.Audio;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Messages;
using OpenUtauMobile.Services;
using ReactiveUI;
using Serilog;
using Environment = System.Environment;
using Log = Serilog.Log;
using Path = System.IO.Path;

namespace OpenUtauMobile.Android;

[Activity(
    Label = "@string/app_name", // Openutau Mobile 预览版
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true, // 主activity
    LaunchMode = LaunchMode.SingleTask, // 单例模式
    WindowSoftInputMode = SoftInput.AdjustPan, // 键盘弹出时调整布局
    ResizeableActivity = true, // 允许调整大小
    HardwareAccelerated = true, // 启用硬件加速
    ConfigurationChanges = ConfigChanges.Orientation | 
                           ConfigChanges.ScreenSize | 
                           ConfigChanges.UiMode)]
// 注册对 file:// 协议的支持
[IntentFilter(actions:[Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "file", // file协议
    DataHost = "*",
    DataMimeType = "*/*",
    DataPathPattern = ".*\\\\.ustx")] // 匹配 ".*\\.ustx"
public class MainActivity : AvaloniaMainActivity<App>
{
    private static MainActivity? _currentActivity;

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // 注册编码提供程序以支持更多编码格式
        InitPathManager();
        InitLogging();
        InitExceptionHandler();
        ServiceHub.InitAudioOutput = InitAudioOutput; // 设置初始化音频输出的委托
        ServiceHub.ExternalStorageService = new Storage.AndroidExternalStorageService(); // 设置外部存储服务
        ServiceHub.TryGetPlatformAccentFallback = TryGetPlatformAccentFallback;
        return base.CustomizeAppBuilder(builder)
            .UseReactiveUI();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandleIntent(intent);
    }
    
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _currentActivity = this;
        HandleIntent(Intent); // 处理启动时的 Intent
        EnterImmersiveMode();
    }
    /// <summary>
    /// 自动恢复沉浸模式
    /// </summary>
    /// <param name="hasFocus"></param>
    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);

        if (hasFocus)
        {
            EnterImmersiveMode();
        }
    }
    /// <summary>
    /// 进入沉浸模式
    /// </summary>
    private void EnterImmersiveMode()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.R) // Android 11+
        {
            Window?.SetDecorFitsSystemWindows(false);

            var controller = Window?.InsetsController;
            if (controller != null)
            {
                controller.Hide(WindowInsets.Type.StatusBars() | WindowInsets.Type.NavigationBars());

                controller.SystemBarsBehavior =
                    (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
            }
        }
        else
        {
            SystemUiFlags flags = SystemUiFlags.LayoutStable |
                                  SystemUiFlags.LayoutHideNavigation |
                                  SystemUiFlags.LayoutFullscreen |
                                  SystemUiFlags.HideNavigation |
                                  SystemUiFlags.Fullscreen |
                                  SystemUiFlags.ImmersiveSticky;

            Window?.DecorView.SystemUiVisibility = (StatusBarVisibility)flags;
        }
    }
    /// <summary>
    /// 处理Intent
    /// </summary>
    /// <param name="intent"></param>
    private static void HandleIntent(Intent? intent)
    {
        if (intent?.Data == null)
        {
            return;
        }
        try
        {
            string? data = intent.Data.ToString(); // 形如content://authority/xxx
            if (data == null)
            {
                return;
            }
            System.Diagnostics.Debug.WriteLine($"Received intent with data: {data}");
            MessageBus.Current.SendMessage(new OpenFileMessage(data)); // 发送打开文件消息
        }
        catch (Exception ex)
        {
            // 处理读取无权限或失败等异常
            System.Diagnostics.Debug.WriteLine($"Failed to open file: {ex.Message}");
        }
    }
    /// <summary>
    /// 初始化路径
    /// </summary>
    private static void InitPathManager() {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string cache = Path.Combine(localData, "Cache");
        PathManager.Inst.Configure(
            rootPath: localData,
            dataPath: localData,
            cachePath: cache,
            homePathIsAscii: true);
    }
    /// <summary>
    /// 初始化日志记录
    /// </summary>
    private static void InitLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Debug()
            .WriteTo.Logger(lc => lc
                .MinimumLevel.Information() // 
                .WriteTo.File(PathManager.Inst.LogFilePath, rollingInterval: RollingInterval.Day, encoding: Encoding.UTF8)) // 写入日志文件
            .CreateLogger();
        Log.Information("==========开始记录日志==========");
    }

    private static void InitExceptionHandler()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) => {
            Log.Error((Exception)args.ExceptionObject, "未经处理的异常！"); // 未处理异常
            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("未经处理的异常", (Exception)args.ExceptionObject));
        };
        TaskScheduler.UnobservedTaskException += (_, args) => {
            Log.Error(args.Exception, "未观察到的 Task 异常！"); // 未观察到的 Task 异常
            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("未观察到的 Task 异常", args.Exception));
            args.SetObserved();
        };
        RxApp.DefaultExceptionHandler = Observer.Create<Exception>(ex => 
        {
            Log.Error(ex, "ReactiveUI 中发生的未处理异常！");
            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("ReactiveUI 中发生的未处理异常", ex));
        });
    }

    /// <summary>
    /// 
    /// </summary>
    /// <remarks>AudioTrack > MiniAudio > Dummy</remarks>
    private static void InitAudioOutput()
    {
        string pref = OpenUtau.Core.Util.Preferences.Default.AudioBackend;
        Log.Information("初始化音频输出，偏好后端: {Backend}", string.IsNullOrEmpty(pref) ? "Auto" : pref);
        
        // Android 支持的后端优先级：AudioTrack > MiniAudio > Dummy
        // 如果指定了特定后端，优先尝试
        if (!string.IsNullOrEmpty(pref) && !pref.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            if (TryInitAudioBackend(pref))
            {
                return;
            }
            Log.Warning($"指定的音频后端 {pref} 初始化失败，尝试回退");
        }
        
        // 自动选择或回退：按优先级尝试
        string[] fallbackOrder = ["AudioTrack", "MiniAudio", "Dummy"];
        foreach (string backend in fallbackOrder)
        {
            if (TryInitAudioBackend(backend))
            {
                return;
            }
        }
        
        Log.Error("所有音频后端初始化失败");
    }
    
    /// <summary>
    /// 尝试初始化，失败返回false
    /// </summary>
    /// <param name="backend"></param>
    /// <returns></returns>
    private static bool TryInitAudioBackend(string backend)
    {
        try
        {
            switch (backend)
            {
                case "MiniAudio":
                    PlaybackManager.Inst.AudioOutput = new MiniAudioOutput();
                    Log.Information("使用 MiniAudio 音频后端");
                    return true;
                    
                case "AudioTrack":
                    PlaybackManager.Inst.AudioOutput = new AudioTrackOutput();
                    Log.Information("使用 AudioTrack 音频后端");
                    return true;
                    
                case "Dummy":
                    PlaybackManager.Inst.AudioOutput = new DummyAudioOutput();
                    Log.Information("使用 Dummy 音频后端（无声）");
                    return true;
                    
                default:
                    Log.Warning("未知的音频后端: {Backend}", backend);
                    return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "初始化音频后端 {Backend} 失败", backend);
            return false;
        }
    }

    private static (bool success, Color color, string source) TryGetPlatformAccentFallback()
    {
        MainActivity? activity = _currentActivity;
        if (activity?.Theme == null)
        {
            return (false, default, string.Empty);
        }

        TypedValue typedValue = new TypedValue();
        bool resolved = activity.Theme.ResolveAttribute(global::Android.Resource.Attribute.ColorAccent, typedValue, true);
        if (!resolved)
        {
            return (false, default, string.Empty);
        }

        int argb = typedValue.Data;
        byte a = (byte)((argb >> 24) & 0xFF);
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);
        if (a == 0)
        {
            a = 0xFF;
        }

        Color color = Color.FromArgb(a, r, g, b);
        string source = Build.VERSION.SdkInt >= BuildVersionCodes.S
            ? "Android.MaterialYou"
            : "Android.ThemeAccent";
        return (true, color, source);
    }
}