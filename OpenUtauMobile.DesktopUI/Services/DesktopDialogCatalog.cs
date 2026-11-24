using System;
using System.Collections.Generic;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Themes.OpenUtauMobile.Tokens.Components;

namespace OpenUtauMobile.DesktopUI.Services
{
    internal readonly record struct DesktopDialogProfile(double Width, double MinWidth, double MaxHeight, bool DenseFields = false)
    {
        public static DesktopDialogProfile Standard => new(560, 400, DialogTokens.RegularMaxHeight);
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
                [typeof(TrackSettingsPopup)] = StandardProfile,
                [typeof(ExpressionGraphSettingsPopup)] = StandardProfile,
                [typeof(ExpressionGraphRendererPickerPopup)] = StandardProfile,
                [typeof(GraphItemPickerPopup)] = StandardProfile,
                [typeof(TextInputPopup)] = StandardProfile,
                [typeof(VoiceColorMappingPopup)] = StandardProfile,
                [typeof(SetupWizardPopup)] = popup => CreateStandardProfile(popup) with { Width = 800, MinWidth = 560 },
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
            bool usesListOrExpandedHeight = popup.Classes.Contains("DialogHeightExpanded") || popup.Classes.Contains("DialogHeightList");
            double maxHeight = popup.UsesAdaptiveHeight ? double.PositiveInfinity
                : popup.Classes.Contains("DialogHeightCompact") ? DialogTokens.CompactMaxHeight
                : usesListOrExpandedHeight ? 560 : DialogTokens.RegularMaxHeight;
            return new DesktopDialogProfile(width, Math.Min(width, 360), maxHeight, popup is NotePropertiesPopup);
        }
    }
}
