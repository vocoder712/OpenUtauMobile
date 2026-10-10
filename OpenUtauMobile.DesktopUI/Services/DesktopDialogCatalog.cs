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
        private static readonly Func<PopupDialogControl, DesktopDialogProfile> StandardProfile = CreateStandardProfile;
        private static readonly IReadOnlyDictionary<Type, Func<PopupDialogControl, DesktopDialogProfile>> Profiles =
            new Dictionary<Type, Func<PopupDialogControl, DesktopDialogProfile>>
            {
                [typeof(ProjectInfoEditPopup)] = StandardProfile,
                [typeof(EditorMorePopup)] = StandardProfile,
                [typeof(ImportTracksPopup)] = StandardProfile,
                [typeof(ErrorDialogPopup)] = StandardProfile,
                [typeof(LyricEditPopup)] = StandardProfile,
                [typeof(PhonemeEditPopup)] = StandardProfile,
                [typeof(NotePropertiesPopup)] = StandardProfile,
                [typeof(ExitEditorConfirmPopup)] = StandardProfile,
                [typeof(ExportAudioPopup)] = StandardProfile,
                [typeof(OptionConfirmPopup)] = StandardProfile,
                [typeof(LoadingPopup)] = StandardProfile,
                [typeof(NoteExtractionPopup)] = StandardProfile,
                [typeof(BatchEditPopup)] = StandardProfile,
                [typeof(BulkLyricEditPopup)] = StandardProfile,
                [typeof(ExpressionsPopup)] = StandardProfile,
                [typeof(DesktopBatchEditCommandPopup)] = StandardProfile,
                [typeof(ThemeColorPickerDialog)] = StandardProfile,
                [typeof(TrackColorPickerPopup)] = StandardProfile,
                [typeof(SingerPickerPopup)] = StandardProfile,
                [typeof(PhonemizerPickerPopup)] = StandardProfile,
                [typeof(RendererPickerPopup)] = StandardProfile,
                [typeof(TextInputPopup)] = StandardProfile,
                [typeof(VoiceColorMappingPopup)] = StandardProfile,
                [typeof(SetupWizardPopup)] = _ => new DesktopDialogProfile(800, 560, 720, true, PreferredHeight: 640),
                [typeof(ProjectTemplatesPopup)] = StandardProfile,
                [typeof(FilePickerPopup)] = StandardProfile,
            };

        public static bool IsRegistered(Type type) => Profiles.ContainsKey(type);

        public static DesktopDialogProfile For(PopupDialogControl popup, Type viewType) =>
            Profiles.TryGetValue(viewType, out Func<PopupDialogControl, DesktopDialogProfile>? profile)
                ? profile(popup)
                : DesktopDialogProfile.Standard;

        private static DesktopDialogProfile CreateStandardProfile(PopupDialogControl popup)
        {
            double width = popup.DialogWidthPreset switch
            {
                PopupDialogWidthPreset.Compact => 360,
                PopupDialogWidthPreset.Wide => 800,
                _ => 560
            };
            bool scrolls = popup.Classes.Contains("DialogHeightExpanded") || popup.Classes.Contains("DialogHeightList") ||
                popup is ImportTracksPopup or FilePickerPopup or NoteExtractionPopup or VoiceColorMappingPopup or ProjectTemplatesPopup;
            return new DesktopDialogProfile(width, Math.Min(width, 360), scrolls ? 560 : 640, scrolls, popup is NotePropertiesPopup);
        }
    }
}
