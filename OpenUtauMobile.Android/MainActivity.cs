using System;
using System.IO;
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
using ReactiveUI.Avalonia;
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtauMobile.Android.Audio;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
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
    WindowSoftInputMode = SoftInput.AdjustResize, // 键盘弹出时调整布局
    ResizeableActivity = true, // 允许调整大小
    HardwareAccelerated = true, // 启用硬件加速
    ConfigurationChanges = ConfigChanges.Orientation | 
                           ConfigChanges.ScreenSize | 
                           ConfigChanges.UiMode)]
// 注册对 file:// 和 content:// 协议的支持
[IntentFilter(actions:[Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "file", // file协议
    DataHost = "*",
    DataMimeType = "*/*",
    DataPathPattern = ".*\\\\.ustx")] // 匹配 ".*\\.ustx"
[IntentFilter(actions:[Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "content",
    DataPathPattern = ".*\\\\.ustx")]
[IntentFilter(actions:[Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "content",
    DataMimeType = "application/yaml")]
[IntentFilter(actions:[Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "content",
    DataMimeType = "application/x-yaml")]
[IntentFilter(actions:[Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "content",
    DataMimeType = "text/yaml")]
[IntentFilter(actions:[Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "content",
    DataMimeType = "application/octet-stream")]
public class MainActivity : AvaloniaMainActivity
{
    private static MainActivity? _currentActivity;
    internal static MainActivity? CurrentActivity => _currentActivity;

    internal static AppBuilder ConfigureAppBuilder(AppBuilder builder)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // 注册编码提供程序以支持更多编码格式
        InitPathManager();
        InitLogging();
        InitExceptionHandler();
        AndroidCrashLogService crashLogService = new(global::Android.App.Application.Context);
        ServiceHub.PlatformCrashLogService = crashLogService;
        _ = crashLogService.CollectPreviousExitAsync();
        ServiceHub.InitAudioOutput = InitAudioOutput; // 设置初始化音频输出的委托
        ServiceHub.ExternalUrlLauncher = new AndroidExternalUrlLauncher(() => CurrentActivity);
        ServiceHub.ExternalStorageService =
            new Storage.AndroidExternalStorageService(() => CurrentActivity); // 设置外部存储服务
        ServiceHub.TryGetPlatformAccentFallback = TryGetPlatformAccentFallback;
        ServiceHub.PlatformPerformanceProvider = new AndroidPerformanceProvider();
        ServiceHub.PlatformDisplayService = new AndroidDisplayService(() => CurrentActivity);
        return builder.UseReactiveUI(reactiveUIBuilder =>
        {
            reactiveUIBuilder.WithExceptionHandler(Observer.Create<Exception>(HandleReactiveException));
        });
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        HandleIntent(intent);
    }
    
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        _currentActivity = this;
        base.OnCreate(savedInstanceState);
        HandleIntent(Intent); // 处理启动时的 Intent
        ServiceHub.PlatformDisplayService?.Refresh();
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(_currentActivity, this))
        {
            _currentActivity = null;
        }

        base.OnDestroy();
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
            ServiceHub.PlatformDisplayService?.Refresh();
        }
    }
    /// <summary>
    /// 处理Intent
    /// </summary>
    /// <param name="intent"></param>
    private void HandleIntent(Intent? intent)
    {
        if (intent?.Action != global::Android.Content.Intent.ActionView || intent.Data == null)
        {
            return;
        }

        global::Android.Net.Uri uri = intent.Data;
        intent.SetData(null); // 防止 Activity 重建时重复处理同一请求
        _ = HandleViewIntentAsync(uri);
    }

    private async Task HandleViewIntentAsync(global::Android.Net.Uri uri)
    {
        try
        {
            string scheme = uri.Scheme ?? string.Empty;
            if (scheme.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                string path = uri.Path ?? string.Empty;
                EnsureUstxName(path);
                ExternalProjectOpenService.Enqueue(new ExternalProjectOpenRequest(path, false));
                return;
            }

            if (scheme.Equals("content", StringComparison.OrdinalIgnoreCase))
            {
                string path = await Task.Run(() => CopyContentUriToCache(uri));
                ExternalProjectOpenService.Enqueue(new ExternalProjectOpenRequest(path, true));
                return;
            }

            throw new NotSupportedException($"Unsupported project URI scheme: {scheme}");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to receive external USTX from {Uri}", uri);
            ExternalProjectOpenService.ReportFailure(ex);
        }
    }

    private string CopyContentUriToCache(global::Android.Net.Uri uri)
    {
        string displayName = GetContentDisplayName(uri) ?? uri.LastPathSegment ?? string.Empty;
        EnsureUstxName(displayName);
        ContentResolver resolver = ContentResolver
            ?? throw new InvalidOperationException("Android ContentResolver is unavailable.");

        string importDirectory = Path.Combine(PathManager.Inst.CachePath, "IntentImports");
        Directory.CreateDirectory(importDirectory);
        string destinationPath = Path.Combine(importDirectory, $"{Guid.NewGuid():N}.ustx");
        try
        {
            using Stream input = resolver.OpenInputStream(uri)
                ?? throw new IOException($"Unable to open project URI: {uri}");
            using FileStream output = File.Create(destinationPath);
            input.CopyTo(output);
            return destinationPath;
        }
        catch
        {
            File.Delete(destinationPath);
            throw;
        }
    }

    private string? GetContentDisplayName(global::Android.Net.Uri uri)
    {
        ContentResolver resolver = ContentResolver
            ?? throw new InvalidOperationException("Android ContentResolver is unavailable.");
        string[] projection = [global::Android.Provider.IOpenableColumns.DisplayName];
        using global::Android.Database.ICursor? cursor =
            resolver.Query(uri, projection, null, null, null);
        if (cursor == null || !cursor.MoveToFirst())
        {
            return null;
        }

        int column = cursor.GetColumnIndex(global::Android.Provider.IOpenableColumns.DisplayName);
        return column >= 0 ? cursor.GetString(column) : null;
    }

    private static void EnsureUstxName(string name)
    {
        if (!Path.GetExtension(name).Equals(".ustx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"External project is not a USTX file: {name}");
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
        OpenUtauMobile.Services.AppLogging.Initialize(PathManager.Inst.LogFilePath);
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
    }

    private static void HandleReactiveException(Exception exception)
    {
        Log.Error(exception, "ReactiveUI 中发生的未处理异常！");
        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("ReactiveUI 中发生的未处理异常", exception));
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
