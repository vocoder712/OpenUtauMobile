using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Systems;
using OpenUtauMobile.Services.Platform;
using Serilog;
using Environment = System.Environment;

namespace OpenUtauMobile.Android;

/// <summary>收集 Android 系统保留的历史异常退出记录。</summary>
internal sealed class AndroidCrashLogService : IPlatformCrashLogService
{
    private const int MaxExitRecords = 10;
    private const long MaxTraceBytes = 32L * 1024 * 1024;
    private readonly Context context;
    private readonly string storageDirectory;
    private readonly object collectionLock = new();
    private Task? collectionTask;

    public AndroidCrashLogService(Context context)
    {
        this.context = context.ApplicationContext ?? context;
        string privateRoot = this.context.FilesDir?.AbsolutePath
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        storageDirectory = Path.Combine(privateRoot, "CrashLogs");
    }

    public Task CollectPreviousExitAsync()
    {
        lock (collectionLock)
        {
            return collectionTask ??= Task.Run(CollectPreviousExitCore);
        }
    }

    public async Task<int> ExportAsync(string destination)
    {
        await CollectPreviousExitAsync();
        return await Task.Run(() => CreateArchive(destination));
    }

    private void CollectPreviousExitCore()
    {
        try
        {
            Directory.CreateDirectory(storageDirectory);
            if (!OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                WriteCollectionStatus("Android 11（API 30）以下版本无法由应用读取历史进程退出记录。", 0);
                return;
            }

            ActivityManager? activityManager =
                context.GetSystemService(Context.ActivityService) as ActivityManager;
            if (activityManager == null)
            {
                WriteCollectionStatus("无法获取 Android ActivityManager。", 0);
                return;
            }

            IList<ApplicationExitInfo> exitRecords =
                activityManager.GetHistoricalProcessExitReasons(null, 0, MaxExitRecords);
            long checkpoint = ReadCheckpoint();
            long processedTimestamp = checkpoint;
            int collectedCount = 0;

            foreach (ApplicationExitInfo exitInfo in GetNewExitRecords(exitRecords, checkpoint))
            {
                try
                {
                    if (IsDiagnosticExit(exitInfo.Reason))
                    {
                        CollectExitRecord(exitInfo);
                        collectedCount++;
                    }
                    processedTimestamp = exitInfo.Timestamp;
                    WriteCheckpoint(processedTimestamp);
                }
                catch (Exception exception)
                {
                    Log.Error(exception,
                        "Failed to collect Android historical exit record at {Timestamp}",
                        exitInfo.Timestamp);
                    break;
                }
            }

            WriteCollectionStatus(
                collectedCount == 0 ? "未发现新的异常退出记录。" : $"已收集 {collectedCount} 条异常退出记录。",
                processedTimestamp);
            Log.Information("Android historical exit collection completed with {Count} new records",
                collectedCount);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to collect Android historical exit records");
            TryWriteFailureStatus(exception);
        }
    }

    [SupportedOSPlatform("android30.0")]
    private void CollectExitRecord(ApplicationExitInfo exitInfo)
    {
        DateTimeOffset exitTime = DateTimeOffset.FromUnixTimeMilliseconds(exitInfo.Timestamp);
        ApplicationExitInfoReason reason = (ApplicationExitInfoReason)exitInfo.Reason;
        string reasonName = GetReasonName(reason);
        string recordName = $"{exitTime.UtcDateTime:yyyyMMdd-HHmmssfff}-{exitInfo.Pid}-{reasonName}";
        string recordDirectory = Path.Combine(storageDirectory, "records", recordName);
        Directory.CreateDirectory(recordDirectory);

        string traceStatus = CollectTrace(exitInfo, recordDirectory);
        StringBuilder report = new();
        report.AppendLine("OpenUtau Mobile Android 异常退出记录");
        report.AppendLine($"采集时间（UTC）: {DateTimeOffset.UtcNow:O}");
        report.AppendLine($"退出时间（UTC）: {exitTime:O}");
        report.AppendLine($"退出时间（本地）: {exitTime.ToLocalTime():O}");
        report.AppendLine($"原因: {reasonName} ({(int)reason})");
        report.AppendLine($"状态码/信号: {exitInfo.Status}");
        report.AppendLine($"进程: {exitInfo.ProcessName ?? "<unknown>"}");
        report.AppendLine($"PID: {exitInfo.Pid}");
        report.AppendLine($"进程重要性: {exitInfo.Importance}");
        report.AppendLine($"PSS: {exitInfo.Pss} kB");
        report.AppendLine($"RSS: {exitInfo.Rss} kB");
        report.AppendLine($"系统描述: {NormalizeValue(exitInfo.Description)}");
        report.AppendLine($"Trace: {traceStatus}");
        report.AppendLine();
        AppendDeviceInformation(report);
        WriteTextAtomically(Path.Combine(recordDirectory, "report.txt"), report.ToString());
    }

