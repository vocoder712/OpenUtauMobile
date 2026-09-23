using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.Core;
using OpenUtauMobile.Controls;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Views;

public partial class EditorView
{
    private enum EditArea { Tracks, PianoRoll, Mixer }
    private EditArea _activeEditArea;
    private readonly HashSet<Key> _pressedKeys = [];
    private readonly Dictionary<Popup, Control?> _inputPopups = [];
    private IDisposable? _popupSubscription;
    private bool _focusRestoreQueued;
    private bool HasInputPopup => _inputPopups.Count != 0;

    private bool IsEditorInputActive => _inputRoot != null && TopLevel.GetTopLevel(this) == _inputRoot &&
        IsEffectivelyVisible && IsEffectivelyEnabled && !IsModalOpen && !HasInputPopup &&
        DataContext is EditorViewModel { IsLoadingProject: false };

    private static bool IsWithin(Visual? source, Visual parent) =>
        source != null && source.GetSelfAndVisualAncestors().Contains(parent);

    private void AttachKeyboardInput()
    {
        if (_inputRoot == null) return;
        _inputRoot.AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel, true);
        _inputRoot.AddHandler(KeyUpEvent, OnEditorKeyUp, RoutingStrategies.Tunnel, true);
        _inputRoot.AddHandler(GotFocusEvent, OnEditorGotFocus, RoutingStrategies.Bubble, true);
        _inputRoot.AddHandler(LostFocusEvent, OnEditorLostFocus, RoutingStrategies.Bubble, true);
        _popupSubscription = Popup.IsOpenProperty.Changed.Subscribe(change =>
        {
            if (change.Sender is Popup popup) TrackInputPopup(popup);
        });
        foreach (Popup popup in _inputRoot.GetVisualDescendants().OfType<Popup>()) TrackInputPopup(popup);
        QueueEditorFocusRepair();
    }

    private void DetachKeyboardInput()
    {
        _pressedKeys.Clear();
        _popupSubscription?.Dispose();
        _popupSubscription = null;
        foreach (Control? child in _inputPopups.Values) UnsubscribePopupKeys(child);
        _inputPopups.Clear();
        if (_inputRoot == null) return;
        _inputRoot.RemoveHandler(KeyDownEvent, OnEditorKeyDown);
        _inputRoot.RemoveHandler(KeyUpEvent, OnEditorKeyUp);
        _inputRoot.RemoveHandler(GotFocusEvent, OnEditorGotFocus);
        _inputRoot.RemoveHandler(LostFocusEvent, OnEditorLostFocus);
    }

    private void TrackInputPopup(Popup popup)
    {
        if (!popup.IsOpen)
        {
            if (_inputPopups.Remove(popup, out Control? child))
            {
                UnsubscribePopupKeys(child);
                QueueEditorFocusRepair();
            }
            return;
        }
        // 原生弹出窗口不一定处于主窗口视觉树中，按其放置目标归属判断。
        if (!popup.IsLightDismissEnabled || TopLevel.GetTopLevel(popup.PlacementTarget ?? popup) != _inputRoot ||
            _inputPopups.ContainsKey(popup)) return;
        _inputPopups.Add(popup, popup.Child);
        _viewportInput.Cancel();
        popup.Child?.AddHandler(KeyDownEvent, OnPopupKeyDown, RoutingStrategies.Tunnel, true);
        popup.Child?.AddHandler(KeyUpEvent, OnEditorKeyUp, RoutingStrategies.Tunnel, true);
    }

    private void UnsubscribePopupKeys(Control? child)
    {
        child?.RemoveHandler(KeyDownEvent, OnPopupKeyDown);
        child?.RemoveHandler(KeyUpEvent, OnEditorKeyUp);
    }

    private void OnPopupKeyDown(object? sender, KeyEventArgs e)
    {
        // 弹出层只延续已消费按键的去重；新按键完全交给弹出层。
        if (_pressedKeys.Contains(e.Key)) e.Handled = true;
    }

    private void OnEditorKeyUp(object? sender, KeyEventArgs e)
    {
        if (_pressedKeys.Remove(e.Key)) e.Handled = true;
    }

    private void OnEditorGotFocus(object? sender, FocusChangedEventArgs e) => UpdateActiveEditArea(e.Source as Visual);

    private void OnEditorLostFocus(object? sender, RoutedEventArgs e)
    {
        if (IsWithin(e.Source as Visual, this)) QueueEditorFocusRepair();
    }

    private void QueueEditorFocusRepair()
    {
        if (_focusRestoreQueued) return;
        _focusRestoreQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _focusRestoreQueued = false;
            if (!IsEditorInputActive || _inputRoot is Window { IsActive: false }) return;
            // 保留有效控件的焦点，仅修复隐藏、移除控件后留下的空焦点。
            if (_inputRoot!.FocusManager.GetFocusedElement() is InputElement focused &&
                focused.IsAttachedToVisualTree() && focused.IsEffectivelyVisible && focused.IsEffectivelyEnabled) return;
            Focus();
        }, DispatcherPriority.Loaded);
    }

    private void UpdateActiveEditArea(Visual? source)
    {
        if (source == null || !IsWithin(source, this)) return;
        if (IsMixerOpen && _mixerPanel != null && IsWithin(source, _mixerPanel)) _activeEditArea = EditArea.Mixer;
        else if (!IsMixerOpen && IsWithin(source, PianoRollAreaGrid)) _activeEditArea = EditArea.PianoRoll;
        else if (source.GetSelfAndVisualAncestors().Any(node => node is TrackHeader or TrackHeaderCanvas) ||
                 TryGetSurface(source, out bool piano, out _) && !piano) _activeEditArea = EditArea.Tracks;
        // 页面工具栏只改变控件焦点，不改变音符／分片的编辑目标。
    }

    /// <summary>
    /// 编辑界面键盘按下事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_pressedKeys.Contains(e.Key)) { e.Handled = true; return; }
        if (e.Handled || !IsEditorInputActive || DataContext is not EditorViewModel vm) return;
        Visual? focused = _inputRoot!.FocusManager.GetFocusedElement() as Visual;
        if (focused != null && focused != _inputRoot && focused.IsAttachedToVisualTree() &&
            !IsWithin(focused, this) && !IsWithin(this, focused)) return;
        Visual? source = focused ?? e.Source as Visual;
        if (DialogKeyboard.IsComposing(source)) return;
        // 保存
        bool saveModifier = e.KeyModifiers is KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift) ||
            OperatingSystem.IsMacOS() && e.KeyModifiers is KeyModifiers.Meta or (KeyModifiers.Meta | KeyModifiers.Shift);
        if (e.Key == Key.S && saveModifier)
        {
            e.Handled = true;
            if (!_pressedKeys.Add(e.Key)) return;
            await vm.SaveFromInputAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            return;
        }
        if (source?.GetSelfAndVisualAncestors().Any(v => v is TextBox) == true) return;
        bool pianoInputActive = _activeEditArea == EditArea.PianoRoll && !IsMixerOpen;
        bool commandModifier = e.KeyModifiers == KeyModifiers.Control ||
            OperatingSystem.IsMacOS() && e.KeyModifiers == KeyModifiers.Meta;
        if (commandModifier)
        {
            // 复制、剪切、粘贴
            if (e.Key is Key.C or Key.V or Key.X && _activeEditArea != EditArea.Mixer)
            {
                e.Handled = true;
                if (!_pressedKeys.Add(e.Key) || _editorPointers.Count != 0 || DocManager.Inst.HasOpenUndoGroup) return;
                if (pianoInputActive)
                {
                    if (!vm.PianoRollViewModel.CanNavigateViewport || vm.PianoRollViewModel.EditingVoicePart == null) return;
                }
                else if (!vm.CanNavigateViewport) return;
                Action action = (pianoInputActive, e.Key) switch
                {
                    (true, Key.C) => vm.PianoRollViewModel.CopySelectedNotes, // 复制音符
                    (true, Key.X) => vm.PianoRollViewModel.CutSelectedNotes, // 剪切音符
                    (true, Key.V) => vm.PianoRollViewModel.PasteNotes, // 粘贴音符
                    (false, Key.C) => vm.CopySelectedParts, // 复制分片
                    (false, Key.X) => vm.CutSelectedParts, // 剪切分片
                    (false, Key.V) => vm.PasteParts, // 粘贴分片
                    _ => () => { } // 无效组合
                };
                action();
            }
            // 撤销、重做
            else if (e.Key is Key.Z or Key.Y)
            {
                e.Handled = true;
                if (!_pressedKeys.Add(e.Key) || _editorPointers.Count != 0) return;
                ICommand command = e.Key == Key.Z ? vm.UndoCommand : vm.RedoCommand;
                if (command.CanExecute(null)) command.Execute(null);
            }
            // 全选音符
            else if (e.Key == Key.A && pianoInputActive && _editorPointers.Count == 0 &&
                     vm.PianoRollViewModel.CanNavigateViewport && vm.PianoRollViewModel.EditingVoicePart != null)
            {
                e.Handled = true;
                if (_pressedKeys.Add(e.Key)) vm.PianoRollViewModel.SelectAllNotes();
            }
            return;
        }
        // 跳过其它组合键，避免与系统快捷键冲突。
        if (e.KeyModifiers != KeyModifiers.None) return;
        // 空格播放／暂停
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            if (_pressedKeys.Add(e.Key) && ((ICommand)vm.PlayPauseCommand).CanExecute(null))
                ((ICommand)vm.PlayPauseCommand).Execute(null);
        }
        // 删除音符
        else if (e.Key == Key.Delete && pianoInputActive && _editorPointers.Count == 0 &&
                 vm.PianoRollViewModel.CanNavigateViewport && vm.PianoRollViewModel.SelectedNotes.Count > 0)
        {
            e.Handled = true;
            if (_pressedKeys.Add(e.Key)) vm.PianoRollViewModel.DeleteSelectedNotes();
        }
    }
}
