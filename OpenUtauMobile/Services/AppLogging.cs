using System;
using System.IO;
using System.Text;
using Serilog;
using Serilog.Sinks.File;

namespace OpenUtauMobile.Services;

/// <summary>统一各平台的编译配置日志级别与文件缓冲策略。</summary>
public static class AppLogging
{
    private const int FileBufferSize = 256 * 1024;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    static AppLogging()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.CloseAndFlush();
    }

    public static void Initialize(string? filePath = null)
    {
        // 先释放旧文件句柄，避免重复初始化时争用同一个日志文件。
        Log.CloseAndFlush();
        LoggerConfiguration configuration = new();
#if DEBUG
        configuration.MinimumLevel.Debug().WriteTo.Debug();
#else
        configuration.MinimumLevel.Information();
#endif
        if (filePath != null)
        {
            configuration.WriteTo.File(filePath,
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: null,
                retainedFileCountLimit: null,
                buffered: true,
                flushToDiskInterval: FlushInterval,
                encoding: Encoding.UTF8,
                hooks: new BufferedFileHooks());
        }
        Log.Logger = configuration.CreateLogger();
        Log.Information("Application logging initialized");
    }

    private sealed class BufferedFileHooks : FileLifecycleHooks
    {
        public override Stream OnFileOpened(string path, Stream underlyingStream, Encoding encoding)
        {
            return new BufferedStream(underlyingStream, FileBufferSize);
        }
    }
}
