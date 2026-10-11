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
            DesktopTool[] registered = DesktopToolsService.GetRegisteredTools(kind).ToArray();
            return compatible.Select(name => registered.First(tool => tool.Name == name)).ToArray();
        }

        public static bool TryApply(UTrack track, DesktopTool tool)
        {
            DocManager manager = DocManager.Inst;
            if (!tool.Available || manager.HasOpenUndoGroup || !manager.Project.tracks.Contains(track)
                || !GetCompatibleTools(track, tool.Kind).Any(candidate => candidate.Name == tool.Name)) return false;

            // 下拉框初始化也会触发选择事件；相同工具不得生成命令并再次重建检查器。
            string? current = tool.Kind == DesktopToolKind.Resampler ? track.RendererSettings.resampler : track.RendererSettings.wavtool;
            if (current == tool.Name) return false;

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
