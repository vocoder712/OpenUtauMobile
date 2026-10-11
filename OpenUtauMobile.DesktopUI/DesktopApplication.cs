using OpenUtau.Core.Util;
using OpenUtauMobile.Services;

namespace OpenUtauMobile.DesktopUI
{
    public static class DesktopApplication
    {
        /// <summary>启动时按桌面模式偏好注册窗口、输入和文件操作能力。</summary>
        public static void Register()
        {
            if (!Preferences.Default.DesktopModeEnabled)
            {
                ServiceHub.DesktopWindowFactory = null;
                return;
            }

            ServiceHub.UseDesktopFileWorkflows = true;
            ServiceHub.DesktopPointerDragFactory = Services.DesktopPointerDrag.Create;
            ServiceHub.DesktopSingerPicker = Views.DesktopTrackPickers.PickSingerAsync;
            ServiceHub.DesktopRendererPicker = Views.DesktopTrackPickers.PickRendererAsync;
            ServiceHub.DesktopTrackNamePicker = Views.DesktopTrackPickers.PickTrackNameAsync;
            ServiceHub.DesktopPhonemizerPicker = Views.DesktopTrackPickers.PickPhonemizerAsync;
            ServiceHub.DesktopPopupSizeProvider = (preset, available) => new Avalonia.Size(
                System.Math.Min(available.Width, preset switch
                {
                    OpenUtauMobile.Controls.PopupDialogWidthPreset.Compact => 360,
                    OpenUtauMobile.Controls.PopupDialogWidthPreset.Wide => 800,
                    _ => 560
                }), System.Math.Min(available.Height, 600));
            ServiceHub.BeforeDesktopProjectOpenAsync = Services.DesktopToolsService.Instance.WaitForIdleAsync;
            ServiceHub.DesktopWindowFactory = main => new DesktopWindow(main);
        }
    }
}
