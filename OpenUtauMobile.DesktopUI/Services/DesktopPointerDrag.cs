using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using OpenUtauMobile.Services.Platform;
using Serilog;

namespace OpenUtauMobile.DesktopUI.Services
{
    internal sealed class DesktopPointerDrag : IDesktopPointerDrag
    {
        private readonly Control _control;
        private readonly Cursor? _cursor;
        private readonly Func<Point>? _get;
        private readonly Action<Point>? _warp;
        private readonly Action? _release;
        private readonly Point _anchor;
        private readonly double _scaling;
        private Point _previous;
        private bool _disposed;
        private bool _nativeAvailable;
        private bool _hasPosition;

        public static IDesktopPointerDrag Create(Control control)
        {
            try { return new DesktopPointerDrag(control, true); }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
            {
                Log.Debug(ex, "当前显示服务器不支持光标复位，使用捕获拖动");
                return new DesktopPointerDrag(control, false);
            }
        }

        private DesktopPointerDrag(Control control, bool native)
        {
            _control = control;
            _scaling = TopLevel.GetTopLevel(control)?.RenderScaling ?? 1;
            if (native && OperatingSystem.IsWindows())
            {
                _get = () => GetCursorPos(out NativePoint p) ? new Point(p.X, p.Y) : throw new InvalidOperationException("GetCursorPos failed");
                _warp = p => { if (!SetCursorPos((int)p.X, (int)p.Y)) throw new InvalidOperationException("SetCursorPos failed"); };
            }
            else if (native && OperatingSystem.IsMacOS())
            {
                _scaling = 1;
                _get = () =>
                {
                    IntPtr evt = CGEventCreate(IntPtr.Zero);
                    if (evt == IntPtr.Zero) throw new InvalidOperationException("CGEventCreate failed");
                    try { NativeDoublePoint p = CGEventGetLocation(evt); return new Point(p.X, p.Y); }
                    finally { CFRelease(evt); }
                };
                _warp = p => { if (CGWarpMouseCursorPosition(new NativeDoublePoint { X = p.X, Y = p.Y }) != 0) throw new InvalidOperationException("CGWarpMouseCursorPosition failed"); };
            }
            else if (native && OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            {
                IntPtr display = XOpenDisplay(IntPtr.Zero);
                if (display == IntPtr.Zero) throw new InvalidOperationException("XOpenDisplay failed");
                IntPtr root = XDefaultRootWindow(display);
                _get = () => XQueryPointer(display, root, out _, out _, out int x, out int y, out _, out _, out _) != 0
                    ? new Point(x, y) : throw new InvalidOperationException("XQueryPointer failed");
                _warp = p => { XWarpPointer(display, IntPtr.Zero, root, 0, 0, 0, 0, (int)p.X, (int)p.Y); XFlush(display); };
                _release = () => XCloseDisplay(display);
            }
            try { _anchor = _get?.Invoke() ?? default; }
            catch { _release?.Invoke(); throw; }
            _nativeAvailable = _get != null && _warp != null;
            _cursor = control.Cursor;
            control.SetCurrentValue(InputElement.CursorProperty, new Cursor(StandardCursorType.None));
        }

        public Vector Move(Point position)
        {
            if (_disposed) return default;
            Vector captured = _hasPosition ? position - _previous : default;
            _previous = position;
            _hasPosition = true;
            if (_nativeAvailable)
            {
                try
                {
                    // 读取实时坐标，忽略复位产生的陈旧移动事件，避免重复累计。
                    Point current = _get!();
                    Vector delta = (current - _anchor) / _scaling;
                    if (delta != default) _warp!(_anchor);
                    return delta;
                }
                catch (InvalidOperationException ex)
                {
                    Log.Debug(ex, "光标复位失败，继续使用捕获拖动");
                    _nativeAvailable = false;
                }
            }
            return captured;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { if (_nativeAvailable) _warp?.Invoke(_anchor); }
            catch (InvalidOperationException ex) { Log.Debug(ex, "恢复光标位置失败"); }
            finally
            {
                _release?.Invoke();
                _control.SetCurrentValue(InputElement.CursorProperty, _cursor);
            }
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativeDoublePoint { public double X; public double Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
        [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")] private static extern IntPtr CGEventCreate(IntPtr source);
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")] private static extern NativeDoublePoint CGEventGetLocation(IntPtr evt);
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")] private static extern int CGWarpMouseCursorPosition(NativeDoublePoint point);
        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] private static extern void CFRelease(IntPtr obj);
        [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
        [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport("libX11.so.6")] private static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int winX, out int winY, out uint mask);
        [DllImport("libX11.so.6")] private static extern int XWarpPointer(IntPtr display, IntPtr source, IntPtr destination, int sourceX, int sourceY, uint sourceWidth, uint sourceHeight, int destinationX, int destinationY);
        [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
    }
}
