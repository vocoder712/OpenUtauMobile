using System;
using Avalonia;

namespace OpenUtauMobile.ViewModels;

public partial class PianoRollViewModel
{
    private bool _viewportInputActive;
    public bool IsTemporaryPitchErase { get; private set; }
    public bool IsEffectivePitchErase => IsPitchEraserMode || IsTemporaryPitchErase;

    public bool BeginTemporaryPitchErase(Point point)
    {
        if (EditMode != PianoRollEditMode.PitchPen || EditingVoicePart == null || !CanNavigateViewport) return false;
        IsTemporaryPitchErase = true;
        OnGestureDragBegin(point);
        OnGestureDragUpdate(point, default, default, point, 0);
        return true;
    }

    public void EndTemporaryPitchErase()
    {
        if (!IsTemporaryPitchErase) return;
        OnGestureDragEnd(default, 0);
        IsTemporaryPitchErase = false;
    }
    public bool CanNavigateViewport => !IsPresentationSuspended && _inputState is PianoRollInputState.Idle or PianoRollInputState.Panning;
    public bool SupportsVerticalZoom => true;

    public void BeginViewportInput()
    {
        _panMotion.Cancel();
        _inputState = PianoRollInputState.Idle;
        _viewportInputActive = true;
    }

    public Vector PanViewport(Vector pixels)
    {
        double x = TickOffset;
        double y = KeyOffset;
        ApplyPanDeltaFromMotion(pixels);
        return new Vector((x - TickOffset) * TickWidth, (y - KeyOffset) * KeyHeight);
    }

    public void ZoomViewport(double scaleX, double scaleY, Point anchor)
    {
        // 保留亚 tick 精度，缩放不调用会改变触屏状态的手势入口。
        double tick = TickOffset + anchor.X / TickWidth;
        double key = KeyOffset + anchor.Y / KeyHeight;
        TickWidth = Math.Clamp(TickWidth * scaleX, ViewConstants.PianoRollTickWidthMin, ViewConstants.PianoRollTickWidthMax);
        KeyHeight = Math.Clamp(KeyHeight * scaleY, ViewConstants.NoteHeightMin, ViewConstants.NoteHeightMax);
        TickOffset = tick - anchor.X / TickWidth;
        KeyOffset = key - anchor.Y / KeyHeight;
        InvalidateMaxOffsets();
        ApplyViewportLimits();
        RequestInvalidateVisual?.Invoke();
    }

    public void EndViewportInput(bool zoomed, bool interrupted)
    {
        _viewportInputActive = false;
        if (!interrupted && (zoomed || !IsPlaying)) SyncPlayPosFromViewportCenter();
    }
}
