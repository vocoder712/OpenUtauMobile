using Avalonia;
using OpenUtau.Core;
using OpenUtau.Core.ExpressionGraph;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Dialogs;

namespace OpenUtauMobile.ViewModels;

public partial class PianoRollViewModel
{
    private UVoicePart? _pitchStrokePart;
    private UProject? _pitchStrokeProject;
    private bool _pitchStrokeOverrides;
    private float _lastDrawnPitch;

    private UTrack? PitchEditingTrack
    {
        get
        {
            int index = EditingVoicePart?.trackNo ?? -1;
            UProject project = DocManager.Inst.Project;
            return index >= 0 && index < project.tracks.Count ? project.tracks[index] : null;
        }
    }

    public PitchEditTarget PitchTarget => PitchEditingTrack is { } track
        ? PitchEditTargetState.Read(DocManager.Inst.Project, track)
        : PitchEditTarget.Auto;

    public string PitchTargetKey => PitchTarget switch
    {
        PitchEditTarget.Override => OpenUtau.Core.Format.Ustx.PITO,
        PitchEditTarget.Deviation => OpenUtau.Core.Format.Ustx.PITD,
        _ => PitchEditingTrack is { } track && ExpressionGraphProgram.PrefersPitchOverride(DocManager.Inst.Project, track)
            ? OpenUtau.Core.Format.Ustx.PITO
            : OpenUtau.Core.Format.Ustx.PITD,
    };

    private string PitchTargetLabel => PitchTarget switch
    {
        PitchEditTarget.Auto => "G",
        PitchEditTarget.Deviation => "PITD",
        _ => "PITO",
    };

    private string PitchTargetDescription => PitchTarget switch
    {
        PitchEditTarget.Auto => string.Format(L.S("PianoRoll.PitchTarget.FollowGraph"), PitchTargetKey.ToUpperInvariant()),
        PitchEditTarget.Deviation => L.S("PianoRoll.PitchTarget.Deviation"),
        _ => L.S("PianoRoll.PitchTarget.Override"),
    };

    private void CyclePitchTarget()
    {
        if (EditMode != PianoRollEditMode.PitchPen || PitchEditingTrack is not { } track) return;
        EndPitchStroke();
        PitchEditTargetState.Cycle(DocManager.Inst.Project, track);
        RebuildPianoRollContextActions();
        ToastService.Enqueue(PitchTargetDescription);
        RequestInvalidateVisual?.Invoke();
    }

    private void BeginPitchStroke(Point point)
    {
        _pitchStrokePart = EditingVoicePart;
        _pitchStrokeProject = DocManager.Inst.Project;
        _pitchStrokeOverrides = PitchTargetKey == OpenUtau.Core.Format.Ustx.PITO;
        _lastPitch = null;
        _lastDrawnPitch = (float)(PointYToPitch(point.Y) * 100);
    }

    private void EndPitchStroke()
    {
        if (_pitchStrokePart == null) return;
        if (DocManager.Inst.HasOpenUndoGroup) DocManager.Inst.EndUndoGroup();
        _pitchStrokePart = null;
        _pitchStrokeProject = null;
        _lastPitch = null;
        _inputState = PianoRollInputState.Idle;
        RequestMagnifierClose?.Invoke();
        ResetPitchDrawPointerState();
    }
}
