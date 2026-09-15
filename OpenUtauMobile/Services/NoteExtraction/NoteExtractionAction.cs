using System;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;
using Serilog;

namespace OpenUtauMobile.Services.NoteExtraction;

internal sealed class NoteExtractionAction
{
    private bool running;

    public async Task RunAsync(UWavePart source)
    {
        if (running) return;
        running = true;
        UProject project = DocManager.Inst.Project;
        try
        {
            NoteExtractionRequest? request = await PopupService.Show<NoteExtractionRequest>(
                new NoteExtractionPopup(), new NoteExtractionPopupViewModel());
            if (request == null || !ReferenceEquals(project, DocManager.Inst.Project) || !project.parts.Contains(source)) return;
            UVoicePart? result = null;
            await LoadingPopupService.RunAsync(L.S("NoteExtraction.Running"), 0,
                async (loading, cancellation) =>
                {
                    loading.SetIndeterminate();
                    // 加载任务完成后再读取 PCM；操作期间弹窗阻止编辑源分片。
                    if (source.Peaks != null) await source.Peaks.WaitAsync(cancellation);
                    cancellation.ThrowIfCancellationRequested();
                    result = await Task.Run(() =>
                    {
                        UVoicePart extracted;
                        using (var backend = GameBackendProvider.Create(request.Backend))
                        {
                            Log.Information("Note extraction: requested and active backend = {Backend}", backend.Name);
                            extracted = NoteExtractionService.Extract(project, source, backend, cancellation,
                                (current, total) => loading.UpdateProgress(current, total), request.Settings);
                        }
                        // 释放 GAME 模型后再加载 RMVPE，避免两套模型同时占用移动设备内存。
                        if (request.Settings.ExtractPitch)
                        {
                            loading.UpdateProgress(0d, L.S("NoteExtraction.ExtractingPitch"));
                            NoteExtractionService.ExtractPitch(project, source, extracted, cancellation,
                                (current, total) => loading.UpdateProgress(current, total));
                        }
                        return extracted;
                    }, cancellation);
                    cancellation.ThrowIfCancellationRequested();
                });

            if (!ReferenceEquals(project, DocManager.Inst.Project) || !project.parts.Contains(source)) return;
            if (result == null || result.notes.Count == 0)
            {
                ToastService.Enqueue(L.S("NoteExtraction.Empty"));
                return;
            }
            result.trackNo = project.tracks.Count;
            DocManager.Inst.StartUndoGroup();
            try
            {
                DocManager.Inst.ExecuteCmd(new AddTrackCommand(project, new UTrack(project) { TrackNo = result.trackNo }));
                DocManager.Inst.ExecuteCmd(new AddPartCommand(project, result));
            }
            finally
            {
                DocManager.Inst.EndUndoGroup();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "Note extraction failed");
            ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(ex)));
        }
        finally
        {
            running = false;
        }
    }
}
