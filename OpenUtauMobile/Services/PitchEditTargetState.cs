using System.Runtime.CompilerServices;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services;

public enum PitchEditTarget
{
    Auto,
    Deviation,
    Override,
}

/// <summary>音高工具的会话状态；同轨分片共享，不写入工程。</summary>
internal static class PitchEditTargetState
{
    private sealed class TrackState
    {
        public string? Renderer;
        public PitchEditTarget Target;
    }

    private static readonly ConditionalWeakTable<UProject, ConditionalWeakTable<UTrack, TrackState>> Projects = new();

    private static TrackState GetState(UProject project, UTrack track)
    {
        TrackState state = Projects.GetValue(project, _ => new()).GetValue(track, _ => new());
        string? renderer = track.RendererSettings?.renderer;
        if (state.Renderer != renderer)
        {
            state.Renderer = renderer;
            state.Target = PitchEditTarget.Auto;
        }
        return state;
    }

    public static PitchEditTarget Read(UProject project, UTrack track) => GetState(project, track).Target;

    public static void Cycle(UProject project, UTrack track)
    {
        TrackState state = GetState(project, track);
        state.Target = state.Target switch
        {
            PitchEditTarget.Auto => PitchEditTarget.Deviation,
            PitchEditTarget.Deviation => PitchEditTarget.Override,
            _ => PitchEditTarget.Auto,
        };
    }
}
