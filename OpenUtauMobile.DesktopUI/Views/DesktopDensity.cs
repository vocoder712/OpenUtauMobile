using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal static class DesktopDensity
    {
        public static void Apply(StyledElement root)
        {
            ApplyProfile(root, mixer: false);
        }

        private static void ApplyProfile(StyledElement root, bool mixer)
        {
            root.Classes.Add("DesktopRoot");
            if (root is TemplatedControl control) control.FontSize = 13;
            root.Resources["TextControlThemeMinHeight"] = 32d;
            root.Resources["TextControlThemePadding"] = new Thickness(6, 4);
            root.Resources["ComboBoxMinHeight"] = 32d;
            root.Resources["CheckBoxMinHeight"] = 28d;
            root.Resources["ButtonSpinnerButtonMinWidth"] = mixer ? 14d : 24d;
            root.Resources["OpumSliderMinimumSize"] = mixer ? 24d : 28d;
            root.Resources["OpumSliderHandleWidth"] = 4d;
            root.Resources["OpumSliderHandleLength"] = 18d;
            root.Resources["OpumSliderActiveHandleWidth"] = 6d;
            root.Resources["OpumSliderFocusWidth"] = mixer ? 24d : 28d;
            root.Resources["OpumSliderFocusHeight"] = mixer ? 24d : 28d;
            root.Resources["OpumSliderTrackHeight"] = mixer ? 4d : 6d;
            root.Resources["OpumSliderHandleGap"] = 4d;
            root.Resources["DesktopTabHeight"] = 32d;
            root.Resources["DesktopTabPadding"] = new Thickness(8, 4);
            root.Resources["SettingsNavItemMinHeight"] = 48d;
        }

        public static void ApplyMixer(Control root, double detailPaneWidth)
        {
            ApplyProfile(root, mixer: true);
            root.Classes.Add("DesktopMixer");
            root.Resources["MixerTrackWidth"] = 112d;
            root.Resources["MixerMasterWidth"] = 80d;
            root.Resources["MixerStateButtonSize"] = 28d;
            root.Resources["MixerDetailPaneWidth"] = detailPaneWidth;
            root.Resources["MixerReadoutEditorVisible"] = true;
            root.Resources["MixerReadoutTextVisible"] = false;
            root.Resources["MixerReadoutUnitVisible"] = true;
        }

        public static void ApplyInspector(Control root)
        {
            root.Resources["TextControlThemeMinHeight"] = 28d;
            root.Resources["TextControlThemePadding"] = new Thickness(6, 3);
            root.Resources["ButtonSpinnerButtonMinWidth"] = 24d;
            root.Resources["ExpanderMinHeight"] = 30d;
            root.Resources["ExpanderChevronButtonSize"] = 24d;
            root.Resources["ExpanderChevronMargin"] = new Thickness(4, 0, 4, 0);
            root.Resources["ExpanderHeaderPadding"] = new Thickness(8, 0, 0, 0);
            root.Resources["ExpanderContentPadding"] = new Thickness(8);
        }

        public static void ApplyDenseDialogFields(Control root)
        {
            root.Resources["NotePropertiesControlMinHeight"] = 28d;
            root.Resources["NotePropertiesChoiceMinHeight"] = 28d;
        }

    }
}
