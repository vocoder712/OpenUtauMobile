using DynamicData.Binding;
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtauMobile.Resources.Strings;
using OpenUtauMobile.Utils;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System.Diagnostics;
using System.Globalization;
using Preferences = OpenUtau.Core.Util.Preferences;

namespace OpenUtauMobile.ViewModels
{
    public partial class SettingsViewModel : ReactiveObject
    {
        [Reactive] public bool AutoScroll { get; set; } = Preferences.Default.PlaybackAutoScroll == 2;
        [Reactive] public int PlaybackRefreshRate { get; set; } = Preferences.Default.PlaybackRefreshRate;
        [Reactive] public ObservableCollectionExtended<KeyValuePair<float, string>> PitchDisplayPrecision { get; set; } = [
            new KeyValuePair<float, string>(1f, AppResources.PitchPrecisionOriginal),
            new KeyValuePair<float, string>(2f, AppResources.PitchPrecisionFine),
            new KeyValuePair<float, string>(3f, AppResources.PitchPrecisionMedium),
            new KeyValuePair<float, string>(5f, AppResources.PitchPrecisionRough),
        ];
        [Reactive] public KeyValuePair<float, string> SelectedPitchDisplayPrecision { get; set; }
        [Reactive] public bool ShowPortrait { get; set; } = Preferences.Default.ShowPortrait;
        [Reactive] public bool CustomPortraitOptions { get; set; } = Preferences.Default.CustomPortraitOptions;
        [Reactive] public double PortraitOpacity { get; set; } = Preferences.Default.PortraitOpacity;
        [Reactive] public bool KeepScreenOn { get; set; } = Preferences.Default.KeepScreenOn;
        [Reactive] public ObservableCollectionExtended<KeyValuePair<int, string>> PianoSampleTypes { get; set; } = [
            new KeyValuePair<int, string>(0, AppResources.OnPianoClickSilence),
            new KeyValuePair<int, string>(1, AppResources.OnPianoClickSineWave),
            new KeyValuePair<int, string>(2, AppResources.OnPianoClickPianoSample),
        ];
        [Reactive] public KeyValuePair<int, string> SelectedPianoSample { get; set; }
        [Reactive] public ObservableCollectionExtended<AudioOutputDevice> AudioOutputDevices { get; set; } = [.. PlaybackManager.Inst.AudioOutput.GetOutputDevices()];
        [Reactive] public AudioOutputDevice SelectedAudioOutputDevice { get; set; } = new AudioOutputDevice();
        [Reactive] public bool PreRender { get; set; } = Preferences.Default.PreRender;
        [Reactive] public bool SkipRenderingMutedTracks { get; set; } = Preferences.Default.SkipRenderingMutedTracks;
        [Reactive] public List<KeyValuePair<int, string>> Themes { get; set; } = [
            new KeyValuePair<int, string>(0, AppResources.ThemeLight),
            new KeyValuePair<int, string>(1, AppResources.ThemeDark),
            new KeyValuePair<int, string>(2, AppResources.System),
        ];
        [Reactive] public KeyValuePair<int, string> SelectedTheme { get; set; }
        public List<LanguageOption> LanguageOptions { get; set; } = ViewConstants.LanguageOptions;
        public LanguageOption SelectedLanguageOption { get; set; }
        /// <summary>
        /// DiffSinger 声学模型推理步数
        /// </summary>
        [Reactive] public int DiffSingerSteps { get; set; } = Preferences.Default.DiffSingerSteps;
        /// <summary>
        /// DiffSinger 唱法模型推理步数
        /// </summary>
        [Reactive] public int DiffSingerStepsVariance { get; set; } = Preferences.Default.DiffSingerStepsVariance;
        /// <summary>
        /// DiffSinger 音高模型推理步数
        /// </summary>
        [Reactive] public int DiffSingerStepsPitch { get; set; } = Preferences.Default.DiffSingerStepsPitch;
        public SettingsViewModel()
        {
            SelectedPitchDisplayPrecision = PitchDisplayPrecision.FirstOrDefault(p => p.Key == Preferences.Default.PitchDisplayPrecision);
            SelectedPianoSample = PianoSampleTypes.FirstOrDefault(p => p.Key == Preferences.Default.PianoSample);
            SelectedTheme = Themes.FirstOrDefault(t => t.Key == Preferences.Default.Theme);
            SelectedLanguageOption = LanguageOptions.FirstOrDefault(l => l.CultureName == Preferences.Default.Language) ?? LanguageOptions[0];
        }

        public void Save()
        {
            Preferences.Default.PlaybackAutoScroll = AutoScroll ? 2 : 0;
            Preferences.Default.PlaybackRefreshRate = PlaybackRefreshRate;
            Preferences.Default.PitchDisplayPrecision = SelectedPitchDisplayPrecision.Key;
            Preferences.Default.ShowPortrait = ShowPortrait;
            Preferences.Default.CustomPortraitOptions = CustomPortraitOptions;
            Preferences.Default.PortraitOpacity = PortraitOpacity;
            Preferences.Default.KeepScreenOn = KeepScreenOn;
            Preferences.Default.PianoSample = SelectedPianoSample.Key;
            Preferences.Default.PreRender = PreRender;
            Preferences.Default.SkipRenderingMutedTracks = SkipRenderingMutedTracks;
            Preferences.Default.Theme = SelectedTheme.Key;
            Preferences.Default.Language = SelectedLanguageOption.CultureName;

            // 应用语言
            string lang = SelectedLanguageOption.CultureName;
            if (string.IsNullOrEmpty(lang))
            {
                lang = CultureInfo.CurrentCulture.TwoLetterISOLanguageName; // 获取系统语言，例如 "en"
            }
            CultureInfo culture = new(lang);
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            AppResources.Culture = culture;

            Preferences.Default.DiffSingerSteps = DiffSingerSteps;
            Preferences.Default.DiffSingerStepsVariance = DiffSingerStepsVariance;
            Preferences.Default.DiffSingerStepsPitch = DiffSingerStepsPitch;
            Preferences.Save();
        }
    }
}
