using System;
using System.IO;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Microsoft.Win32;
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtauMobile.Windows.Audio;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;
using Serilog;

namespace OpenUtauMobile.Windows;

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
        ServiceHub.ExternalStorageService = new Storage.WindowsExternalStorageService();
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

        string[] fallbackOrder = ["NAudio", "MiniAudio", "Dummy"];

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
                case "NAudio":
                    PlaybackManager.Inst.AudioOutput = new NAudioOutput();
                    Log.Information("Using NAudio audio backend.");
                    return true;

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
        if (TryReadDwmColor("AccentColor", out Color accent))
        {
            return (true, accent, "Windows.Registry.AccentColor");
        }

        if (TryReadDwmColor("ColorizationColor", out Color colorization))
        {
            return (true, colorization, "Windows.Registry.ColorizationColor");
        }

        return (false, default, string.Empty);
    }

    private static bool TryReadDwmColor(string valueName, out Color color)
    {
        color = default;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\DWM");
            object? value = key?.GetValue(valueName);
            if (value is null)
            {
                return false;
            }

            uint raw = value switch
            {
                int i => unchecked((uint)i),
                long l => unchecked((uint)l),
                _ => 0u
            };

            byte r = (byte)((raw >> 16) & 0xFF);
            byte g = (byte)((raw >> 8) & 0xFF);
            byte b = (byte)(raw & 0xFF);
            color = Color.FromRgb(r, g, b);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

