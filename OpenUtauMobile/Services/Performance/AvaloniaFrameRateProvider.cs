using System;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;

namespace OpenUtauMobile.Services.Performance;

/// <summary>通过 Avalonia 动画帧回调测量 UI 的有效刷新节拍。</summary>
public sealed class AvaloniaFrameRateProvider : IFrameRateProvider
{
    private static readonly TimeSpan MeasurementInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StaleTimeout = TimeSpan.FromSeconds(2);

    private readonly object _stateLock = new object();
    private readonly TopLevel _topLevel;
    private bool _isRunning;
    private int _generation;
    private TimeSpan? _windowStart;
    private int _frameIntervalCount;
    private long? _lastFrameTimestamp;
    private FrameRateMetrics? _latestMetrics;

    public AvaloniaFrameRateProvider(TopLevel topLevel)
    {
        _topLevel = topLevel;
    }

    public void Start()
    {
        int generation;
        lock (_stateLock)
        {
            if (_isRunning)
            {
                return;
            }

            _isRunning = true;
            generation = ++_generation;
            ResetMetrics();
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            RequestNextFrame(generation);
        }
        else
        {
            Dispatcher.UIThread.Post(() => RequestNextFrame(generation));
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (!_isRunning)
            {
                return;
            }

            _isRunning = false;
            _generation++;
            ResetMetrics();
        }
    }

    public FrameRateMetrics? Capture()
    {
        lock (_stateLock)
        {
            if (!_isRunning ||
                _latestMetrics is not FrameRateMetrics metrics ||
                _lastFrameTimestamp is not long lastFrameTimestamp ||
                Stopwatch.GetElapsedTime(lastFrameTimestamp) > StaleTimeout)
            {
                return null;
            }

            return metrics;
        }
    }

    private void RequestNextFrame(int generation)
    {
        lock (_stateLock)
        {
            if (!_isRunning || generation != _generation)
            {
                return;
            }
        }

        _topLevel.RequestAnimationFrame(timestamp => OnAnimationFrame(timestamp, generation));
    }

    private void OnAnimationFrame(TimeSpan timestamp, int generation)
    {
        lock (_stateLock)
        {
            if (!_isRunning || generation != _generation)
            {
                return;
            }

            _lastFrameTimestamp = Stopwatch.GetTimestamp();
            if (_windowStart is not TimeSpan windowStart)
            {
                _windowStart = timestamp;
            }
            else
            {
                _frameIntervalCount++;
                TimeSpan elapsed = timestamp - windowStart;
                if (elapsed >= MeasurementInterval && elapsed > TimeSpan.Zero)
                {
                    double elapsedSeconds = elapsed.TotalSeconds;
                    _latestMetrics = new FrameRateMetrics(
                        _frameIntervalCount / elapsedSeconds,
                        elapsed.TotalMilliseconds / _frameIntervalCount);
                    _windowStart = timestamp;
                    _frameIntervalCount = 0;
                }
            }
        }

        RequestNextFrame(generation);
    }

    private void ResetMetrics()
    {
        _windowStart = null;
        _frameIntervalCount = 0;
        _lastFrameTimestamp = null;
        _latestMetrics = null;
    }
}
