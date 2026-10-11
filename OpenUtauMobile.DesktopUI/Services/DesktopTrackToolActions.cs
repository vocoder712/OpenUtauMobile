using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.DesktopUI.Services
{
    internal static class DesktopTrackToolActions
    {
        public static DesktopTool[] GetCompatibleTools(UTrack track, DesktopToolKind kind)
        {
            string[] compatible = kind == DesktopToolKind.Resampler
                ? Renderers.GetSupportedResamplers(track.RendererSettings.Wavtool).Select(tool => tool.ToString()!).ToArray()
                : Renderers.GetSupportedWavtools(track.RendererSettings.Resampler).Select(tool => tool.ToString()!).ToArray();
            return DesktopToolsService.GetTools(kind).Where(tool => tool.Available && compatible.Contains(tool.Name)).ToArray();
        }

        public static bool TryApply(UTrack track, DesktopTool tool)
        {
            DocManager manager = DocManager.Inst;
            if (!tool.Available || manager.HasOpenUndoGroup || !manager.Project.tracks.Contains(track)
                || !GetCompatibleTools(track, tool.Kind).Any(candidate => candidate.Name == tool.Name)) return false;

            URenderSettings settings = track.RendererSettings.Clone();
            if (tool.Kind == DesktopToolKind.Resampler) settings.resampler = tool.Name;
            else settings.wavtool = tool.Name;
            manager.StartUndoGroup();
            try { manager.ExecuteCmd(new TrackChangeRenderSettingCommand(manager.Project, track, settings)); }
            finally { manager.EndUndoGroup(); }
            return true;
        }
    }
}