    [SupportedOSPlatform("android30.0")]
    private string CollectTrace(ApplicationExitInfo exitInfo, string recordDirectory)
    {
        try
        {
            using Stream? trace = exitInfo.TraceInputStream;
            if (trace == null)
            {
                return "系统未提供 trace；其环形缓冲区可能已经覆盖该记录。";
            }

            bool nativeTombstone = (ApplicationExitInfoReason)exitInfo.Reason ==
                                   ApplicationExitInfoReason.CrashNative &&
                                   OperatingSystem.IsAndroidVersionAtLeast(31);
            string fileName = nativeTombstone ? "tombstone.pb" : "trace.txt";
            string tracePath = Path.Combine(recordDirectory, fileName);
            using FileStream output = new(tracePath, FileMode.Create, FileAccess.Write, FileShare.None);
            long copied = CopyWithLimit(trace, output, MaxTraceBytes);
            if (trace.ReadByte() >= 0)
            {
                File.WriteAllText(Path.Combine(recordDirectory, "trace-truncated.txt"),
                    $"Trace 超过 {MaxTraceBytes} 字节，导出时已截断。", Encoding.UTF8);
                return $"{fileName}（已截断为 {copied} 字节）";
            }
            return $"{fileName}（{copied} 字节）";
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to copy Android exit trace");
            return $"读取失败: {exception.GetType().Name}: {NormalizeValue(exception.Message)}";
        }
    }

