using System;
using System.IO;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;
using Serilog;

namespace OpenUtauMobile.Linux;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        InitPathManager();
        InitLogging();
        InitExceptionHandler();
        ServiceHub.InitAudioOutput = InitAudioOutput;
        ServiceHub.ExternalStorageService = new Storage.LinuxExternalStorageService();
        ServiceHub.TryGetPlatformAccentFallback = TryGetPlatformAccentFallback;
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI()
            .LogToTrace();
    }

    private static void InitPathManager()
    {
        string dataHome = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        string rootPath = Path.Combine(dataHome, "OpenUtauMobile");
        string dataPath = Path.Combine(dataHome, "OpenUtauMobile");
        string cachePath = Path.Combine(dataPath, "Cache");
        PathManager.Inst.Configure(
            rootPath: rootPath,
            dataPath: dataPath,
            cachePath: cachePath);
    }

    private static void InitLogging()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Debug()
            .WriteTo.Logger(lc => lc
                .MinimumLevel.Information()
                .WriteTo.File(PathManager.Inst.LogFilePath, rollingInterval: RollingInterval.Day, encoding: Encoding.UTF8))
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

        string[] fallbackOrder = ["MiniAudio", "Dummy"];

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
                case "MiniAudio":
                    PlaybackManager.Inst.AudioOutput = new MiniAudioOutput();
                    Log.Information("Using MiniAudio audio backend.");
                    return true;

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
            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification((Exception)args.ExceptionObject));
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception.");
            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(args.Exception));
            args.SetObserved();
        };
        RxApp.DefaultExceptionHandler = Observer.Create<Exception>(ex =>
        {
            Log.Error(ex, "Unhandled ReactiveUI exception.");
            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
        });
    }

    private static (bool success, Color color, string source) TryGetPlatformAccentFallback()
    {
        return (false, default, string.Empty);
    }
}

