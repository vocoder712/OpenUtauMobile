using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OpenUtauMobile.Controls;

/// <summary>恢复弹窗前的焦点，并按窗口保持 Enter 状态，避免长按提交下一层。</summary>
internal static class DialogKeyboard
{
    private static readonly ConditionalWeakTable<TopLevel, State> States = new();
    private static readonly ConditionalWeakTable<DialogShell, State> Owners = new();

    public static IDisposable PreserveFocus(TopLevel? root)
    {
        if (root == null) return Disposable.Empty;
        State state = States.GetValue(root, top => new State(top));
        return state.PreserveFocus();
    }

    public static void Attach(DialogShell shell)
    {
        if (TopLevel.GetTopLevel(shell) is not { } root) return;
        State state = States.GetValue(root, top => new State(top));
        Owners.Remove(shell);
        Owners.Add(shell, state);
        state.PreviousFocus[shell] = root.FocusManager.GetFocusedElement() as InputElement;
        state.Shells.Add(shell);
        Dispatcher.UIThread.Post(() =>
        {
            if (state.Shells.LastOrDefault() == shell && shell.IsAttachedToVisualTree() &&
                !Within(root.FocusManager.GetFocusedElement() as Visual, shell)) shell.Focus();
        }, DispatcherPriority.Loaded);
    }

    public static void Detach(DialogShell shell)
    {
        if (Owners.TryGetValue(shell, out State? state)) state.RestoreFocus(shell);
        Owners.Remove(shell);
    }

    internal static bool IsComposing(Visual? source) => source?.GetSelfAndVisualAncestors().OfType<TextBox>()
        .Any(box => box.GetVisualDescendants().OfType<TextPresenter>().Any(p => !string.IsNullOrEmpty(p.PreeditText))) == true;

    private static bool Within(Visual? source, Visual parent) => source != null &&
        (source == parent || source.GetVisualAncestors().Contains(parent));

    private sealed class State
    {
        public List<DialogShell> Shells { get; } = [];
        public Dictionary<DialogShell, InputElement?> PreviousFocus { get; } = [];
        private readonly TopLevel _root;
        private readonly List<InputElement[]> _focusScopes = [];
        private bool _enterDown;
        private bool _compositionEnter;
        private DialogShell? _keyShell;

        public State(TopLevel root)
        {
            _root = root;
            root.AddHandler(InputElement.KeyDownEvent, Preview, RoutingStrategies.Tunnel, true);
            root.AddHandler(InputElement.KeyDownEvent, Complete, RoutingStrategies.Bubble);
            root.AddHandler(InputElement.KeyUpEvent, Release, RoutingStrategies.Tunnel, true);
            if (root is Window window) window.Deactivated += (_, _) => _enterDown = false;
        }

        public void RestoreFocus(DialogShell shell)
        {
            PreviousFocus.Remove(shell, out InputElement? previous);
            Shells.Remove(shell);
            ScheduleRestore(previous == null ? [] : [previous], shell);
        }

        public IDisposable PreserveFocus()
        {
            IEnumerable<InputElement> current = (_root.FocusManager.GetFocusedElement() as Visual)?
                .GetSelfAndVisualAncestors().TakeWhile(element => element != _root).OfType<InputElement>()
                .Where(element => element.Focusable) ?? [];
            // 连续弹窗可能在上一层归还焦点前打开，保留原主体作为后备目标。
            InputElement[] previous = current.Concat(_focusScopes.LastOrDefault() ?? []).Distinct().ToArray();
            _focusScopes.Add(previous);
            return Disposable.Create(() => ScheduleRestore(previous, releaseScope: true));
        }

        private void ScheduleRestore(InputElement[] previous, DialogShell? closing = null, bool releaseScope = false)
        {
            // 等宿主移除遮罩并恢复主体可用性后再归还焦点。
            Dispatcher.UIThread.Post(() =>
            {
                if (releaseScope) _focusScopes.Remove(previous);
                DialogShell? active = Shells.LastOrDefault(s => s.IsEffectivelyVisible);
                if (_root.FocusManager.GetFocusedElement() is Visual current && (closing == null || !Within(current, closing)) &&
                    (active == null || Within(current, active))) return;
                foreach (InputElement element in previous)
                {
                    if (TopLevel.GetTopLevel(element) == _root && element is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } &&
                        (active == null || Within(element, active)) && element.Focus()) return;
                }
                active?.Focus();
            }, DispatcherPriority.Loaded);
        }

        private void Preview(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
            _keyShell = Shells.LastOrDefault(s => s.IsEffectivelyVisible);
            if (_keyShell == null && !_enterDown) return;
            Visual? source = e.Source as Visual;
            // 多行输入的重复 Enter 仍由文本框处理。
            if (source?.GetSelfAndVisualAncestors().OfType<TextBox>().Any(t => t.AcceptsReturn) == true) return;
            if (_enterDown) { e.Handled = true; return; }
            _enterDown = true;
            _compositionEnter = IsComposing(source);
        }

        private void Complete(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None || _compositionEnter ||
                _keyShell == null || !Shells.Contains(_keyShell) || !_keyShell.IsEffectivelyVisible) return;
            Visual? source = e.Source as Visual;
            if (source?.GetSelfAndVisualAncestors().Any(v => v is ComboBox or ListBox || v is TextBox { AcceptsReturn: true }) == true) return;
            Button[] defaults = _keyShell.GetVisualDescendants().OfType<Button>()
                .Where(b => DialogShell.GetIsDefaultAction(b) && b.IsEffectivelyVisible).ToArray();
            // 错误配置不猜测按钮优先级；不可执行也不能退回其他操作。
            if (defaults.Length != 1 || !defaults[0].IsEffectivelyEnabled) return;
            Button button = defaults[0];
            if (button.Command != null && !button.Command.CanExecute(button.CommandParameter)) return;
            e.Handled = true;
            new ButtonAutomationPeer(button).Invoke();
        }

        private void Release(object? sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            _enterDown = false;
            _compositionEnter = false;
            _keyShell = null;
        }
    }
}