    private int CreateArchive(string destination)
    {
        string? destinationDirectory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        using ZipArchive archive = new(output, ZipArchiveMode.Create);

        ZipArchiveEntry deviceEntry = archive.CreateEntry("device-info.txt", CompressionLevel.Optimal);
        using (StreamWriter writer = new(deviceEntry.Open(), Encoding.UTF8))
        {
            StringBuilder deviceInfo = new();
            AppendDeviceInformation(deviceInfo);
            writer.Write(deviceInfo.ToString());
        }

        string statusPath = Path.Combine(storageDirectory, "collection-status.txt");
        if (File.Exists(statusPath))
        {
            AddFileToArchive(archive, statusPath, "collection-status.txt");
        }

        string recordsDirectory = Path.Combine(storageDirectory, "records");
        if (!Directory.Exists(recordsDirectory)) return 0;

        string[] reports = Directory.EnumerateFiles(recordsDirectory, "report.txt",
            SearchOption.AllDirectories).ToArray();
        foreach (string file in Directory.EnumerateFiles(recordsDirectory, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(storageDirectory, file).Replace('\\', '/');
            AddFileToArchive(archive, file, relativePath);
        }
        return reports.Length;
    }

    private void AppendDeviceInformation(StringBuilder report)
    {
        report.AppendLine("[应用]");
        PackageInfo? packageInfo = GetPackageInfo();
        AppendValue(report, "包名", context.PackageName);
        AppendValue(report, "版本名", packageInfo?.VersionName);
        long? versionCode = packageInfo == null ? null :
            OperatingSystem.IsAndroidVersionAtLeast(28)
                ? packageInfo.LongVersionCode
                : packageInfo.VersionCode;
        AppendValue(report, "版本代码", versionCode?.ToString(CultureInfo.InvariantCulture));

        Assembly appAssembly = typeof(OpenUtauMobile.App).Assembly;
        AppendValue(report, "程序集版本", appAssembly.GetName().Version?.ToString());
        AppendValue(report, "产品版本", appAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        foreach (string key in new[] { "CoreVersion", "BuildNumber", "BuildTimestamp", "SourceCommit", "ActionRunId" })
        {
            string? value = appAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == key)?.Value;
            AppendValue(report, key, value);
        }

        report.AppendLine();
        report.AppendLine("[Android]");
        AppendValue(report, "版本", Build.VERSION.Release);
        AppendValue(report, "SDK", ((int)Build.VERSION.SdkInt).ToString(CultureInfo.InvariantCulture));
        AppendValue(report, "代号", Build.VERSION.Codename);
        AppendValue(report, "增量版本", Build.VERSION.Incremental);
        AppendValue(report, "基础系统", Build.VERSION.BaseOs);
        AppendValue(report, "安全补丁", Build.VERSION.SecurityPatch);
        AppendValue(report, "构建 ID", Build.Id);
        AppendValue(report, "显示版本", Build.Display);
        AppendValue(report, "构建类型", Build.Type);
        AppendValue(report, "构建标签", Build.Tags);
        AppendValue(report, "构建指纹", Build.Fingerprint);

        report.AppendLine();
        report.AppendLine("[设备与 Vendor]");
        AppendValue(report, "制造商", Build.Manufacturer);
        AppendValue(report, "品牌", Build.Brand);
        AppendValue(report, "型号", Build.Model);
        AppendValue(report, "产品", Build.Product);
        AppendValue(report, "设备", Build.Device);
        AppendValue(report, "主板", Build.Board);
        AppendValue(report, "硬件", Build.Hardware);
        AppendValue(report, "Bootloader", Build.Bootloader);
        AppendValue(report, "支持 ABI", string.Join(", ", Build.SupportedAbis ?? []));
        AppendValue(report, "Radio", Build.RadioVersion);
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            AppendValue(report, "SoC 制造商", Build.SocManufacturer);
            AppendValue(report, "SoC 型号", Build.SocModel);
            AppendValue(report, "SKU", Build.Sku);
            AppendValue(report, "ODM SKU", Build.OdmSku);
        }

        report.AppendLine();
        report.AppendLine("[内核]");
        try
        {
            StructUtsname? uname = Os.Uname();
            AppendValue(report, "系统", uname?.Sysname);
            AppendValue(report, "Release", uname?.Release);
            AppendValue(report, "Version", uname?.Version);
            AppendValue(report, "架构", uname?.Machine);
        }
        catch (Exception exception)
        {
            AppendValue(report, "uname", $"读取失败: {exception.GetType().Name}");
        }
        AppendValue(report, "/proc/version", TryReadOneLine("/proc/version"));

        report.AppendLine();
        report.AppendLine("[运行环境]");
        AppendValue(report, ".NET", RuntimeInformation.FrameworkDescription);
        AppendValue(report, "进程架构", RuntimeInformation.ProcessArchitecture.ToString());
        AppendValue(report, "OS 架构", RuntimeInformation.OSArchitecture.ToString());
        AppendValue(report, "系统语言", CultureInfo.CurrentCulture.Name);
        AppendValue(report, "UI 语言", CultureInfo.CurrentUICulture.Name);
        AppendValue(report, "时区", TimeZoneInfo.Local.Id);
        AppendValue(report, "采集时间（UTC）", DateTimeOffset.UtcNow.ToString("O"));

        ActivityManager? activityManager =
            context.GetSystemService(Context.ActivityService) as ActivityManager;
        if (activityManager != null)
        {
            ActivityManager.MemoryInfo memory = new();
            activityManager.GetMemoryInfo(memory);
            AppendValue(report, "总内存", FormatBytes(memory.TotalMem));
            AppendValue(report, "可用内存", FormatBytes(memory.AvailMem));
            AppendValue(report, "低内存状态", memory.LowMemory.ToString());
            AppendValue(report, "低内存阈值", FormatBytes(memory.Threshold));
        }
    }

    private PackageInfo? GetPackageInfo()
    {
        try
        {
            return context.PackageManager?.GetPackageInfo(context.PackageName!, PackageInfoFlags.MetaData);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Failed to read Android package information");
            return null;
        }
    }

    private void WriteCollectionStatus(string message, long processedTimestamp)
    {
        StringBuilder status = new();
        status.AppendLine("OpenUtau Mobile Android 崩溃记录采集状态");
        status.AppendLine($"采集级别: {GetCollectionTier()}");
        status.AppendLine($"结果: {message}");
        status.AppendLine($"已处理的最后退出时间戳: {processedTimestamp}");
        status.AppendLine($"更新时间（UTC）: {DateTimeOffset.UtcNow:O}");
        WriteTextAtomically(Path.Combine(storageDirectory, "collection-status.txt"), status.ToString());
    }

