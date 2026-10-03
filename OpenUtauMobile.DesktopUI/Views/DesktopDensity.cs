using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal static class DesktopDensity
    {
        public static void Apply(StyledElement root)
        {
            root.Classes.Add("DesktopRoot");
            if (root is TemplatedControl control) control.FontSize = 13;
            root.Resources["TextControlThemeMinHeight"] = 32d;
            root.Resources["TextControlThemePadding"] = new Thickness(6, 4);
            root.Resources["ComboBoxMinHeight"] = 32d;
            root.Resources["CheckBoxMinHeight"] = 28d;
            root.Resources["ButtonSpinnerButtonMinWidth"] = 24d;
            root.Resources["OpumSliderMinimumSize"] = 28d;
            root.Resources["OpumSliderHandleWidth"] = 4d;
            root.Resources["OpumSliderHandleLength"] = 18d;
            root.Resources["OpumSliderActiveHandleWidth"] = 6d;
            root.Resources["OpumSliderFocusWidth"] = 28d;
            root.Resources["OpumSliderFocusHeight"] = 28d;
            root.Resources["OpumSliderTrackHeight"] = 6d;
            root.Resources["OpumSliderHandleGap"] = 4d;
            root.Resources["DesktopTabHeight"] = 32d;
            root.Resources["DesktopTabPadding"] = new Thickness(8, 4);
            root.Resources["SettingsNavItemMinHeight"] = 48d;
        }

        public static void ApplyMixer(Control root, double detailPaneWidth)
        {
            Apply(root);
            root.Classes.Add("DesktopMixer");
            root.Resources["MixerTrackWidth"] = 112d;
            root.Resources["MixerMasterWidth"] = 80d;
            root.Resources["MixerStateButtonSize"] = 28d;
            root.Resources["MixerDetailPaneWidth"] = detailPaneWidth;
            root.Resources["ButtonSpinnerButtonMinWidth"] = 14d;
            root.Resources["MixerReadoutEditorVisible"] = true;
            root.Resources["MixerReadoutTextVisible"] = false;
            root.Resources["MixerReadoutUnitVisible"] = true;
            root.Resources["OpumSliderMinimumSize"] = 24d;
            root.Resources["OpumSliderHandleWidth"] = 4d;
            root.Resources["OpumSliderHandleLength"] = 18d;
            root.Resources["OpumSliderActiveHandleWidth"] = 6d;
            root.Resources["OpumSliderFocusWidth"] = 24d;
            root.Resources["OpumSliderFocusHeight"] = 24d;
            root.Resources["OpumSliderTrackHeight"] = 4d;
        }

        public static void ApplyDenseDialogFields(Control root)
        {
            root.Resources["NotePropertiesControlMinHeight"] = 28d;
            root.Resources["NotePropertiesChoiceMinHeight"] = 28d;
        }

        public static void ApplyPopupGeometry(Control root)
        {
            foreach (Button button in root.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("MixerPresetOption")))
            {
                button.MinHeight = 36;
                button.Padding = new Thickness(12, 8);
                button.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left;
            }
        }
    }
}
