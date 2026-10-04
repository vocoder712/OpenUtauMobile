using System;
using Avalonia;
using OpenUtau.Core;

namespace OpenUtauMobile.ViewModels;

public partial class PianoRollViewModel
{
    private bool _viewportInputActive;
    /// <summary>
    /// 右键临时进入橡皮擦
    /// </summary>
    public bool IsTemporaryPitchErase { get; private set; }
    public bool IsEffectivePitchErase => IsPitchEraserMode || IsTemporaryPitchErase;

    public bool BeginTemporaryPitchErase(Point point)
    {
        if (EditMode != PianoRollEditMode.PitchPen || EditingVoicePart == null || !CanNavigateViewport || DocManager.Inst.HasOpenUndoGroup) return false;
        IsTemporaryPitchErase = true;
        OnGestureDragBegin(point);
        OnGestureDragUpdate(point, default, default, point, 0);
        return true;
    }

    public void EndTemporaryPitchErase(bool cancel = false)
    {
        if (!IsTemporaryPitchErase) return;
        if (cancel && DocManager.Inst.HasOpenUndoGroup)
        {
            DocManager.Inst.RollBackUndoGroup();
            _lastPitch = null;
            _inputState = PianoRollInputState.Idle;
            ResetPitchDrawPointerState();
            RequestMagnifierClose?.Invoke();
        }
        else OnGestureDragEnd(default, 0);
        IsTemporaryPitchErase = false;
        RequestInvalidateVisual?.Invoke();
    }
    /// <summary>
    /// 是否可以平移视口
    /// </summary>
    public bool CanNavigateViewport => !IsPresentationSuspended && _inputState is PianoRollInputState.Idle or PianoRollInputState.Panning;
    public bool SupportsVerticalZoom => true;

    public void BeginViewportInput()
    {
        EndPitchStroke();
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
        // 桌面固定在鼠标处，移动端横向仍固定在播放标记处。
        double anchorX = UseDesktopMouseInput ? anchor.X : PlayMarkerScreenX;
        double tick = TickOffset + anchorX / TickWidth;
        double key = KeyOffset + anchor.Y / KeyHeight;
        TickWidth = Math.Clamp(TickWidth * scaleX, ViewConstants.PianoRollTickWidthMin, ViewConstants.PianoRollTickWidthMax);
        KeyHeight = Math.Clamp(KeyHeight * scaleY, ViewConstants.NoteHeightMin, ViewConstants.NoteHeightMax);
        TickOffset = tick - anchorX / TickWidth;
        KeyOffset = key - anchor.Y / KeyHeight;
        InvalidateMaxOffsets();
        ApplyViewportLimits();
        RequestInvalidateVisual?.Invoke();
    }

    /// <summary>
    /// 结束视口输入
    /// </summary>
    /// <param name="hadZoomInput"></param>
    /// <param name="interrupted"></param>
    public void EndViewportInput(bool hadZoomInput, bool interrupted)
    {
        _viewportInputActive = false;
        if (!interrupted) SyncPlayPosFromViewportCenter();
    }
}