    private void TryWriteFailureStatus(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(storageDirectory);
            WriteCollectionStatus(
                $"采集失败: {exception.GetType().Name}: {NormalizeValue(exception.Message)}",
                ReadCheckpoint());
        }
        catch
        {
            // 崩溃日志采集不能影响应用启动。
        }
    }

    private long ReadCheckpoint()
    {
        string path = Path.Combine(storageDirectory, "last-exit-timestamp.txt");
        return File.Exists(path) && long.TryParse(File.ReadAllText(path), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out long timestamp) ? timestamp : 0;
    }

    private void WriteCheckpoint(long timestamp)
    {
        WriteTextAtomically(Path.Combine(storageDirectory, "last-exit-timestamp.txt"),
            timestamp.ToString(CultureInfo.InvariantCulture));
    }

    [SupportedOSPlatform("android30.0")]
    private static bool IsDiagnosticExit(int reason) => (ApplicationExitInfoReason)reason is
        ApplicationExitInfoReason.Anr or
        ApplicationExitInfoReason.Crash or
        ApplicationExitInfoReason.CrashNative or
        ApplicationExitInfoReason.DependencyDied or
        ApplicationExitInfoReason.ExcessiveResourceUsage or
        ApplicationExitInfoReason.InitializationFailure or
        ApplicationExitInfoReason.LowMemory or
        ApplicationExitInfoReason.Other or
        ApplicationExitInfoReason.Signaled or
        ApplicationExitInfoReason.Unknown;

    [SupportedOSPlatform("android30.0")]
    private static ApplicationExitInfo[] GetNewExitRecords(
        IEnumerable<ApplicationExitInfo> records, long checkpoint) => records
        .Where(info => info.Timestamp > checkpoint)
        .OrderBy(info => info.Timestamp)
        .ToArray();

    [SupportedOSPlatform("android30.0")]
    private static string GetReasonName(ApplicationExitInfoReason reason) => reason switch
    {
        ApplicationExitInfoReason.Anr => "anr",
        ApplicationExitInfoReason.Crash => "managed-crash",
        ApplicationExitInfoReason.CrashNative => "native-crash",
        ApplicationExitInfoReason.DependencyDied => "dependency-died",
        ApplicationExitInfoReason.ExcessiveResourceUsage => "excessive-resource-usage",
        ApplicationExitInfoReason.InitializationFailure => "initialization-failure",
        ApplicationExitInfoReason.LowMemory => "low-memory",
        ApplicationExitInfoReason.Signaled => "signaled",
        _ => "unknown",
    };

    private static string GetCollectionTier()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
            return "API 31+：退出原因、ANR trace、native tombstone protobuf";
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            return "API 30：退出原因与可用的 ANR trace";
        return "API 24-29：仅设备和应用信息";
    }

    private static long CopyWithLimit(Stream input, Stream output, long limit)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        while (total < limit)
        {
            int requested = (int)Math.Min(buffer.Length, limit - total);
            int read = input.Read(buffer, 0, requested);
            if (read == 0) break;
            output.Write(buffer, 0, read);
            total += read;
        }
        return total;
    }

    private static void AddFileToArchive(ZipArchive archive, string path, string entryName)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using Stream destination = entry.Open();
        using FileStream source = new(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        source.CopyTo(destination);
    }

    private static void WriteTextAtomically(string path, string content)
    {
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, content, Encoding.UTF8);
        File.Move(temporaryPath, path, true);
    }

    private static void AppendValue(StringBuilder builder, string name, string? value)
    {
        builder.Append(name).Append(": ").AppendLine(NormalizeValue(value));
    }

    private static string NormalizeValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "<unknown>"
            : value.Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal);

    private static string TryReadOneLine(string path)
    {
        try
        {
            return File.ReadLines(path).FirstOrDefault() ?? "<empty>";
        }
        catch (Exception exception)
        {
            return $"读取失败: {exception.GetType().Name}";
        }
    }

    private static string FormatBytes(long bytes) =>
        $"{bytes / 1024d / 1024d / 1024d:0.##} GiB ({bytes.ToString(CultureInfo.InvariantCulture)} bytes)";
}
