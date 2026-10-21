using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Pipeline;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Storage;
using Serilog;

namespace OpenUtauMobile.ViewModels;

public partial class EditorViewModel
{
    private bool replacingAudio;

    /// <summary>选择并完整加载替代音频，验证后一次提交，取消或失败不修改原片段。</summary>
    private async Task ReplaceSelectedAudioAsync()
    {
        if (replacingAudio || _disposed || SelectedParts is not [UWavePart source]) return;
        replacingAudio = true;
        UProject project = DocManager.Inst.Project;
        DocRevision revision = DocManager.Inst.Revision;
        try
        {
            string file = await FilePicker.PickSingleFileAsync(L.S("Editor.Action.ReplaceAudio"),
                ["*.mp3", "*.wav", "*.flac", "*.aac", "*.m4a", "*.ogg", "*.aiff", "*.aif", "*.aifc"]);
            if (string.IsNullOrEmpty(file)) return;
            if (_disposed) return;
            EnsureAudioReplacementCurrent(project, source, revision);

            // 独立时间轴避免后台读取时与工程编辑共享可变的速度、拍号数据。
            UProject timing = new()
            {
                tempos = project.tempos.Select(tempo => new UTempo(tempo.position, tempo.bpm)).ToList(),
                timeSignatures = project.timeSignatures.Select(signature => new UTimeSignature(
                    signature.barPosition, signature.beatPerBar, signature.beatUnit)).ToList()
            };
            timing.timeAxis.BuildSegments(timing);
            UWavePart replacement = new()
            {
                FilePath = file,
                position = source.position,
                trackNo = source.trackNo
            };
            await LoadingPopupService.RunAsync(L.S("AudioReplacement.Loading"), _ => Task.Run(async () =>
            {
                replacement.Load(timing);
                if (await replacement.Peaks == null || replacement.Samples == null)
                    throw new InvalidDataException(string.Format(L.S("AudioReplacement.Unreadable"), file));
            }));

            if (_disposed) return;
            EnsureAudioReplacementCurrent(project, source, revision);
            PlaybackManager.Inst.StopPlayback();
            DocManager.Inst.StartUndoGroup("Editor.Action.ReplaceAudio");
            try
            {
                DocManager.Inst.ExecuteCmd(new ReplacePartCommand(project, source, replacement));
                ReplaceSelectedParts([replacement]);
            }
            finally
            {
                DocManager.Inst.EndUndoGroup();
            }
            ToastService.Enqueue(L.S("AudioReplacement.Success"));
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to replace audio part");
            if (!_disposed)
                ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(exception)));
        }
        finally
        {
            replacingAudio = false;
        }
    }

    /// <summary>文件选择和加载期间工程发生变化时，拒绝向过期片段提交替换。</summary>
    private static void EnsureAudioReplacementCurrent(UProject project, UWavePart source, DocRevision revision)
    {
        if (!ReferenceEquals(DocManager.Inst.Project, project) || !project.parts.Contains(source) ||
            DocManager.Inst.Revision != revision || DocManager.Inst.HasOpenUndoGroup)
            throw new InvalidOperationException(L.S("AudioReplacement.ProjectChanged"));
    }
}
