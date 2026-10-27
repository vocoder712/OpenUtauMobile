using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Foundation;
using Avalonia;
using Avalonia.Media;
using Avalonia.iOS;
using ObjCRuntime;
using ReactiveUI.Avalonia;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.iOS.Services.Audio;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Graphics;
using OpenUtauMobile.iOS.Services.Graphics;
using OpenUtauMobile.iOS.Services.Platform;
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
        InitPathManager();
        InitLogging();
        ProbeOnnxRuntime();
        ServiceHub.InitAudioOutput = InitAudioOutput;
        ServiceHub.ExternalUrlLauncher = new IosExternalUrlLauncher();
        ServiceHub.SandboxFileTransferService = new OpenUtauMobile.Storage.StorageProviderSandboxTransferService();
        ServiceHub.TryGetPlatformAccentFallback = TryGetPlatformAccentFallback;
        IosGraphicsBackendProvider graphicsProvider = new();
        GraphicsBackendService graphicsService = new(graphicsProvider);
        ServiceHub.GraphicsBackendService = graphicsService;
        return base.CustomizeAppBuilder(builder)
            .With(IosGraphicsBackendProvider.CreatePlatformOptions(graphicsService.StartupFallbackOrder))
            .UseReactiveUI(_ =>
            {
            });
    }

    /// <summary>
    /// 接收文件提供程序交给应用打开的 USTX 工程。
    /// </summary>
    [Export("application:openURL:options:")]
    public new bool OpenUrl(UIApplication app, NSUrl url, NSDictionary options)
    {
        if (!IsSupportedProjectUrl(url))
        {
            return base.OpenUrl(app, url, options);
        }

        _ = OpenProjectDocumentAsync(url);
        return true;
    }

    /// <summary>
    /// 判断系统交给应用的文件是否为 USTX 工程。
    /// </summary>
    private static bool IsSupportedProjectUrl(NSUrl url)
    {
        return url.IsFileUrl &&
               !string.IsNullOrEmpty(url.Path) &&
               string.Equals(Path.GetExtension(url.Path), ".ustx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 在安全作用域仍有效时复制外部工程，并交给共享导航队列打开。
    /// </summary>
    private static async Task OpenProjectDocumentAsync(NSUrl url)
    {
        bool hasSecurityScope = false;
        try
        {
            hasSecurityScope = url.StartAccessingSecurityScopedResource();
            string localPath = await Task.Run(() => CopyProjectDocumentToCache(url));
            ExternalProjectOpenService.Enqueue(new ExternalProjectOpenRequest(localPath, DeleteSourceAfterRead: true));
        }
        catch (Exception exception)
        {
            Log.Error(exception, "无法从 iOS 文件提供程序导入 USTX 工程");
            ExternalProjectOpenService.ReportFailure(exception);
        }
        finally
        {
            if (hasSecurityScope)
            {
                url.StopAccessingSecurityScopedResource();
            }
        }
    }

    /// <summary>
    /// 协调读取系统文件提供程序中的工程，并复制到应用缓存。
    /// </summary>
    private static string CopyProjectDocumentToCache(NSUrl sourceUrl)
    {
        if (string.IsNullOrEmpty(sourceUrl.Path))
        {
            throw new IOException(L.S("Ios.Document.ImportFailed"));
        }
        string importDirectory = Path.Combine(PathManager.Inst.CachePath, "DocumentImports");
        Directory.CreateDirectory(importDirectory);
        string destinationPath = Path.Combine(importDirectory, $"{Guid.NewGuid():N}.ustx");

        using NSFileCoordinator coordinator = new(null);
        Exception? copyException = null;
        coordinator.CoordinateRead(
            sourceUrl,
            NSFileCoordinatorReadingOptions.WithoutChanges,
            out NSError? coordinationError,
            coordinatedUrl =>
            {
                try
                {
                    string coordinatedPath = coordinatedUrl.Path
                        ?? throw new IOException(L.S("Ios.Document.ImportFailed"));
                    File.Copy(coordinatedPath, destinationPath);
                }
                catch (Exception exception)
                {
                    copyException = exception;
                    try
                    {
                        if (File.Exists(destinationPath))
                        {
                            File.Delete(destinationPath);
                        }
                    }
                    catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
                    {
                        // 保留原始复制异常，缓存目录由应用独占。
                    }
                }
            });

        if (coordinationError != null)
        {
            throw new IOException(coordinationError.LocalizedDescription);
        }
        if (copyException != null)
        {
            throw new IOException(L.S("Ios.Document.ImportFailed"), copyException);
        }

        if (!File.Exists(destinationPath))
        {
            throw new IOException(L.S("Ios.Document.ImportFailed"));
        }

        return destinationPath;
    }

    private static void InitPathManager()
    {
        string dataPath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        string cachePath = NSFileManager.DefaultManager
            .GetUrls(NSSearchPathDirectory.CachesDirectory, NSSearchPathDomain.User)
            .FirstOrDefault()?.Path
            ?? throw new InvalidOperationException("无法获取 iOS 缓存目录。");
        PathManagerInitialization.Initialize(
            rootPath: dataPath,
            dataPath: dataPath,
            cachePath: cachePath);
    }

    private static void InitLogging()
    {
        AppLogging.Initialize(PathManager.Inst.LogFilePath);
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

    private static void ProbeOnnxRuntime()
    {
        try
        {
            OrtEnv environment = OrtEnv.Instance();
            using SessionOptions options = new SessionOptions();
            Log.Information("ONNX Runtime 原生环境加载成功: {Environment}", environment.GetType().FullName);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "ONNX Runtime 原生环境加载失败");
            throw;
        }
    }

    private static void InitAudioOutput()
    {
        try
        {
            PlaybackManager.Inst.AudioOutput = new IosAudioOutput();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "AVAudioEngine 初始化失败，回退到 Dummy 音频后端");
            PlaybackManager.Inst.AudioOutput = new DummyAudioOutput();
        }
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
