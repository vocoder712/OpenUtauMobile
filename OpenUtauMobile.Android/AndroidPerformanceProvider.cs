using System;
using System.Globalization;
using System.IO;
using Android.App;
using Android.Content;
using OpenUtauMobile.Services.Performance;

namespace OpenUtauMobile.Android;

/// <summary>读取 Android PSS、系统内存和 /proc/stat CPU 时间。</summary>
internal sealed class AndroidPerformanceProvider : IPlatformPerformanceProvider
{
    private const string ProcStatPath = "/proc/stat";
    private const string CpuLinePrefix = "cpu ";
    private const int MinimumCpuFieldCount = 5;
    private const int CpuTotalFieldCount = 8;
    private const int IdleFieldIndex = 3;
    private const int IoWaitFieldIndex = 4;
    private const long BytesPerKibibyte = 1024L;
    private const double PercentScale = 100.0;

    private ulong? _previousIdleTime;
    private ulong? _previousTotalTime;

    public PlatformPerformanceMetrics Capture()
    {
        ActivityManager? activityManager = global::Android.App.Application.Context
            .GetSystemService(Context.ActivityService) as ActivityManager;

        long? appMemoryBytes = CaptureAppPssBytes(activityManager);
        long? totalMemoryBytes = null;
        long? availableMemoryBytes = null;
        if (activityManager != null)
        {
            ActivityManager.MemoryInfo memoryInfo = new ActivityManager.MemoryInfo();
            activityManager.GetMemoryInfo(memoryInfo);
            totalMemoryBytes = memoryInfo.TotalMem;
            availableMemoryBytes = memoryInfo.AvailMem;
        }

        return new PlatformPerformanceMetrics(
            appMemoryBytes,
            totalMemoryBytes,
            availableMemoryBytes,
            CaptureSystemCpuUsage());
    }

    private static long? CaptureAppPssBytes(ActivityManager? activityManager)
    {
        if (activityManager == null)
        {
            return null;
        }

        int[] processIds = [global::Android.OS.Process.MyPid()];
        global::Android.OS.Debug.MemoryInfo[]? memoryInfos = activityManager.GetProcessMemoryInfo(processIds);
        if (memoryInfos == null || memoryInfos.Length == 0)
        {
            return null;
        }

        return memoryInfos[0].TotalPss * BytesPerKibibyte;
    }

    private double? CaptureSystemCpuUsage()
    {
        try
        {
            using StreamReader reader = new StreamReader(ProcStatPath);
            string? cpuLine = reader.ReadLine();
            if (cpuLine == null || !cpuLine.StartsWith(CpuLinePrefix, StringComparison.Ordinal))
            {
                ResetCpuBaseline();
                return null;
            }

            string[] fields = cpuLine[CpuLinePrefix.Length..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < MinimumCpuFieldCount)
            {
                ResetCpuBaseline();
                return null;
            }

            ulong totalTime = 0UL;
            int totalFieldCount = Math.Min(fields.Length, CpuTotalFieldCount);
            for (int index = 0; index < totalFieldCount; index++)
            {
                totalTime += ulong.Parse(fields[index], CultureInfo.InvariantCulture);
            }

            ulong idleTime = ulong.Parse(fields[IdleFieldIndex], CultureInfo.InvariantCulture) +
                             ulong.Parse(fields[IoWaitFieldIndex], CultureInfo.InvariantCulture);
            double? usage = null;
            if (_previousIdleTime is ulong previousIdle && _previousTotalTime is ulong previousTotal)
            {
                ulong idleDelta = idleTime - previousIdle;
                ulong totalDelta = totalTime - previousTotal;
                if (totalDelta > 0UL)
                {
                    usage = Math.Clamp(
                        (double)(totalDelta - Math.Min(idleDelta, totalDelta)) / totalDelta * PercentScale,
                        0.0,
                        PercentScale);
                }
            }

            _previousIdleTime = idleTime;
            _previousTotalTime = totalTime;
            return usage;
        }
        catch
        {
            ResetCpuBaseline();
            return null;
        }
    }

    private void ResetCpuBaseline()
    {
        _previousIdleTime = null;
        _previousTotalTime = null;
    }
}
