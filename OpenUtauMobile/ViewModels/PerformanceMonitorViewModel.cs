using System;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Performance;
using ReactiveUI;

namespace OpenUtauMobile.ViewModels;

/// <summary>将性能采样结果格式化为轻量悬浮层文本。</summary>
public sealed class PerformanceMonitorViewModel : ReactiveObject, IDisposable
{
    private const double BytesPerMebibyte = 1024.0 * 1024.0;
    private const double BytesPerGibibyte = BytesPerMebibyte * 1024.0;
    private const string MissingValue = "N/A";

    private readonly PerformanceMonitorService _monitor = PerformanceMonitorService.Instance;
    private bool _isActive;
    private bool _isEnabled;
    private string _appMemoryText = string.Empty;
    private string _managedHeapText = string.Empty;
    private string _systemMemoryText = string.Empty;
    private string _cpuText = string.Empty;
    private string _frameRateText = string.Empty;

    public bool IsEnabled
    {
        get => _isEnabled;
        private set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
    }

    public string AppMemoryText
    {
        get => _appMemoryText;
        private set => this.RaiseAndSetIfChanged(ref _appMemoryText, value);
    }

    public string ManagedHeapText
    {
        get => _managedHeapText;
        private set => this.RaiseAndSetIfChanged(ref _managedHeapText, value);
    }

    public string SystemMemoryText
    {
        get => _systemMemoryText;
        private set => this.RaiseAndSetIfChanged(ref _systemMemoryText, value);
    }

    public string CpuText
    {
        get => _cpuText;
        private set => this.RaiseAndSetIfChanged(ref _cpuText, value);
    }

    public string FrameRateText
    {
        get => _frameRateText;
        private set => this.RaiseAndSetIfChanged(ref _frameRateText, value);
    }

    public void Activate()
    {
        if (_isActive)
        {
            return;
        }

        _isActive = true;
        _monitor.EnabledChanged += OnEnabledChanged;
        _monitor.SnapshotUpdated += OnSnapshotUpdated;
        IsEnabled = _monitor.IsEnabled;

        if (_monitor.LatestSnapshot is PerformanceSnapshot snapshot)
        {
            OnSnapshotUpdated(snapshot);
        }

        _monitor.SetEnabled(Preferences.Default.PerformanceMonitorEnabled);
    }

    public void Dispose()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;
        _monitor.EnabledChanged -= OnEnabledChanged;
        _monitor.SnapshotUpdated -= OnSnapshotUpdated;
    }

    private void OnEnabledChanged(bool enabled)
    {
        IsEnabled = enabled;
    }

    private void OnSnapshotUpdated(PerformanceSnapshot snapshot)
    {
        AppMemoryText = $"{L.S("PerformanceMonitor.AppMemory")}: {FormatBytes(snapshot.AppMemoryBytes)}";
        ManagedHeapText = $"{L.S("PerformanceMonitor.ManagedHeap")}: {FormatBytes(snapshot.ManagedHeapBytes)}";
        SystemMemoryText = $"{L.S("PerformanceMonitor.SystemMemory")}: " +
                           $"{FormatBytes(snapshot.SystemMemoryUsedBytes)} / {FormatBytes(snapshot.SystemMemoryTotalBytes)}";
        CpuText = $"{L.S("PerformanceMonitor.Cpu")}: " +
                  $"{FormatPercent(snapshot.AppCpuUsagePercent)} {L.S("PerformanceMonitor.AppShort")} / " +
                  $"{FormatPercent(snapshot.SystemCpuUsagePercent)} {L.S("PerformanceMonitor.SystemShort")}";
        FrameRateText = $"{L.S("PerformanceMonitor.Fps")}: {FormatFrameRate(snapshot)}";
    }

    private static string FormatBytes(long? bytes)
    {
        if (bytes is not long value)
        {
            return MissingValue;
        }

        if (value >= BytesPerGibibyte)
        {
            return $"{value / BytesPerGibibyte:F2} GiB";
        }

        return $"{value / BytesPerMebibyte:F0} MiB";
    }

    private static string FormatPercent(double? value)
    {
        return value is double percent ? $"{percent:F1}%" : MissingValue;
    }

    private static string FormatFrameRate(PerformanceSnapshot snapshot)
    {
        if (snapshot.FramesPerSecond is not double framesPerSecond)
        {
            return MissingValue;
        }

        if (snapshot.AverageFrameTimeMilliseconds is double frameTime)
        {
            return $"{framesPerSecond:F1} ({frameTime:F1} ms)";
        }

        return $"{framesPerSecond:F1}";
    }
}

