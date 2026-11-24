using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Serilog;
using Serilog.Sinks.File;

namespace OpenUtauMobile.Services;

/// <summary>统一各平台的编译配置日志级别与文件缓冲策略。</summary>
public static class AppLogging
{
    private const string Unavailable = "不可用";
    private const int FileBufferSize = 256 * 1024;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);

    static AppLogging()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Log.CloseAndFlush();
    }

    /// <summary>初始化日志器，并在启动阶段逐项记录应用与平台信息。</summary>
    public static void Initialize(string? filePath = null, Action<ILogger>? logPlatformInfo = null,
        LoggerConfiguration? configuration = null)
    {
        // 先释放旧文件句柄，避免重复初始化时争用同一个日志文件。
        Log.CloseAndFlush();
        if (configuration == null)
        {
            configuration = new LoggerConfiguration();
#if DEBUG
            configuration.MinimumLevel.Debug().WriteTo.Debug();
#else
            configuration.MinimumLevel.Information();
#endif
        }
        configuration.WriteTo.Sink(PluginLoadErrorReporter.Instance);
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
        Avalonia.Logging.Logger.Sink = new AvaloniaLogSink(); // 将 Avalonia 日志转发到 Serilog
        Assembly assembly = typeof(App).Assembly;
        AssemblyMetadataAttribute[] metadata = [.. assembly.GetCustomAttributes<AssemblyMetadataAttribute>()];
        ILogger logger = Log.Logger;
        logger.Information("应用版本：{AppVersion:l}",
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Unavailable);
        logger.Information("构建号：{BuildNumber:l}",
            metadata.FirstOrDefault(attribute => attribute.Key == "BuildNumber")?.Value ?? Unavailable);
        logger.Information("OpenUtau 内核版本：{CoreVersion:l}",
            metadata.FirstOrDefault(attribute => attribute.Key == "CoreVersion")?.Value ?? Unavailable);
        logPlatformInfo?.Invoke(logger);
    }

    /// <summary>逐项记录操作系统信息，统一字段名称与物理内存单位。</summary>
    public static void LogOperatingSystem(ILogger logger, string? osType, Architecture? architecture,
        string? osVersion, string? kernelVersion, ulong? totalPhysicalMemoryBytes)
    {
        logger.Information("操作系统类型：{OsType:l}", osType ?? Unavailable);
        logger.Information("处理器架构：{OsArchitecture:l}", architecture?.ToString().ToLowerInvariant() ?? Unavailable);
        logger.Information("操作系统版本：{OsVersion:l}", osVersion ?? Unavailable);
        logger.Information("操作系统内核版本：{OsKernelVersion:l}", kernelVersion ?? Unavailable);
        logger.Information("总物理内存（字节）：{TotalPhysicalMemoryBytes}", (object?)totalPhysicalMemoryBytes ?? Unavailable);
    }

    private sealed class BufferedFileHooks : FileLifecycleHooks
    {
        public override Stream OnFileOpened(string path, Stream underlyingStream, Encoding encoding)
        {
            return new BufferedStream(underlyingStream, FileBufferSize);
        }
    }
}
