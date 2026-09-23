using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using OpenUtauMobile.Controls.Gestures;

namespace OpenUtauMobile.Windows;

/// <summary>在当前窗口内读取完整滚轮消息，保留触摸板兼容消息中的 Ctrl/Shift。</summary>
public sealed class WindowsViewportInput : IViewportInputPlatform
{
    public IDisposable Attach(TopLevel root, Func<ViewportInput, bool> dispatch) => new Connection(root, dispatch);

    private sealed class Connection : IDisposable
    {
        private readonly TopLevel _root;
        private readonly Func<ViewportInput, bool> _dispatch;

        public Connection(TopLevel root, Func<ViewportInput, bool> dispatch)
        {
            _root = root;
            _dispatch = dispatch;
            Win32Properties.AddWndProcHookCallback(root, OnMessage);
        }

        private IntPtr OnMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (handled || message is not (0x020A or 0x020E or 0x024E or 0x024F)) return IntPtr.Zero;
            long word = wParam.ToInt64();
            double amount = unchecked((short)(word >> 16)) / 120.0;
            bool horizontal = message is 0x020E or 0x024F;
            KeyModifiers modifiers = KeyModifiers.None;
            if (IsDown(0x11)) modifiers |= KeyModifiers.Control;
            if (IsDown(0x10)) modifiers |= KeyModifiers.Shift;
            if (IsDown(0x12)) modifiers |= KeyModifiers.Alt;
            if (IsDown(0x5B) || IsDown(0x5C)) modifiers |= KeyModifiers.Meta;
            // WM_POINTERWHEEL 的低字是指针编号，不是 MK 标志。
            if (message is 0x020A or 0x020E)
            {
                if ((word & 0x0008) != 0) modifiers |= KeyModifiers.Control;
                if ((word & 0x0004) != 0) modifiers |= KeyModifiers.Shift;
            }
            long coordinates = lParam.ToInt64();
            Point position = _root.PointToClient(new PixelPoint(
                unchecked((short)coordinates), unchecked((short)(coordinates >> 16))));
            ViewportInput input = new(ViewportInputKind.Wheel,
                horizontal ? new Vector(-amount, 0) : new Vector(0, amount), 1, position, modifiers);
            // 只有编辑器实际接收才阻止原路由；列表、弹窗及其他窗口照常交给框架。
            handled = _dispatch(input);
            return IntPtr.Zero;
        }

        private static bool IsDown(int key) => (GetKeyState(key) & 0x8000) != 0;
        public void Dispose() => Win32Properties.RemoveWndProcHookCallback(_root, OnMessage);
    }

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);
}
