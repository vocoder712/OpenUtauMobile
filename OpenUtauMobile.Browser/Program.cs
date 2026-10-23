using System;
using System.IO;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using Avalonia.Threading;
using ReactiveUI.Avalonia;
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtauMobile;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Graphics;
using OpenUtauMobile.Browser.Services.Graphics;
using OpenUtauMobile.Browser.Services.Platform;
using OpenUtauMobile.Browser.Services;
using Serilog;

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        try
        {
            AppBuilder appBuilder = BuildAvaloniaApp()
                .UseReactiveUI(reactiveUIBuilder =>
                {
                    reactiveUIBuilder.WithExceptionHandler(Observer.Create<Exception>(HandleReactiveException));
                });
            await BrowserExternalUrlLauncher.InitializeAsync();
            BrowserGraphicsBackendPreferenceStore graphicsStore = await BrowserGraphicsBackendPreferenceStore.CreateAsync();
            BrowserGraphicsBackendProvider graphicsProvider = new();
            GraphicsBackendService graphicsService = new(graphicsProvider, graphicsStore);
            ServiceHub.GraphicsBackendService = graphicsService;
            await appBuilder.StartBrowserAppAsync("out",
                BrowserGraphicsBackendProvider.CreatePlatformOptions(graphicsService.StartupFallbackOrder));
        }
        catch (Exception ex)
        {
            try
            {
                // Surface minimal info to console to avoid triggering resource loading issues in WASM.
                Console.Error.WriteLine("Unhandled exception during startup:");
                Console.Error.WriteLine($"Type: {ex.GetType().FullName}");
                Console.Error.WriteLine($"Message: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine($"Inner: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                }
            }
            catch { }

            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        InitPathManager();
        InitLogging();
        InitExceptionHandler();
        ServiceHub.InitAudioOutput = InitAudioOutput;
        ServiceHub.FlushFileSystemAsync = BrowserFileSystem.FlushAsync;
        ServiceHub.SandboxFileTransferService = new BrowserSandboxFileTransferService();
        ServiceHub.CreateSandboxTransferFileAsync = BrowserSandboxTransferFile.CreateAsync;
        ServiceHub.ExternalUrlLauncher = new BrowserExternalUrlLauncher();
        ServiceHub.TryGetPlatformAccentFallback = TryGetPlatformAccentFallback;
        return AppBuilder.Configure<App>()
            .WithInterFont()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "fonts:Inter#Inter",
                FontFallbacks =
                [
                    new FontFallback
                    {
                        FontFamily = new FontFamily("avares://OpenUtauMobile.Browser/Assets/Fonts#Noto Sans CJK SC")
                    }
                ]
            });
    }

    private static void InitPathManager()
    {
        if (OperatingSystem.IsBrowser())
        {
            // 使用虚拟文件系统路径，正常构造实例并跳过桌面默认路径探测。
            PathManagerInitialization.Initialize(
                rootPath: "/OpenUtauMobile",
                dataPath: "/OpenUtauMobile/Data",
                cachePath: "/OpenUtauMobile/Data/Cache");
            return;
        }

        // Non-browser fallback
        string dataHome = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        string rootPath = Path.Combine(dataHome, "OpenUtauMobile");
        string dataPath = Path.Combine(dataHome, "OpenUtauMobile");
        string cachePath = Path.Combine(dataPath, "Cache");
        PathManagerInitialization.Initialize(
            rootPath: rootPath,
            dataPath: dataPath,
            cachePath: cachePath);
    }

    private static void InitLogging()
    {
        AppLogging.Initialize();
        // 浏览器控制台输出需要跨线程代理，避免逐条输出拖动命令及已捕获异常。
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Sink(new BrowserConsoleLogSink())
            .WriteTo.Sink(PluginLoadErrorReporter.Instance)
            .CreateLogger();
        Log.Information("==========Start logging==========");
    }

    private static void InitAudioOutput()
    {
        string pref = OpenUtau.Core.Util.Preferences.Default.AudioBackend;
        Log.Information("Init audio output, preferred backend: {Backend}", string.IsNullOrEmpty(pref) ? "Auto" : pref);

        if (!string.IsNullOrEmpty(pref) && !pref.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            if (TryInitAudioBackend(pref))
            {
                return;
            }
            Log.Warning("Preferred backend {Backend} failed to initialize; falling back.", pref);
        }

        string[] fallbackOrder = ["Dummy"];

        foreach (string backend in fallbackOrder)
        {
            if (TryInitAudioBackend(backend))
            {
                return;
            }
        }

        Log.Error("All audio backends failed to initialize.");
    }

    private static bool TryInitAudioBackend(string backend)
    {
        try
        {
            switch (backend)
            {
                case "Dummy":
                    PlaybackManager.Inst.AudioOutput = new DummyAudioOutput();
                    Log.Information("Using Dummy audio backend (silent).");
                    return true;

                default:
                    Log.Warning("Unknown audio backend: {Backend}", backend);
                    return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize audio backend {Backend}.", backend);
            return false;
        }
    }

    private static void InitExceptionHandler()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error((Exception)args.ExceptionObject, "Unhandled exception.");
            // 致命运行时异常不能通过弹窗恢复，也不应继续向正在退出的 UI 派发任务。
            if (!args.IsTerminating)
            {
                PostErrorNotification((Exception)args.ExceptionObject);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception.");
            args.SetObserved();
            PostErrorNotification(args.Exception);
        };
    }

    private static void HandleReactiveException(Exception exception)
    {
        Log.Error(exception, "Unhandled ReactiveUI exception.");
        PostErrorNotification(exception);
    }

    /// <summary>将可恢复的后台异常通知派发到 UI 线程。</summary>
    private static void PostErrorNotification(Exception exception)
    {
        Dispatcher.UIThread.Post(() => DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(exception)));
    }

    private static (bool success, Color color, string source) TryGetPlatformAccentFallback()
    {
        return (false, default, string.Empty);
    }
}
