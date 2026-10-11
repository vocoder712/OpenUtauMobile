using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OpenUtauMobile.DesktopUI.Views
{
    /// <summary>跟踪修饰键，并请求工作区刷新当前编辑提示。</summary>
    internal sealed class DesktopShortcutContext : IDisposable
    {
        private readonly Control _root;
        private readonly DesktopStatusBar _status;
        private readonly Action _fallback;
        private readonly TopLevel? _window;
        private readonly HashSet<Key> _held = [];

        public DesktopShortcutContext(Control root, DesktopStatusBar status, Action fallback)
        {
            _root = root;
            _status = status;
            _fallback = fallback;
            _window = TopLevel.GetTopLevel(root);
            root.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, true);
            root.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, true);
            if (_window is Window window) window.Deactivated += OnDeactivated;
        }

        public void Refresh() => _fallback();

        private static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;
        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (IsModifier(e.Key) && _held.Add(e.Key)) UpdateModifiers();
        }
        private void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (_held.Remove(e.Key)) UpdateModifiers();
            _fallback();
        }
        private void UpdateModifiers()
        {
            KeyModifiers modifiers = KeyModifiers.None;
            if (_held.Contains(Key.LeftCtrl) || _held.Contains(Key.RightCtrl)) modifiers |= KeyModifiers.Control;
            if (_held.Contains(Key.LeftShift) || _held.Contains(Key.RightShift)) modifiers |= KeyModifiers.Shift;
            if (_held.Contains(Key.LeftAlt) || _held.Contains(Key.RightAlt)) modifiers |= KeyModifiers.Alt;
            if (_held.Contains(Key.LWin) || _held.Contains(Key.RWin)) modifiers |= KeyModifiers.Meta;
            _status.SetModifiers(modifiers);
        }
        private void OnDeactivated(object? sender, EventArgs e)
        {
            _held.Clear();
            UpdateModifiers();
        }
        public void Dispose()
        {
            _root.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            _root.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
            if (_window is Window window) window.Deactivated -= OnDeactivated;
            _held.Clear();
            UpdateModifiers();
        }
    }
}
