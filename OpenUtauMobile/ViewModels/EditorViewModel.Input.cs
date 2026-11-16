using System;
using System.Threading.Tasks;
using Avalonia;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Services.Projects;
using Serilog;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 视口变幻抽象层输入处理分部
/// </summary>
public partial class EditorViewModel
{
    private bool _viewportInputActive;
    private Task<bool>? _manualSaveTask;
    public bool CanNavigateViewport => _inputState is TrackInputState.Idle or TrackInputState.Panning;
    public bool SupportsVerticalZoom => false;

    public void BeginViewportInput()
    {
        _panMotion.Cancel();
        _inputState = TrackInputState.Idle;
        _playbackFollow.Reset();
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

    /// <summary>重复请求等待同一保存事务，失败不会放行保存并退出。</summary>
    public Task<bool> SaveFromInputAsync(bool saveAs) =>
        RunManualSaveAsync(() => saveAs ? RequestSaveAs() : SaveCore());

    /// <summary>核查编辑器所拥有的文档会话，避免文件选择后保存了另一个工程。</summary>
    private bool OwnsSaveDocument() => !_disposed && _projectLoaded && _saveOwner != null &&
        DocManager.Inst.IsSaveCheckpointCurrent(_saveOwner);

    /// <summary>所有手动保存入口复用同一个在途任务，包括模板保存和另存为选择器。</summary>
    private Task<bool> RunManualSaveAsync(Func<Task<bool>> operation) =>
        _manualSaveTask ??= ExecuteManualSaveAsync(operation);

    /// <summary>统一处理失败通知和任务收尾，不改写可能已切换的工程状态。</summary>
    private async Task<bool> ExecuteManualSaveAsync(Func<Task<bool>> operation)
    {
        // 先返回任务，保证同步取消/失败也会清理已登记的在途任务。
        await Task.Yield();
        try
        {
            return await operation();
        }
        catch (Exception exception)
        {
            Log.Error(exception, "保存工程失败");
            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(exception));
            return false;
        }
        finally { _manualSaveTask = null; }
    }

    /// <summary>复用现有模态加载窗口显示阶段，整个手动保存期间禁用编辑器输入。</summary>
    private async Task<bool> SaveWithProgressAsync(string path, bool template)
    {
        if (!OwnsSaveDocument()) return false;
        bool saved = false;
        IsSavingProject = true;
        try
        {
            await LoadingPopupService.RunAsync(L.S("ProjectSave.Capturing"), async loading =>
            {
                Progress<ProjectSaveStage> progress = new(stage => loading.SetIndeterminate(L.S($"ProjectSave.{stage}")));
                if (OwnsSaveDocument())
                    saved = await ProjectSaveCoordinator.Inst.SaveManualAsync(_saveOwner!, path, template, progress);
            });
            if (saved && OwnsSaveDocument()) ToastService.Enqueue(L.S("Editor.Saved"));
            return saved && OwnsSaveDocument();
        }
        finally { IsSavingProject = false; }
    }
}
