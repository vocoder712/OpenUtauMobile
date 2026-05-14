using System;
using System.IO;
using System.Reflection;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtauMobile;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;
using Serilog;

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        // Keep FirstChance handler minimal to avoid triggering complex formatting/resource lookup in WASM.
        AppDomain.CurrentDomain.FirstChanceException += (s, e) =>
        {
            try
            {
                var ex = e.Exception;
                Console.Error.WriteLine($"FirstChanceException: {ex?.GetType().FullName}: {ex?.Message}");
            }
            catch { }
        };

        try
        {
            await BuildAvaloniaApp()
                .WithInterFont()
                .UseReactiveUI()
                .StartBrowserAppAsync("out");
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
        ServiceHub.TryGetPlatformAccentFallback = TryGetPlatformAccentFallback;
        return AppBuilder.Configure<App>();
    }

    private static void InitPathManager()
    {
        // On wasm/browser, PathManager ctor uses platform APIs that are unavailable and can crash the runtime.
        // Create an uninitialized instance and inject it into SingletonBase<PathManager> via reflection.
        if (OperatingSystem.IsBrowser())
        {
            try
            {
                var pmType = typeof(OpenUtau.Core.PathManager);
                var pmObj = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(pmType);
                var pm = (OpenUtau.Core.PathManager)pmObj;

                // Set auto-properties backing fields
                BindingFlags bf = BindingFlags.Instance | BindingFlags.NonPublic;
                pmType.GetField("<RootPath>k__BackingField", bf)?.SetValue(pm, "OpenUtauMobile");
                pmType.GetField("<DataPath>k__BackingField", bf)?.SetValue(pm, Path.Combine("OpenUtauMobile", "Data"));
                pmType.GetField("<CachePath>k__BackingField", bf)?.SetValue(pm, Path.Combine("OpenUtauMobile", "Data", "Cache"));
                pmType.GetField("<HomePathIsAscii>k__BackingField", bf)?.SetValue(pm, true);
                pmType.GetField("<IsInstalled>k__BackingField", bf)?.SetValue(pm, false);

                // Inject into SingletonBase<PathManager>.inst
                var singletonType = typeof(OpenUtau.Core.Util.SingletonBase<OpenUtau.Core.PathManager>);
                var instField = singletonType.GetField("inst", BindingFlags.Static | BindingFlags.NonPublic);
                if (instField != null)
                {
                    var lazy = new Lazy<OpenUtau.Core.PathManager>(() => pm);
                    instField.SetValue(null, lazy);
                }

                return;
            }
            catch (Exception e)
            {
                try { Console.Error.WriteLine($"InitPathManager(browser) failed: {e.GetType().FullName}: {e.Message}"); } catch { }
            }
        }

        // Non-browser fallback
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