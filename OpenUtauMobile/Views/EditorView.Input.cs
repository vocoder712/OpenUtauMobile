using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DialogHostAvalonia;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Controls.Gestures;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Views;

public partial class EditorView
{
    private readonly ViewportInputSession _viewportInput = new();
    private readonly HashSet<IPointer> _editorPointers = [];
    private TopLevel? _inputRoot;
    private IDisposable? _platformInput;
    private PianoRollViewModel? _inputPiano;

    private static bool IsModalOpen => DialogHost.IsDialogOpen("MainDialogHost");

    private void InitializeEditorInput()
    {
        Focusable = true;
        AddHandler(PointerPressedEvent, OnEditorPress, RoutingStrategies.Tunnel, true);
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
        AttachKeyboardInput();
        _inputRoot.AddHandler(PointerWheelChangedEvent, OnEditorWheel, RoutingStrategies.Bubble);
        _inputRoot.AddHandler(PointerTouchPadGestureMagnifyEvent, OnEditorMagnify, RoutingStrategies.Bubble);
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
        DetachKeyboardInput();
        PopupService.Opening -= CancelEditorInput;
        _platformInput?.Dispose();
        _platformInput = null;
        if (_inputPiano != null) _inputPiano.PropertyChanged -= OnInputPianoPropertyChanged;
        _inputPiano = null;
        if (_inputRoot == null) return;
        _inputRoot.RemoveHandler(PointerWheelChangedEvent, OnEditorWheel);
        _inputRoot.RemoveHandler(PointerTouchPadGestureMagnifyEvent, OnEditorMagnify);
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

    private void OnEditorRelease(object? sender, PointerReleasedEventArgs e)
    {
        PointerPointProperties p = e.GetCurrentPoint(this).Properties;
        if (!p.IsLeftButtonPressed && !p.IsRightButtonPressed && !p.IsMiddleButtonPressed) _editorPointers.Remove(e.Pointer);
    }
    private void OnEditorCaptureLost(object? sender, PointerCaptureLostEventArgs e) => _editorPointers.Remove(e.Pointer);

    private void OnEditorPress(object? sender, PointerPressedEventArgs e)
    {
        _viewportInput.Cancel();
        UpdateActiveEditArea(e.Source as Visual);
        if (e.Source is not Visual visual || !TryGetSurface(visual, out bool piano, out _)) return;
        _editorPointers.Add(e.Pointer);
        _activeEditArea = piano ? EditArea.PianoRoll : EditArea.Tracks;
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
        if (_inputRoot == null || !IsEditorInputActive ||
            _editorPointers.Count != 0 || DataContext is not EditorViewModel vm) return false;
        if (_inputRoot.InputHitTest(input.Position) is not Visual hit ||
            !TryGetSurface(hit, out bool piano, out ViewportAxes axes)) return false;
        Control canvas = piano ? NotesInputCanvas : PartsInputCanvas;
        Point? anchor = _inputRoot.TranslatePoint(input.Position, canvas);
        if (anchor == null) return false;
        // 标尺和琴键的滚轮默认缩放；轴限制与平滑处理仍由共用输入层负责。
        bool zoomOnWheel = piano && hit.GetSelfAndVisualAncestors()
            .Any(node => node == PianoInputRuler || node is PianoKeysCanvas);
        return _viewportInput.Submit(piano ? vm.PianoRollViewModel : vm, input, anchor.Value, axes, zoomOnWheel);
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
}
