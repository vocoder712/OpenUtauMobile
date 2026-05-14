using System;
using Avalonia;
using Avalonia.Threading;

namespace OpenUtauMobile.Controls.Gestures;

/// <summary>
/// 处理拖拽惯性计时并以像素为单位发出平移增量。
/// 与具体业务逻辑和控件无关
/// </summary>
public sealed class ViewportMotionController : IDisposable
{
    private enum MotionPhase
    {
        Idle,
        DirectManipulation,
        Inertia,
    }

    private struct DragSample
    {
        public Vector DeltaPx;
        public ulong TimestampMs;
    }

    private readonly DispatcherTimer _timer;
    private readonly DragSample[] _samples = new DragSample[16];

    private MotionPhase _phase = MotionPhase.Idle;
    private int _sampleCount;
    private int _sampleWriteIndex;

    private Vector _inertiaVelocityPxPerMs;
    private long _lastInertiaTickMs;

    public ViewportMotionController()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnInertiaTick;
    }

    /// <summary>触发惯性滑动的最小速度 in px/ms.</summary>
    public double StartInertiaSpeedPxPerMs { get; set; } = 0.05;

    /// <summary>速度下降到该速度以下时停止惯性滑动，单位 px/ms.</summary>
    public double StopInertiaSpeedPxPerMs { get; set; } = 0.005;

    /// <summary>线性减速，单位 px/ms^2.</summary>
    public double FrictionPxPerMs2 { get; set; } = 0.0025;

    /// <summary>速度平均窗口大小，单位 ms.</summary>
    public ulong VelocityWindowMs { get; set; } = 80;

    /// <summary>速度平均的最小样本数。</summary>
    public int MinVelocitySamples { get; set; } = 3;

    public bool IsRunning => _phase != MotionPhase.Idle;

    public bool IsInertiaRunning => _phase == MotionPhase.Inertia;

    /// <summary>以像素为单位发出视口平移增量。</summary>
    public Action<Vector>? PanDelta;

    /// <summary>当运动会话达到空闲状态时触发；true 表示被中断。</summary>
    public Action<bool>? MotionCompleted;

    public void BeginDirectManipulation(ulong timestampMs)
    {
        ResetSamples();
        _phase = MotionPhase.DirectManipulation;
        AddSample(new Vector(), timestampMs);
        StopInertiaTimer();
    }

    public void UpdateDirectManipulation(Vector deltaPx, ulong timestampMs)
    {
        if (_phase != MotionPhase.DirectManipulation)
        {
            return;
        }

        PanDelta?.Invoke(deltaPx);
        AddSample(deltaPx, timestampMs);
    }

    public void EndDirectManipulation(ulong timestampMs)
    {
        if (_phase != MotionPhase.DirectManipulation)
        {
            return;
        }

        Vector startVelocity = EstimateVelocity(timestampMs);
        if (startVelocity.Length < StartInertiaSpeedPxPerMs)
        {
            CompleteMotion(interrupted: false);
            return;
        }

        _phase = MotionPhase.Inertia;
        _inertiaVelocityPxPerMs = startVelocity;
        _lastInertiaTickMs = Environment.TickCount64;
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }
    }

    public void Cancel()
    {
        if (_phase == MotionPhase.Idle)
        {
            return;
        }

        CompleteMotion(interrupted: true);
    }

    public void Stop()
    {
        if (_phase == MotionPhase.Idle)
        {
            return;
        }

        CompleteMotion(interrupted: false);
    }

    public void Dispose()
    {
        StopInertiaTimer();
        _timer.Tick -= OnInertiaTick;
    }

    private void OnInertiaTick(object? sender, EventArgs e)
    {
        if (_phase != MotionPhase.Inertia)
        {
            StopInertiaTimer();
            return;
        }

        long nowMs = Environment.TickCount64;
        double dtMs = nowMs - _lastInertiaTickMs;
        _lastInertiaTickMs = nowMs;

        if (dtMs <= 0)
        {
            return;
        }

        // Clamp dt to avoid a single large hitch causing a huge jump.
        dtMs = Math.Min(dtMs, 34);

        double speed = _inertiaVelocityPxPerMs.Length;
        if (speed <= StopInertiaSpeedPxPerMs)
        {
            CompleteMotion(interrupted: false);
            return;
        }

        double nextSpeed = Math.Max(0, speed - FrictionPxPerMs2 * dtMs);
        Vector dir = speed > 0 ? _inertiaVelocityPxPerMs / speed : new Vector();
        double avgSpeed = (speed + nextSpeed) * 0.5;
        Vector deltaPx = dir * (avgSpeed * dtMs);

        PanDelta?.Invoke(deltaPx);
        _inertiaVelocityPxPerMs = dir * nextSpeed;

        if (nextSpeed <= StopInertiaSpeedPxPerMs)
        {
            CompleteMotion(interrupted: false);
        }
    }

    private void AddSample(Vector deltaPx, ulong timestampMs)
    {
        _samples[_sampleWriteIndex] = new DragSample { DeltaPx = deltaPx, TimestampMs = timestampMs };
        _sampleWriteIndex = (_sampleWriteIndex + 1) % _samples.Length;
        if (_sampleCount < _samples.Length)
        {
            _sampleCount++;
        }
    }

    private Vector EstimateVelocity(ulong endTimestampMs)
    {
        if (_sampleCount == 0)
        {
            return new Vector();
        }

        ulong windowStart = endTimestampMs > VelocityWindowMs ? endTimestampMs - VelocityWindowMs : 0;
        Vector sumDelta = new();
        ulong earliestTs = endTimestampMs;
        int selected = 0;

        for (int i = 0; i < _sampleCount; i++)
        {
            int idx = (_sampleWriteIndex - 1 - i + _samples.Length) % _samples.Length;
            DragSample sample = _samples[idx];
            if (sample.TimestampMs < windowStart)
            {
                break;
            }

            sumDelta += sample.DeltaPx;
            earliestTs = sample.TimestampMs;
            selected++;
        }

        if (selected < MinVelocitySamples)
        {
            return new Vector();
        }

        ulong dtMs = endTimestampMs > earliestTs ? endTimestampMs - earliestTs : 0;
        if (dtMs == 0)
        {
            return new Vector();
        }

        return sumDelta / dtMs;
    }

    private void ResetSamples()
    {
        _sampleCount = 0;
        _sampleWriteIndex = 0;
    }

    private void CompleteMotion(bool interrupted)
    {
        StopInertiaTimer();
        ResetSamples();
        _inertiaVelocityPxPerMs = new Vector();
        _phase = MotionPhase.Idle;
        MotionCompleted?.Invoke(interrupted);
    }

    private void StopInertiaTimer()
    {
        if (_timer.IsEnabled)
        {
            _timer.Stop();
        }
    }
}