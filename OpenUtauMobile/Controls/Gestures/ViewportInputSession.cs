using System;
using Avalonia;
using Avalonia.Threading;

namespace OpenUtauMobile.Controls.Gestures;

/// <summary>合并连续输入并短时平滑，不推算系统已提供的惯性。整个会话只完成一次。</summary>
public sealed class ViewportInputSession : IDisposable
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private IEditorViewport? _viewport;
    private Vector _pan;
    private Vector _zoom;
    private Point _anchor;
    private bool _zoomed;
    private bool _ended;
    private long _lastInput;
    private long _lastFrame;

    public ViewportInputSession() => _timer.Tick += OnTick;

    public bool IsActive => _viewport != null;

    public void EndInput() => _ended = true;

    public bool Submit(IEditorViewport viewport, ViewportInput input, Point anchor, ViewportAxes axes)
    {
        if (input.Phase == ViewportInputPhase.Cancel) { Cancel(); return true; }
        if (input.Phase == ViewportInputPhase.End)
        {
            if (_viewport != viewport) return false;
            _ended = true;
            return true;
        }
        if (!viewport.CanNavigateViewport || !double.IsFinite(input.Delta.X) || !double.IsFinite(input.Delta.Y) ||
            !double.IsFinite(input.Scale) || input.Scale <= 0) return false;
        (Vector pan, Vector zoom) = ViewportInputMapping.Map(input, axes, viewport.SupportsVerticalZoom);
        if (pan == default && zoom == default) return false;
        if (_viewport != viewport)
        {
            Cancel();
            _viewport = viewport;
            viewport.BeginViewportInput();
            _lastFrame = Environment.TickCount64;
        }
        // 换方向立即丢弃该轴旧尾段，避免反向时仍继续前进。
        _pan = new Vector(Accumulate(_pan.X, pan.X), Accumulate(_pan.Y, pan.Y));
        // 锚点移动或滚动/缩放切换时先落实旧增量，不能把旧缩放套到新锚点。
        if (_zoom != default && (_anchor != anchor || pan != default)) FlushZoom();
        if (zoom != default) { _pan = default; _zoomed = true; }
        _zoom += zoom;
        _anchor = anchor;
        _ended = false;
        _lastInput = Environment.TickCount64;
        _timer.Start();
        return true;
    }

    private static double Accumulate(double pending, double value) =>
        value != 0 && Math.Sign(pending) != Math.Sign(value) ? value : pending + value;

    private void OnTick(object? sender, EventArgs e)
    {
        if (_viewport == null) return;
        if (!_viewport.CanNavigateViewport) { Cancel(); return; }
        long now = Environment.TickCount64;
        double blend = 1 - Math.Exp(-Math.Clamp(now - _lastFrame, 1, 64) / 25.0);
        _lastFrame = now;
        Vector step = _pan.Length < 0.05 ? _pan : _pan * blend;
        Vector applied = _viewport.PanViewport(step);
        _pan -= step;
        if (Math.Abs(applied.X - step.X) > 0.01) _pan = _pan.WithX(0);
        if (Math.Abs(applied.Y - step.Y) > 0.01) _pan = _pan.WithY(0);
        Vector zoomStep = _zoom.Length < 0.0001 ? _zoom : _zoom * blend;
        _zoom -= zoomStep;
        if (zoomStep != default) _viewport.ZoomViewport(Math.Exp(zoomStep.X), Math.Exp(zoomStep.Y), _anchor);
        if ((_ended || now - _lastInput >= 140) && _pan == default && _zoom == default) Complete(false);
    }

    private void FlushZoom()
    {
        _viewport?.ZoomViewport(Math.Exp(_zoom.X), Math.Exp(_zoom.Y), _anchor);
        _zoom = default;
    }

    public void Cancel() => Complete(true);

    private void Complete(bool interrupted)
    {
        _timer.Stop();
        IEditorViewport? viewport = _viewport;
        _viewport = null;
        _pan = _zoom = default;
        bool zoomed = _zoomed;
        _zoomed = false;
        viewport?.EndViewportInput(zoomed, interrupted);
    }

    public void Dispose()
    {
        Cancel();
        _timer.Tick -= OnTick;
    }
}
