using System;
using System.Collections.Generic;
using OpenUtauMobile.Controls;

namespace OpenUtauMobile.DesktopUI.Services
{
    internal readonly record struct DesktopDialogProfile(double Width, double MinWidth, double MaxHeight, bool Scrolls, bool DenseFields = false, double? PreferredHeight = null)
    {
        public static DesktopDialogProfile Standard => new(560, 400, 640, false);
    }

    internal static class DesktopDialogCatalog
    {
        private static readonly HashSet<Type> Registered =
        [
            typeof(ProjectInfoEditPopup), typeof(EditorMorePopup), typeof(ImportTracksPopup),
            typeof(ErrorDialogPopup), typeof(LyricEditPopup), typeof(PhonemeEditPopup),
            typeof(NotePropertiesPopup), typeof(ExitEditorConfirmPopup), typeof(ExportAudioPopup),
            typeof(OptionConfirmPopup), typeof(LoadingPopup), typeof(NoteExtractionPopup),
            typeof(BatchEditPopup), typeof(BulkLyricEditPopup), typeof(ExpressionsPopup),
            typeof(DesktopBatchEditCommandPopup),
            typeof(ThemeColorPickerDialog), typeof(TrackColorPickerPopup), typeof(SingerPickerPopup),
            typeof(PhonemizerPickerPopup), typeof(RendererPickerPopup), typeof(TextInputPopup),
            typeof(MixerPresetPopup), typeof(VoiceColorMappingPopup), typeof(SetupWizardPopup),
            typeof(ProjectTemplatesPopup), typeof(FilePickerPopup),
        ];

        public static bool TryGet(Type type, out DesktopDialogProfile profile)
        {
            if (!Registered.Contains(type))
            {
                profile = default;
                return false;
            }
            profile = DesktopDialogProfile.Standard;
            return true;
        }

        public static DesktopDialogProfile For(PopupDialogControl popup, Type viewType)
        {
            if (!TryGet(viewType, out _)) return DesktopDialogProfile.Standard;
            // 向导的后续页面比欢迎页高，不能按首屏内容把整个窗口压缩。
            if (popup is SetupWizardPopup) return new(800, 560, 720, true, PreferredHeight: 640);
            double width = popup.DialogWidthPreset switch
            {
                PopupDialogWidthPreset.Compact => 360,
                PopupDialogWidthPreset.Wide => 800,
                _ => 560
            };
            bool scrolls = popup.Classes.Contains("DialogHeightExpanded") || popup.Classes.Contains("DialogHeightList") ||
                popup is ImportTracksPopup or FilePickerPopup or NoteExtractionPopup or VoiceColorMappingPopup or SetupWizardPopup or ProjectTemplatesPopup;
            if (popup is MixerPresetPopup) width = 360;
            return new DesktopDialogProfile(width, Math.Min(width, 360), scrolls ? 560 : 640, scrolls, popup is NotePropertiesPopup);
        }
    }
}
