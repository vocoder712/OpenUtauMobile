using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DialogHostAvalonia;
using OpenUtau.Core;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Controls.Gestures;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Views;

public partial class EditorView
{
    private readonly ViewportInputSession _viewportInput = new();
    private readonly HashSet<Key> _pressedKeys = [];
    private readonly HashSet<IPointer> _editorPointers = [];
    private TopLevel? _inputRoot;
    private IDisposable? _platformInput;
    private PianoRollViewModel? _inputPiano;
    private bool _pianoInputActive;

    private static bool IsModalOpen => DialogHost.IsDialogOpen("MainDialogHost");

    private void InitializeEditorInput()
    {
        Focusable = true;
        AddHandler(PointerWheelChangedEvent, OnEditorWheel, RoutingStrategies.Bubble);
        AddHandler(PointerTouchPadGestureMagnifyEvent, OnEditorMagnify, RoutingStrategies.Bubble);
        AddHandler(PointerPressedEvent, OnEditorPress, RoutingStrategies.Tunnel, true);
        AddHandler(KeyDownEvent, OnEditorKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => AttachEditorInput();
        DetachedFromVisualTree += (_, _) => DetachEditorInput();
        DataContextChanged += (_, _) => { if (_inputRoot != null) AttachEditorInput(); };
    }

    private void AttachEditorInput()
    {
        DetachEditorInput();
        _inputRoot = TopLevel.GetTopLevel(this);
        if (_inputRoot == null) return;
        _platformInput = ServiceHub.ViewportInputPlatform?.Attach(_inputRoot, DispatchViewportInput);
        _inputRoot.AddHandler(KeyUpEvent, OnEditorKeyUp, RoutingStrategies.Tunnel, true);
        _inputRoot.AddHandler(PointerReleasedEvent, OnEditorRelease, RoutingStrategies.Bubble, true);
        _inputRoot.AddHandler(PointerCaptureLostEvent, OnEditorCaptureLost, RoutingStrategies.Bubble, true);
        if (_inputRoot is Window window) window.Deactivated += OnInputDeactivated;
        if (DataContext is EditorViewModel vm)
        {
            _inputPiano = vm.PianoRollViewModel;
            _inputPiano.PropertyChanged += OnInputPianoPropertyChanged;
        }
        PopupService.Opening += CancelEditorInput;
    }

    private void DetachEditorInput()
    {
        CancelEditorInput();
        _pressedKeys.Clear();
        PopupService.Opening -= CancelEditorInput;
        _platformInput?.Dispose();
        _platformInput = null;
        if (_inputPiano != null) _inputPiano.PropertyChanged -= OnInputPianoPropertyChanged;
        _inputPiano = null;
        if (_inputRoot == null) return;
        _inputRoot.RemoveHandler(KeyUpEvent, OnEditorKeyUp);
        _inputRoot.RemoveHandler(PointerReleasedEvent, OnEditorRelease);
        _inputRoot.RemoveHandler(PointerCaptureLostEvent, OnEditorCaptureLost);
        if (_inputRoot is Window window) window.Deactivated -= OnInputDeactivated;
        _inputRoot = null;
    }

    private void OnInputPianoPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PianoRollViewModel.EditingVoicePart) or nameof(PianoRollViewModel.EditingWavePart) or nameof(PianoRollViewModel.EditMode))
            CancelEditorInput();
    }

    private void OnInputDeactivated(object? sender, EventArgs e)
    {
        CancelEditorInput();
        _pressedKeys.Clear();
    }

    private void CancelEditorInput()
    {
        _viewportInput.Cancel();
        foreach (IPointer pointer in _editorPointers.ToArray()) pointer.Capture(null);
        _editorPointers.Clear();
    }

    private void OnEditorKeyUp(object? sender, KeyEventArgs e)
    {
        if (_pressedKeys.Remove(e.Key)) e.Handled = true;
    }
    private void OnEditorRelease(object? sender, PointerReleasedEventArgs e)
    {
        PointerPointProperties p = e.GetCurrentPoint(this).Properties;
        if (!p.IsLeftButtonPressed && !p.IsRightButtonPressed && !p.IsMiddleButtonPressed) _editorPointers.Remove(e.Pointer);
    }
    private void OnEditorCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _editorPointers.Remove(e.Pointer);

    private void OnEditorPress(object? sender, PointerPressedEventArgs e)
    {
        _viewportInput.Cancel();
        _pianoInputActive = e.Source is Visual target && target.GetSelfAndVisualAncestors().Contains(PianoRollAreaGrid) && !IsMixerOpen;
        if (e.Source is not Visual visual || !TryGetSurface(visual, out bool piano, out _)) return;
        _editorPointers.Add(e.Pointer);
        _pianoInputActive = piano;
        Focus();
    }

    private void OnEditorWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_inputRoot == null) return;
        e.Handled = DispatchViewportInput(new ViewportInput(ViewportInputKind.Wheel,
            e.Delta, 1, e.GetPosition(_inputRoot), e.KeyModifiers));
    }

    private void OnEditorMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (_inputRoot == null) return;
        e.Handled = DispatchViewportInput(new ViewportInput(ViewportInputKind.Magnify,
            default, 1 + e.Delta.X, e.GetPosition(_inputRoot), e.KeyModifiers));
    }

    private bool DispatchViewportInput(ViewportInput input)
    {
        // 原生结束/取消属于已接收的会话，指针离开画布后仍须清理。
        if (input.Phase != ViewportInputPhase.Update)
        {
            if (!_viewportInput.IsActive) return false;
            if (input.Phase == ViewportInputPhase.Cancel) _viewportInput.Cancel();
            else _viewportInput.EndInput();
            return true;
        }
        _editorPointers.RemoveWhere(pointer => pointer.Captured == null);
        if (_inputRoot == null || !IsEffectivelyVisible || !IsEffectivelyEnabled || IsModalOpen ||
            _editorPointers.Count != 0 || DataContext is not EditorViewModel vm || vm.IsLoadingProject) return false;
        if (_inputRoot.InputHitTest(input.Position) is not Visual hit ||
            !TryGetSurface(hit, out bool piano, out ViewportAxes axes)) return false;
        Control canvas = piano ? NotesInputCanvas : PartsInputCanvas;
        Point? anchor = _inputRoot.TranslatePoint(input.Position, canvas);
        if (anchor == null) return false;
        return _viewportInput.Submit(piano ? vm.PianoRollViewModel : vm, input, anchor.Value, axes);
    }

    private bool TryGetSurface(Visual source, out bool piano, out ViewportAxes axes)
    {
        piano = false;
        axes = ViewportAxes.Both;
        if (source != this && !source.GetVisualAncestors().Contains(this)) return false;
        foreach (Visual node in source.GetSelfAndVisualAncestors())
        {
            if (node is Button or TextBox or RangeBase or ComboBox or ScrollViewer) return false;
            if (node == PianoInputRuler) { piano = true; axes = ViewportAxes.Horizontal; return !IsMixerOpen; }
            if (node is NotesCanvas or WaveCanvas) { piano = true; return !IsMixerOpen; }
            if (node is PianoKeysCanvas) { piano = true; axes = ViewportAxes.Vertical; return !IsMixerOpen; }
            if (node is ParameterCanvas or PhonemeSimpleCanvas or PhonemeAdvancedCanvas)
            { piano = true; axes = ViewportAxes.Horizontal; return !IsMixerOpen; }
            if (node is PartsCanvas) return true;
            if (node is TrackHeaderCanvas) { axes = ViewportAxes.Vertical; return true; }
            if (node is TickBackground)
            { piano = node is Control { DataContext: PianoRollViewModel }; axes = ViewportAxes.Horizontal; return !piano || !IsMixerOpen; }
        }
        return false;
    }

    private async void OnEditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsModalOpen || DataContext is not EditorViewModel vm || vm.IsLoadingProject) return;
        bool saveModifier = e.KeyModifiers is KeyModifiers.Control or (KeyModifiers.Control | KeyModifiers.Shift) ||
            OperatingSystem.IsMacOS() && e.KeyModifiers is KeyModifiers.Meta or (KeyModifiers.Meta | KeyModifiers.Shift);
        if (e.Key == Key.S && saveModifier)
        {
            e.Handled = true;
            if (!_pressedKeys.Add(e.Key)) return;
            await vm.SaveFromInputAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            return;
        }
        if (e.Source is not Visual source ||
            source.GetSelfAndVisualAncestors().Any(v => v is TextBox or ComboBox or RangeBase)) return;
        bool commandModifier = e.KeyModifiers == KeyModifiers.Control ||
            OperatingSystem.IsMacOS() && e.KeyModifiers == KeyModifiers.Meta;
        if (commandModifier)
        {
            if (e.Key is Key.C or Key.V or Key.X && !IsMixerOpen)
            {
                e.Handled = true;
                if (!_pressedKeys.Add(e.Key) || _editorPointers.Count != 0 || DocManager.Inst.HasOpenUndoGroup) return;
                if (_pianoInputActive)
                {
                    if (!vm.PianoRollViewModel.CanNavigateViewport || vm.PianoRollViewModel.EditingVoicePart == null) return;
                }
                else if (!vm.CanNavigateViewport) return;
                Action action = (_pianoInputActive, e.Key) switch
                {
                    (true, Key.C) => vm.PianoRollViewModel.CopySelectedNotes,
                    (true, Key.X) => vm.PianoRollViewModel.CutSelectedNotes,
                    (true, _) => vm.PianoRollViewModel.PasteNotes,
                    (false, Key.C) => vm.CopySelectedParts,
                    (false, Key.X) => vm.CutSelectedParts,
                    _ => vm.PasteParts
                };
                action();
            }
            else if (e.Key is Key.Z or Key.Y)
            {
                e.Handled = true;
                if (!_pressedKeys.Add(e.Key) || _editorPointers.Count != 0) return;
                ICommand command = e.Key == Key.Z ? vm.UndoCommand : vm.RedoCommand;
                if (command.CanExecute(null)) command.Execute(null);
            }
            else if (e.Key == Key.A && _pianoInputActive && !IsMixerOpen && _editorPointers.Count == 0 &&
                     vm.PianoRollViewModel.CanNavigateViewport && vm.PianoRollViewModel.EditingVoicePart != null)
            {
                e.Handled = true;
                if (_pressedKeys.Add(e.Key)) vm.PianoRollViewModel.SelectAllNotes();
            }
            return;
        }
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            if (_pressedKeys.Add(e.Key) && ((ICommand)vm.PlayPauseCommand).CanExecute(null))
                ((ICommand)vm.PlayPauseCommand).Execute(null);
        }
        else if (e.Key == Key.Delete && _pianoInputActive && !IsMixerOpen && _editorPointers.Count == 0 &&
                 vm.PianoRollViewModel.CanNavigateViewport && vm.PianoRollViewModel.SelectedNotes.Count > 0)
        {
            e.Handled = true;
            if (_pressedKeys.Add(e.Key)) vm.PianoRollViewModel.DeleteSelectedNotes();
        }
    }
}
