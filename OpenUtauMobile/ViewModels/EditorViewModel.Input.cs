using Avalonia;
using System;
using System.Threading.Tasks;
using OpenUtau.Core;
using Serilog;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 视口变幻抽象层输入处理分部
/// </summary>
public partial class EditorViewModel
{
    private bool _viewportInputActive;
    private bool _saveInProgress;
    public bool CanNavigateViewport => _inputState is TrackInputState.Idle or TrackInputState.Panning;
    public bool SupportsVerticalZoom => false;

    public void BeginViewportInput()
    {
        _panMotion.Cancel();
        _inputState = TrackInputState.Idle;
        _autoPageActive = false;
        _viewportInputActive = true;
    }

    public Vector PanViewport(Vector pixels)
    {
        double x = TickOffset;
        double y = TrackOffset;
        ApplyPanDeltaFromMotion(pixels);
        return new Vector((x - TickOffset) * TickWidth, y - TrackOffset);
    }

    public void ZoomViewport(double scaleX, double scaleY, Point anchor)
    {
        double tick = TickOffset + anchor.X / TickWidth;
        TickWidth = Math.Clamp(TickWidth * scaleX, ViewConstants.TickWidthMin, ViewConstants.TickWidthMax);
        TickOffset = tick - anchor.X / TickWidth;
        InvalidateMaxOffsets();
        ApplyViewportLimits();
        RequestInvalidateVisual?.Invoke();
    }

    public void EndViewportInput(bool hadZoomInput, bool interrupted) => _viewportInputActive = false;

    public async Task<bool> SaveFromInputAsync(bool saveAs)
    {
        if (_saveInProgress) return false;
        _saveInProgress = true;
        try { return saveAs ? await RequestSaveAs() : await SaveCore(); }
        catch (Exception exception)
        {
            Log.Error(exception, "保存工程失败");
            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(exception));
            return false;
        }
        finally { _saveInProgress = false; }
    }
}
