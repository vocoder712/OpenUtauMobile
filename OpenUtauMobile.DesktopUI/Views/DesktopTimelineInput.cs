using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtauMobile.Controls.Gestures;
using OpenUtauMobile.Services.Editor;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopTimelineInput : IDisposable
    {
        private readonly Control _ruler, _canvas;
        private readonly Func<double, int> _toTick;
        private readonly EditorInputController _input;
        private readonly IEditorViewport _viewport;
        private readonly DispatcherTimer _edgeTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private IPointer? _pointer;
        private double _pressX, _lastX;
        private int _pressTick;
        private long _lastFrame;

        public DesktopTimelineInput(Control ruler, Control canvas, Func<double, int> toTick, EditorInputController input, IEditorViewport viewport)
        {
            _ruler = ruler; _canvas = canvas; _toTick = toTick; _input = input; _viewport = viewport;
            ruler.PointerPressed += Press; ruler.PointerMoved += Move;
            ruler.PointerReleased += Release; ruler.PointerCaptureLost += Lost;
            ruler.DetachedFromVisualTree += Detached;
            _edgeTimer.Tick += ScrollEdge;
        }
        private void Seek()
        {
            if (!DocManager.Inst.HasOpenUndoGroup)
            {
                // 跟随定位和边缘滚动分别处理，保持按下位置与鼠标移动的对应关系。
                int tick = _pressTick + _toTick(Math.Clamp(_lastX, 0, _canvas.Bounds.Width)) - _toTick(_pressX);
                DocManager.Inst.ExecuteCmd(new SeekPlayPosTickNotification(Math.Max(0, tick)));
            }
        }
        private void Press(object? sender, PointerPressedEventArgs e)
        {
            if (e.Pointer.Type != PointerType.Mouse || e.GetCurrentPoint(_ruler).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed || DocManager.Inst.HasOpenUndoGroup) return;
            _input.CancelEditorInput();
            _viewport.BeginViewportInput();
            _pressX = _lastX = e.GetPosition(_canvas).X;
            _pressTick = _toTick(_pressX);
            _pointer = e.Pointer; _pointer.Capture(_ruler);
            _lastFrame = Stopwatch.GetTimestamp(); _edgeTimer.Start();
            Seek(); e.Handled = true;
        }
        private void Move(object? sender, PointerEventArgs e)
        {
            if (_pointer != e.Pointer || !e.GetCurrentPoint(_ruler).Properties.IsLeftButtonPressed) return;
            _lastX = e.GetPosition(_canvas).X;
            Seek(); e.Handled = true;
        }
        private void ScrollEdge(object? sender, EventArgs e)
        {
            double elapsed = Math.Min(.1, Stopwatch.GetElapsedTime(_lastFrame).TotalSeconds);
            _lastFrame = Stopwatch.GetTimestamp();
            if (_pointer == null || DocManager.Inst.HasOpenUndoGroup || _canvas.Bounds.Width <= 0) return;
            const double edge = 24;
            double direction = _lastX < edge ? -Math.Clamp((edge - _lastX) / edge, 0, 2)
                : _lastX > _canvas.Bounds.Width - edge ? Math.Clamp((_lastX - _canvas.Bounds.Width + edge) / edge, 0, 2) : 0;
            if (direction == 0) return;
            int before = _toTick(0);
            _viewport.PanViewport(new Vector(-direction * 640 * elapsed, 0));
            _pressTick += _toTick(0) - before;
            Seek();
        }
        private void Release(object? sender, PointerReleasedEventArgs e)
        {
            if (_pointer != e.Pointer) return;
            _lastX = e.GetPosition(_canvas).X; Seek(); e.Handled = true;
            Cancel();
        }
        private void Lost(object? sender, PointerCaptureLostEventArgs e) => Cancel();
        private void Detached(object? sender, VisualTreeAttachmentEventArgs e) => Cancel();
        public void Cancel()
        {
            _edgeTimer.Stop();
            IPointer? pointer = _pointer; _pointer = null;
            if (pointer == null) return;
            pointer.Capture(null); _viewport.EndViewportInput(false, false);
        }
        public void Dispose()
        {
            Cancel(); _edgeTimer.Tick -= ScrollEdge;
            _ruler.PointerPressed -= Press; _ruler.PointerMoved -= Move;
            _ruler.PointerReleased -= Release; _ruler.PointerCaptureLost -= Lost;
            _ruler.DetachedFromVisualTree -= Detached;
        }
    }
}
