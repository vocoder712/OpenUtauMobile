using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using OpenUtau.Audio;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using OpenUtauMobile.Audio;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Services.Performance;
using OpenUtauMobile.Storage;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime.Platform;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels;

public enum SettingsCategory
{
    EditAndBehaviour,
    RenderAndPerformance,
    FileAndStorage,
    AppearanceAndLanguage
}

/// <summary>
/// Piano key behavior options.
/// </summary>
public enum PianoKeyBehavior
{
    /// <summary>
    /// No sound when pressing piano keys.
    /// </summary>
    Silent = 0,

    /// <summary>
    /// Play sine wave tone (default, uses existing ToneGenerator).
    /// </summary>
    SineWave = 1,

    /// <summary>
    /// Play piano sample from SoundFont (SF2) file.
    /// </summary>
    SoundFont = 2
}

/// <summary>语言选项（用于绑定到语言选择器）。</summary>
public class LanguageOption
{
    public string Code { get; }
    public string DisplayName { get; }

    public LanguageOption(string code, string displayName)
    {
        Code = code;
        DisplayName = displayName;
    }
}

/// <summary>钢琴键行为选项（用于绑定到行为选择器）。</summary>
public class PianoKeyBehaviorOption
{
    public PianoKeyBehavior Value { get; }
    public string DisplayName { get; }

    public PianoKeyBehaviorOption(PianoKeyBehavior value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
}

public enum StopButtonBehavior
{
    StartTickSelectedPartZero = 1,
    StartTickZero = 2,
    AlwaysZero = 3
}

public class StopButtonBehaviorOption
{
    public StopButtonBehavior Value { get; }
    public string DisplayName { get; }

    public StopButtonBehaviorOption(StopButtonBehavior value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
}

/// <summary>回放自动滚动行为选项（用于绑定到选择器）。</summary>
public class AutoScrollBehaviorOption
{
    public int Value { get; }
    public string DisplayName { get; }

    public AutoScrollBehaviorOption(int value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
}

/// <summary>音频后端选项（用于绑定到选择器）。</summary>
public class AudioBackendOption
{
    public string Value { get; }
    public string DisplayName { get; }

    public AudioBackendOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
}

/// <summary>机器学习运行器后端选项（用于绑定到选择器）。</summary>
public class OnnxRunnerOption
{
    public string Value { get; }
    public string DisplayName { get; }

    public OnnxRunnerOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
}

/// <summary>歌词助手选项。</summary>
public class LyricsHelperOption
{
    public Type HelperType { get; }
    public string DisplayName => HelperType.Name;

    public LyricsHelperOption(Type helperType)
    {
        HelperType = helperType;
    }
}

/// <summary>钢琴键标签显示选项。</summary>
public class PianoKeyLabelModeOption
{
    public PianoKeyLabelMode Value { get; }
    public string DisplayName { get; }

    public PianoKeyLabelModeOption(PianoKeyLabelMode value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
}

public enum AndroidFullscreenMode
{
    Always = 0,
    EditorOnly = 1,
    Off = 2
}

/// <summary>Android 全屏显示选项。</summary>
public class AndroidFullscreenModeOption
{
    public AndroidFullscreenMode Value { get; }
    public string DisplayName { get; }

    public AndroidFullscreenModeOption(AndroidFullscreenMode value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
}

/// <summary>机器学习加速设备选项（用于绑定到选择器）。</summary>
public class OnnxDeviceOption
{
    public int DeviceId { get; }
    public string DisplayName { get; }

    public OnnxDeviceOption(int deviceId, string displayName)
    {
        DeviceId = deviceId;
        DisplayName = displayName;
    }
}

/// <summary>音频设备选项（用于绑定到选择器）。</summary>
public class AudioDeviceOption
{
    public AudioOutputDevice? Device { get; }
    public string DisplayName { get; }

    public AudioDeviceOption(AudioOutputDevice? device, string displayName)
    {
        Device = device;
        DisplayName = displayName;
    }
}

/// <summary>主题选项（用于绑定到主题选择器）。</summary>
public class ThemeOption
{
    public string Value { get; }
    public string DisplayName { get; }

    public ThemeOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
}

public class ThemeColorModeOption
{
    public int Value { get; }
    public string DisplayName { get; }

    public ThemeColorModeOption(int value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
}

public class ThemeSeedPresetOption
{
    public string Id { get; }
    public string DisplayName { get; }
    public string Hex { get; }
    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }

    public ThemeSeedPresetOption(string id, string displayName, string hex, Action<ThemeSeedPresetOption> onApply)
    {
        Id = id;
        DisplayName = displayName;
        Hex = hex;
        ApplyCommand = ReactiveCommand.Create(() => onApply(this));
    }
}

public class SettingsViewModel : NavigateViewModelBase, IDisposable
{
    // ── 导航命令 ───────────────────────────────────────────────────
    public ReactiveCommand<Unit, Unit> BackCommand { get; }
    public ReactiveCommand<Unit, Unit> ToggleNavCommand { get; }
    public ReactiveCommand<SettingsCategory, Unit> SelectCategoryCommand { get; }

    // ── State ────────────────────────────────────────────────────────
    /// <summary>
    /// 当前所在的设置类别，绑定到导航栏选项。初始值为 EditAndBehaviour。
    /// </summary>
    [Reactive]
    public SettingsCategory SelectedCategory { get; set; } = SettingsCategory.EditAndBehaviour;

    /// <summary>
    /// 侧栏是否展开
    /// </summary>
    [Reactive]
    public bool IsNavExpanded { get; set; } = true;

    /// <summary>
    /// NavRail 当前宽度：展开 220，收起 64。由 View 层宽度自适应或手动切换驱动。
    /// </summary>
    [Reactive]
    public double NavWidth { get; set; } = 220;

    // ── Localization ─────────────────────────────────────────────────
    /// <summary>可选语言列表，绑定到语言选择器。</summary>
    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
        new[]
        {
            new LanguageOption(LocalizationManager.FollowSystemLanguageCode,
                L.S("Settings.Appearance.Language.FollowSystem"))
        }
        .Concat(LocalizationManager.AvailableLanguages.Select(l => new LanguageOption(l.Code, l.DisplayName)))
        .ToList();

    /// <summary>当前选中的语言选项。</summary>
    [Reactive]
    public LanguageOption? SelectedLanguageOption { get; set; }

    // ── Appearance ───────────────────────────────────────────────────
    /// <summary>可选主题列表，Light / Dark / Follow System。</summary>
    public IReadOnlyList<ThemeOption> AvailableThemes { get; } = new List<ThemeOption>
    {
        new("System", L.S("Settings.Appearance.Theme.System")),
        new("Light", L.S("Settings.Appearance.Theme.Light")),
        new("Dark", L.S("Settings.Appearance.Theme.Dark")),
    };

    /// <summary>当前选中的主题选项。</summary>
    [Reactive]
    public ThemeOption? SelectedThemeOption { get; set; }

    /// <summary>主题色模式选项。</summary>
    public IReadOnlyList<ThemeColorModeOption> AvailableThemeColorModes { get; } = new List<ThemeColorModeOption>
    {
        new((int)ThemeColorMode.FollowSystem, L.S("Settings.Appearance.ThemeColor.Mode.FollowSystem")),
        new((int)ThemeColorMode.Custom, L.S("Settings.Appearance.ThemeColor.Mode.Custom")),
    };

    /// <summary>当前主题色模式。</summary>
    [Reactive]
    public ThemeColorModeOption? SelectedThemeColorMode { get; set; }

    /// <summary>预设主题色集合。</summary>
    [Reactive]
    public IReadOnlyList<ThemeSeedPresetOption> PresetSeedColors { get; private set; } = [];

    /// <summary>当前匹配到的预设主题色。</summary>
    [Reactive]
    public ThemeSeedPresetOption? SelectedThemePreset { get; set; }

    /// <summary>主题色 HEX 输入。</summary>
    [Reactive]
    public string ThemeSeedHexInput { get; set; } = "#FF0000";

    /// <summary>主题色预览画刷。</summary>
    [Reactive]
    public IBrush ThemeSeedPreviewBrush { get; set; } = new SolidColorBrush(Color.Parse("#FF0000"));

    /// <summary>主题色来源说明。</summary>
    [Reactive]
    public string ThemeColorSystemSource { get; set; } = string.Empty;

    [Reactive] public bool HasThemeColorSystemSource { get; set; }

    /// <summary>主题色提示信息。</summary>
    [Reactive]
    public string ThemeColorHint { get; set; } = string.Empty;

    [Reactive] public bool HasThemeColorHint { get; set; }

    /// <summary>主题色模式是否为自定义。</summary>
    [Reactive]
    public bool IsCustomThemeColorMode { get; set; }

    [Reactive]
    public ReactiveCommand<Unit, Unit> ApplyThemeSeedHexCommand { get; private set; } = null!;
    [Reactive]
    public ReactiveCommand<ThemeSeedPresetOption, Unit> ApplyThemePresetCommand { get; private set; } = null!;
    [Reactive]
    public ReactiveCommand<Unit, Unit> OpenThemeColorPickerCommand { get; private set; } = null!;
    [Reactive]
    public ReactiveCommand<Unit, Unit> ResetThemeSeedCommand { get; private set; } = null!;

    // ── File & Storage ────────────────────────────────────────────────
    /// <summary>额外歌手路径开关，开启时表示已设置了有效路径。</summary>
    [Reactive]
    public bool AdditionalSingerPathEnabled { get; set; }

    /// <summary>额外歌手路径，实时与 Preferences 同步。</summary>
    [Reactive]
    public string AdditionalSingerPath { get; set; } = string.Empty;

    /// <summary>重新选择额外歌手路径命令。</summary>
    [Reactive]
    public ReactiveCommand<Unit, Unit> ChangeAdditionalSingerPathCommand { get; private set; } = null!;

    /// <summary>整个应用缓存目录的大小，异步统计期间显示省略号。</summary>
    [Reactive]
    public string CacheSize { get; private set; } = "…";

    private int _cacheSizeRefreshVersion;

    /// <summary>清除应用缓存命令。</summary>
    [Reactive]
    public ReactiveCommand<Unit, Unit> ClearCacheCommand { get; private set; } = null!;

    // ── Edit & Behaviour ─────────────────────────────────────────────
    /// <summary>只有 Windows、Linux 和 macOS 展示桌面模式选项。</summary>
    public bool IsDesktopPlatform { get; } = OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

    /// <summary>下次启动时是否启用桌面界面。</summary>
    [Reactive]
    public bool DesktopModeEnabled { get; set; }

    public bool IsAndroidPlatform { get; } = OperatingSystem.IsAndroid();

    [Reactive]
    public IReadOnlyList<LyricsHelperOption> AvailableLyricsHelpers { get; private set; } = [];

    [Reactive]
    public LyricsHelperOption? SelectedLyricsHelper { get; set; }

    [Reactive]
    public bool LyricsHelperBrackets { get; set; }

    /// <summary>Android 全屏显示选项。</summary>
    public IReadOnlyList<AndroidFullscreenModeOption> AvailableAndroidFullscreenModes { get; } =
        new List<AndroidFullscreenModeOption>
        {
            new(AndroidFullscreenMode.Always, L.S("Settings.AndroidFullscreen.Always")),
            new(AndroidFullscreenMode.EditorOnly, L.S("Settings.AndroidFullscreen.EditorOnly")),
            new(AndroidFullscreenMode.Off, L.S("Settings.AndroidFullscreen.Off"))
        };

    /// <summary>当前 Android 全屏显示模式。</summary>
    [Reactive]
    public AndroidFullscreenModeOption? SelectedAndroidFullscreenMode { get; set; }

    /// <summary>Android 编辑页是否阻止屏幕休眠。</summary>
    [Reactive]
    public bool AndroidKeepScreenAwakeWhileEditing { get; set; }

    public const double PitchPenNoteHitTickExtensionSliderMinimum =
        Preferences.SerializablePreferences.PitchPenNoteHitTickExtensionMinimum;
    public const double PitchPenNoteHitTickExtensionSliderMaximum =
        Preferences.SerializablePreferences.PitchPenNoteHitTickExtensionMaximum;
    public const double PitchPenNoteHitToneExtensionSliderMinimum =
        Preferences.SerializablePreferences.PitchPenNoteHitToneExtensionMinimum;
    public const double PitchPenNoteHitToneExtensionSliderMaximum =
        Preferences.SerializablePreferences.PitchPenNoteHitToneExtensionMaximum;

    /// <summary>可选钢琴键行为列表。</summary>
    public IReadOnlyList<PianoKeyBehaviorOption> AvailablePianoKeyBehaviors { get; } = new List<PianoKeyBehaviorOption>
    {
        new(PianoKeyBehavior.Silent, L.S("Settings.PianoKey.Silent")),
        new(PianoKeyBehavior.SineWave, L.S("Settings.PianoKey.SineWave")),
        new(PianoKeyBehavior.SoundFont, L.S("Settings.PianoKey.SoundFont"))
    };

    /// <summary>当前选中的钢琴键行为选项。</summary>
    [Reactive]
    public PianoKeyBehaviorOption? SelectedPianoKeyBehavior { get; set; }

    /// <summary>可选钢琴键标签显示方式。</summary>
    public IReadOnlyList<PianoKeyLabelModeOption> AvailablePianoKeyLabelModes { get; } =
        new List<PianoKeyLabelModeOption>
        {
            new(PianoKeyLabelMode.PitchLabels, L.S("Settings.PianoKeyLabel.PitchLabels")),
            new(PianoKeyLabelMode.TonicPitchLabels, L.S("Settings.PianoKeyLabel.TonicPitchLabels")),
            new(PianoKeyLabelMode.NumberedNotation, L.S("Settings.PianoKeyLabel.NumberedNotation")),
        };

    /// <summary>当前钢琴键标签显示方式。</summary>
    [Reactive]
    public PianoKeyLabelModeOption? SelectedPianoKeyLabelMode { get; set; }

    /// <summary>当前 SoundFont 文件路径。</summary>
    [Reactive]
    public string SoundFontPath { get; set; } = string.Empty;

    /// <summary>SoundFont 是否已成功加载。</summary>
    [Reactive]
    public bool IsSoundFontLoaded { get; set; }

    /// <summary>首次检查音色期间避免用户操作在 UI 线程等待单例加载锁。</summary>
    [Reactive]
    public bool IsSoundFontInitializing { get; private set; } = true;

    /// <summary>更改 SoundFont 路径命令。</summary>
    [Reactive]
    public ReactiveCommand<Unit, Unit> ChangeSoundFontPathCommand { get; private set; } = null!;

    // ── Edit & Behaviour: AutoScroll ────────────────────────────────
    /// <summary>可选走带自动翻页行为列表。</summary>
    public IReadOnlyList<AutoScrollBehaviorOption> AvailableAutoScrollBehaviors { get; } =
        new List<AutoScrollBehaviorOption>
        {
            new(0, L.S("Settings.AutoScroll.Disabled")),
            new(1, L.S("Settings.AutoScroll.Enabled")),
            new(2, L.S("Settings.AutoScroll.Smooth"))
        };

    /// <summary>当前选中的走带自动翻页行为选项。</summary>
    [Reactive]
    public AutoScrollBehaviorOption? SelectedAutoScrollBehavior { get; set; }

    /// <summary>可选回放纵向滚动行为列表。</summary>
    public IReadOnlyList<AutoScrollBehaviorOption> AvailableVerticalScrollBehaviors { get; } =
        new List<AutoScrollBehaviorOption>
        {
            new(0, L.S("Settings.VerticalScroll.Disabled")),
            new(1, L.S("Settings.VerticalScroll.Boundary")),
            new(2, L.S("Settings.VerticalScroll.Center"))
        };

    /// <summary>当前选中的回放纵向滚动行为。</summary>
    [Reactive]
    public AutoScrollBehaviorOption? SelectedVerticalScrollBehavior { get; set; }

    // ── Edit & Behaviour: Stop Button ──────────────────────────────
    /// <summary>List of available stop button actions.</summary>
    public IReadOnlyList<StopButtonBehaviorOption> AvailableStopButtonBehaviors { get; } =
        new List<StopButtonBehaviorOption>
        {
            new(StopButtonBehavior.StartTickSelectedPartZero, L.S("Settings.StopButton.StartTickSelectedPartZero")),
            new(StopButtonBehavior.StartTickZero, L.S("Settings.StopButton.StartTickZero")),
            new(StopButtonBehavior.AlwaysZero, L.S("Settings.StopButton.AlwaysZero"))
        };

    /// <summary>The currently selected behavior option for the Stop button.</summary>
    [Reactive]
    public StopButtonBehaviorOption? SelectedStopButtonBehavior { get; set; }

    /// <summary>回放刷新率（1-60）。</summary>
    [Reactive]
    public int PlaybackRefreshRate { get; set; }

    /// <summary>是否显示立绘。</summary>
    [Reactive]
    public bool ShowPortraitEnabled { get; set; }

    /// <summary>撤销步数上限（10-100）。</summary>
    [Reactive]
    public int UndoLimit { get; set; }

    /// <summary>音高线编辑模式下是否允许拖拽画布。</summary>
    [Reactive]
    public bool PitchPenCanvasDragEnabled { get; set; }

    /// <summary>是否显示音高画笔命中区域。</summary>
    [Reactive]
    public bool ShowPitchPenHitArea { get; set; }

    /// <summary>扩展音符命中范围前后增加的 Tick 数。</summary>
    [Reactive]
    public int PitchPenNoteHitTickExtension { get; set; }

    /// <summary>扩展音符命中范围上下增加的半音数。</summary>
    [Reactive]
    public int PitchPenNoteHitToneExtension { get; set; }

    /// <summary>保留偏好中的连续倍率，独立于滑块显示的最近档位。</summary>
    [Reactive]
    public double MagnifierMagnificationFactor { get; private set; }

    /// <summary>仅供用户操作的离散档位，初始化时不回写连续偏好。</summary>
    [Reactive]
    public double MagnifierSliderValue { get; set; }

    /// <summary>自动保存间隔秒数（0=禁用，30-600）。</summary>
    [Reactive]
    public int AutoSaveInterval { get; set; }

    /// <summary>是否启用自动保存。</summary>
    [Reactive]
    public bool AutoSaveEnabled { get; set; }

    // ── Render & Performance ────────────────────────────────────────
    /// <summary>是否显示全局性能监视悬浮层。</summary>
    [Reactive]
    public bool PerformanceMonitorEnabled { get; set; }

    /// <summary>DiffSinger 推理步数（音质模型）。</summary>
    [Reactive]
    public int DiffSingerSteps { get; set; }

    /// <summary>DiffSinger 推理步数（唱法模型/Variance）。</summary>
    [Reactive]
    public int DiffSingerStepsVariance { get; set; }

    /// <summary>DiffSinger 推理步数（音高模型）。</summary>
    [Reactive]
    public int DiffSingerStepsPitch { get; set; }

    /// <summary>是否启用预渲染。</summary>
    [Reactive]
    public bool PreRenderEnabled { get; set; }

    /// <summary>预渲染线程数。</summary>
    [Reactive]
    public int NumRenderThreads { get; set; }

    /// <summary>可选机器学习运行器后端列表。</summary>
    [Reactive]
    public IReadOnlyList<OnnxRunnerOption> AvailableOnnxRunners { get; private set; } = [];

    /// <summary>当前选中的机器学习运行器后端。</summary>
    [Reactive]
    public OnnxRunnerOption? SelectedOnnxRunner { get; set; }

    /// <summary>可选机器学习加速设备列表。</summary>
    [Reactive]
    public IReadOnlyList<OnnxDeviceOption> AvailableOnnxDevices { get; private set; } = [];

    /// <summary>当前选中的机器学习加速设备。</summary>
    [Reactive]
    public OnnxDeviceOption? SelectedOnnxDevice { get; set; }

    /// <summary>当前后端是否支持选择具体加速设备。</summary>
    [Reactive]
    public bool ShowOnnxDeviceSelector { get; set; }

    // ── Audio Backend & Device ──────────────────────────────────────
    /// <summary>可选音频后端列表（根据平台动态生成）。</summary>
    [Reactive]
    public IReadOnlyList<AudioBackendOption> AvailableAudioBackends { get; private set; } = [];

    /// <summary>当前选中的音频后端选项。</summary>
    [Reactive]
    public AudioBackendOption? SelectedAudioBackend { get; set; }

    /// <summary>可用音频设备列表。</summary>
    [Reactive]
    public IReadOnlyList<AudioDeviceOption> AvailableAudioDevices { get; set; } = [];

    /// <summary>当前选中的音频设备选项。</summary>
    [Reactive]
    public AudioDeviceOption? SelectedAudioDevice { get; set; }

    /// <summary>刷新音频设备列表命令。</summary>
    [Reactive]
    public ReactiveCommand<Unit, Unit> RefreshAudioDevicesCommand { get; private set; } = null!;

    /// <summary>是否正在刷新设备列表。</summary>
    [Reactive]
    public bool IsRefreshingDevices { get; set; }

    // ── Disposables ───────────────────────────────────────────────────
    private readonly CompositeDisposable _disposables = new();

    /// <summary>图形设置使用空模型占位，平台数据在后台读取。</summary>
    [Reactive]
    public GraphicsBackendSettingsViewModel GraphicsBackendSettings { get; private set; } = new(null);

    /// <summary>基础偏好已完成绑定，页面内容可以交互。</summary>
    [Reactive]
    public bool ArePreferencesInitialized { get; private set; }

    /// <summary>初始化错误只作为页内信息展示，不阻止导航及其他设置加载。</summary>
    [Reactive]
    public string InitializationError { get; private set; } = string.Empty;

    /// <summary>是否存在需要展示的初始化错误。</summary>
    [Reactive]
    public bool HasInitializationError { get; private set; }

    /// <summary>是否成功读取到可用的 ONNX 后端。</summary>
    [Reactive]
    public bool IsOnnxAvailable { get; private set; }

    private Task? initializationTask;
    private bool disposed;
    private bool applyingInitialization;

    /// <summary>防止开关回拨时再次触发 async 订阅的标志。</summary>
    private bool _suppressToggle;

    public SettingsViewModel(MainViewModel navigator) : base(navigator)
    {
        BackCommand = ReactiveCommand.Create(() => Navigator.NavigateBack(this));
        ToggleNavCommand = ReactiveCommand.Create(OnToggleNav);
        SelectCategoryCommand = ReactiveCommand.Create<SettingsCategory>(OnSelectCategory);
    }

    /// <summary>页面显示后启动一次初始化；重复挂载复用任务，返回不等待后台探测。</summary>
    public Task InitializeAsync() => initializationTask ??= InitializeCoreAsync();

    /// <summary>先分批绑定基础偏好，再并行读取互不依赖的平台能力。</summary>
    private async Task InitializeCoreAsync()
    {
        await InitializeSectionAsync("Settings.Appearance.Language", InitializeGeneralSettings);
        await InitializeSectionAsync("Settings.Nav.FileAndStorage", InitializeStorageSettings);
        await InitializeSectionAsync("Settings.Nav.EditAndBehaviour", InitializeLyricsSettings);
        await InitializeSectionAsync("Settings.Edit.PianoKeyBehavior", InitializePianoSettings);
        await InitializeSectionAsync("Settings.Nav.EditAndBehaviour", InitializePlaybackSettings);
        await InitializeSectionAsync("Settings.Nav.EditAndBehaviour", InitializeEditingSettings);
        await InitializeSectionAsync("Settings.Nav.RenderAndPerformance", InitializeRenderingSettings);
        await InitializeSectionAsync("Settings.Render.OnnxRunner", InitializeOnnxSettings);
        await InitializeSectionAsync("Settings.Nav.RenderAndPerformance", InitializeAudioSettings);
        await InitializeSectionAsync("Settings.Nav.AppearanceAndLanguage", InitializeAppearanceSettings);
        if (disposed) return;
        ArePreferencesInitialized = true;

        await Task.WhenAll(
            InitializeOnnxAsync(),
            InitializeSoundFontAsync(),
            InitializeBackgroundAsync("Settings.Edit.LyricsHelper", () =>
            {
                List<LyricsHelperOption> options = ActiveLyricsHelper.Inst.Available
                    .Select(type => new LyricsHelperOption(type)).ToList();
                return (options, preferred: ActiveLyricsHelper.Inst.GetPreferred());
            }, result =>
            {
                AvailableLyricsHelpers = result.options;
                SelectedLyricsHelper = result.options.FirstOrDefault(option => option.HelperType == result.preferred);
            }),
            InitializeBackgroundAsync("Settings.Graphics.Title",
                () => new GraphicsBackendSettingsViewModel(ServiceHub.GraphicsBackendService),
                settings => GraphicsBackendSettings = settings),
            InitializeThemePreviewAsync(),
            RunInitializationAsync("Settings.Nav.RenderAndPerformance", RefreshAudioDevicesAsync),
            RunInitializationAsync("Settings.Nav.FileAndStorage", RefreshCacheSizeAsync));
    }

    /// <summary>以低优先级逐项填充控件，任何一项失败都继续处理后续设置。</summary>
    private async Task InitializeSectionAsync(string section, Action initialize)
    {
        await RunInitializationAsync(section, async () =>
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!disposed) initialize();
            }, DispatcherPriority.Background);
        });
    }

    /// <summary>耗时读取仅在后台执行，结果及绑定通知统一在 UI 线程提交。</summary>
    private Task InitializeBackgroundAsync<T>(string section, Func<T> read, Action<T> apply)
    {
        return RunInitializationAsync(section, async () =>
        {
            if (disposed) return;
            T result = await Task.Run(read);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (disposed) return;
                applyingInitialization = true;
                try
                {
                    apply(result);
                }
                finally
                {
                    applyingInitialization = false;
                }
            }, DispatcherPriority.Background);
        });
    }

    /// <summary>统一接住初始化及结果应用异常，只记录日志和页内提示。</summary>
    private async Task RunInitializationAsync(string section, Func<Task> initialize)
    {
        try
        {
            if (!disposed) await initialize();
        }
        catch (Exception exception)
        {
            await ReportInitializationErrorAsync(section, exception);
        }
    }

    /// <summary>错误统一显示在当前页，退出页面后只保留日志。</summary>
    private async Task ReportInitializationErrorAsync(string section, Exception exception)
    {
        Log.Error(exception, "Settings initialization failed: {Section}", section);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (disposed) return;
            // 错误展示不能再因资源缺失抛异常，必要时直接显示原始分类名。
            string label = section;
            try
            {
                label = L.S(section);
            }
            catch (Exception)
            {
            }
            string message = $"{label}: {exception.GetBaseException().Message}";
            InitializationError = string.IsNullOrEmpty(InitializationError)
                ? message : InitializationError + Environment.NewLine + message;
            HasInitializationError = true;
        }, DispatcherPriority.Background);
    }

    /// <summary>平台主题 API 在 UI 线程读取，只刷新说明，不重新应用全局主题。</summary>
    private Task InitializeThemePreviewAsync()
    {
        return InitializeSectionAsync("Settings.Nav.AppearanceAndLanguage", () =>
        {
            Color seed = ThemeSeedResolver.ResolveSeed(ServiceHub.SystemAccentColorProvider,
                out string source, out string fallbackReason);
            UpdateThemeColorPreview(seed, source, fallbackReason);
        });
    }

    /// <summary>后端及设备顺序探测，设备失败时仍保留已发现的运行器。</summary>
    private async Task InitializeOnnxAsync()
    {
        await InitializeBackgroundAsync("Settings.Render.OnnxRunner", GetAvailableOnnxRunners, runners =>
        {
            AvailableOnnxRunners = runners;
            SelectedOnnxRunner = runners.FirstOrDefault(runner =>
                string.Equals(runner.Value, Preferences.Default.OnnxRunner, StringComparison.OrdinalIgnoreCase))
                ?? runners.FirstOrDefault();
            IsOnnxAvailable = runners.Count > 0;
        });
        if (!IsOnnxAvailable || disposed) return;
        await InitializeBackgroundAsync("Settings.Render.OnnxDevice", GetAvailableOnnxDevices, devices =>
        {
            AvailableOnnxDevices = devices;
            SelectedOnnxDevice = devices.FirstOrDefault(device => device.DeviceId == Preferences.Default.OnnxGpu)
                ?? devices.FirstOrDefault();
            ShowOnnxDeviceSelector = SupportsOnnxDeviceSelection(SelectedOnnxRunner?.Value ?? string.Empty)
                && devices.Count > 0;
        });
    }

    /// <summary>首次访问音色单例可能读取大型文件，禁止在导航或 UI 线程执行。</summary>
    private async Task InitializeSoundFontAsync()
    {
        await InitializeBackgroundAsync("Settings.Edit.SoundFontPath", () => SoundFontPlayer.Instance.IsReady,
            loaded => IsSoundFontLoaded = loaded);
        if (!disposed) IsSoundFontInitializing = false;
    }

    /// <summary>初始化语言与导航设置及变更订阅。</summary>
    private void InitializeGeneralSettings()
    {
        PresetSeedColors = ThemeSeedPresets.All
            .Select(p => new ThemeSeedPresetOption(p.Id, p.DisplayName, p.Hex, ApplyThemePreset))
            .ToList();

        // 联动 IsNavExpanded → NavWidth
        this.WhenAnyValue(x => x.IsNavExpanded)
            .Subscribe(expanded => NavWidth = expanded ? 220 : 64)
            .DisposeWith(_disposables);

        // 初始化当前语言选项
        string savedCode = string.IsNullOrWhiteSpace(Preferences.Default.Language)
            ? LocalizationManager.FollowSystemLanguageCode
            : Preferences.Default.Language;
        SelectedLanguageOption = AvailableLanguages.FirstOrDefault(l =>
                                     string.Equals(l.Code, savedCode, StringComparison.OrdinalIgnoreCase))
                                 ?? AvailableLanguages[0];

        // 分类按需创建时控件会重新提交选择；先按语义去重，再过滤初始化，避免回写默认项。
        // 语言切换：写入 Preferences 并热加载语言资源字典
        this.WhenAnyValue(x => x.SelectedLanguageOption)
            .WhereNotNull()
            .DistinctUntilChanged(option => option.Code)
            .Skip(1)
            .Subscribe(opt =>
            {
                // 分类模板首次绑定可能重新提交当前选项，空语言偏好等价于跟随系统。
                string currentCode = string.IsNullOrWhiteSpace(Preferences.Default.Language)
                    ? LocalizationManager.FollowSystemLanguageCode : Preferences.Default.Language;
                if (string.Equals(currentCode, opt.Code, StringComparison.OrdinalIgnoreCase)) return;
                LocalizationManager.LoadLanguage(opt.Code);
                GraphicsBackendSettings.RefreshLocalization();
                Preferences.Default.Language = opt.Code;
                Preferences.Save();
            })
            .DisposeWith(_disposables);
    }

    /// <summary>初始化文件与存储设置及变更订阅。</summary>
    private void InitializeStorageSettings()
    {
        // ── 额外歌手路径初始化 ──────────────────────────────────────
        AdditionalSingerPath = Preferences.Default.AdditionalSingerPath;
        AdditionalSingerPathEnabled = !string.IsNullOrEmpty(AdditionalSingerPath);

        // 监听额外歌手路径开关
        this.WhenAnyValue(x => x.AdditionalSingerPathEnabled)
            .Skip(1)
            .Subscribe(enabled =>
            {
                if (_suppressToggle)
                {
                    _suppressToggle = false;
                    return;
                }

                if (enabled)
                {
                    // 开启：弹出文件夹选择器（必须在 UI 线程触发，避免跨线程构造控件）
                    _ = EnableAdditionalSingerPathAsync();
                }
                else
                {
                    // 关闭：清空路径
                    AdditionalSingerPath = string.Empty;
                    Preferences.Default.AdditionalSingerPath = string.Empty;
                    Preferences.Save();
                }
            })
            .DisposeWith(_disposables);

        // 更改路径命令
        ChangeAdditionalSingerPathCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            string path = await FilePicker.PickFolderAsync(L.S("FilePicker.SelectSingerDir"));
            if (!string.IsNullOrEmpty(path))
            {
                AdditionalSingerPath = path;
                Preferences.Default.AdditionalSingerPath = path;
                Preferences.Save();
            }
        });

        ClearCacheCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(PathManager.Inst.CachePath);
                    PathManager.Inst.ClearCache();
                });
            }
            finally
            {
                await RefreshCacheSizeAsync();
            }
        });
    }

    /// <summary>初始化歌词助手设置及变更订阅。</summary>
    private void InitializeLyricsSettings()
    {
        // 歌词助手设置与桌面端共用 Core 偏好。
        LyricsHelperBrackets = Preferences.Default.LyricsHelperBrackets;

        this.WhenAnyValue(x => x.SelectedLyricsHelper)
            .WhereNotNull()
            .DistinctUntilChanged(option => option.HelperType)
            .Skip(1)
            .Where(_ => !applyingInitialization)
            .Subscribe(option =>
            {
                ActiveLyricsHelper.Inst.Set(option.HelperType);
                Preferences.Default.LyricHelper = option.HelperType.Name;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.LyricsHelperBrackets)
            .Skip(1)
            .Subscribe(enabled =>
            {
                Preferences.Default.LyricsHelperBrackets = enabled;
                Preferences.Save();
            })
            .DisposeWith(_disposables);
    }

    /// <summary>初始化钢琴键与音色设置及变更订阅。</summary>
    private void InitializePianoSettings()
    {
        // 钢琴键行为初始化
        int behaviorVal = Preferences.Default.PianoKeyBehavior;
        PianoKeyBehavior behavior = behaviorVal is >= 0 and <= 2
            ? (PianoKeyBehavior)behaviorVal
            : PianoKeyBehavior.SineWave;
        SelectedPianoKeyBehavior = AvailablePianoKeyBehaviors.FirstOrDefault(b => b.Value == behavior)
                                   ?? AvailablePianoKeyBehaviors[1]; // 默认正弦波

        PianoKeyLabelMode labelMode = PianoKeyLabelFormatter.NormalizeMode(Preferences.Default.PianoKeyLabelMode);
        SelectedPianoKeyLabelMode = AvailablePianoKeyLabelModes.First(option => option.Value == labelMode);

        this.WhenAnyValue(x => x.SelectedPianoKeyLabelMode)
            .WhereNotNull()
            .DistinctUntilChanged(option => option.Value)
            .Skip(1)
            .Subscribe(option =>
            {
                Preferences.Default.PianoKeyLabelMode = (int)option.Value;
                Preferences.Save();
                MessageBus.Current.SendMessage(new PianoKeyLabelModeChangedEvent(option.Value));
            })
            .DisposeWith(_disposables);

        SoundFontPath = Preferences.Default.SoundFontPath;

        // 监听钢琴键行为变化
        this.WhenAnyValue(x => x.SelectedPianoKeyBehavior)
            .WhereNotNull()
            .DistinctUntilChanged(option => option.Value)
            .Skip(1)
            .Subscribe(opt =>
            {
                Preferences.Default.PianoKeyBehavior = (int)opt.Value;
                Preferences.Save();

                // 如果选择 SoundFont，尝试重新加载
                if (opt.Value == PianoKeyBehavior.SoundFont)
                {
                    SoundFontPlayer.Instance.TryLoadSoundFont();
                    IsSoundFontLoaded = SoundFontPlayer.Instance.IsReady;
                }
            })
            .DisposeWith(_disposables);

        // 更改 SoundFont 路径命令
        ChangeSoundFontPathCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            string path = await FilePicker.PickSingleFileAsync(L.S("FilePicker.SelectSF2"), ["*.sf2"]);
            if (!string.IsNullOrEmpty(path))
            {
                SoundFontPath = path;
                Preferences.Default.SoundFontPath = path;
                Preferences.Save();

                // 重新加载 SoundFont
                SoundFontPlayer.Instance.TryLoadSoundFont();
                IsSoundFontLoaded = SoundFontPlayer.Instance.IsReady;
            }
        });
    }

    /// <summary>初始化播放行为设置及变更订阅。</summary>
    private void InitializePlaybackSettings()
    {
        // 走带自动翻页行为初始化
        int autoScrollVal = Preferences.Default.PlaybackAutoScroll;
        SelectedAutoScrollBehavior = AvailableAutoScrollBehaviors.FirstOrDefault(b => b.Value == autoScrollVal)
                                     ?? AvailableAutoScrollBehaviors[1];

        // 监听走带自动翻页行为变化
        this.WhenAnyValue(x => x.SelectedAutoScrollBehavior)
            .WhereNotNull()
            .DistinctUntilChanged(option => option.Value)
            .Skip(1)
            .Subscribe(opt =>
            {
                Preferences.Default.PlaybackAutoScroll = opt.Value;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        int verticalScrollVal = Preferences.Default.PlaybackVerticalScroll;
        SelectedVerticalScrollBehavior = AvailableVerticalScrollBehaviors.FirstOrDefault(b => b.Value == verticalScrollVal)
                                         ?? AvailableVerticalScrollBehaviors[0];
        this.WhenAnyValue(x => x.SelectedVerticalScrollBehavior)
            .WhereNotNull()
            .DistinctUntilChanged(option => option.Value)
            .Skip(1)
            .Subscribe(opt =>
            {
                Preferences.Default.PlaybackVerticalScroll = opt.Value;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        // Initializing Stop Button Behavior
        int stopBehaviorVal = Preferences.Default.StopButtonBehavior;
        StopButtonBehavior stopBehavior = stopBehaviorVal is >= 1 and <= 3
            ? (StopButtonBehavior)stopBehaviorVal
            : StopButtonBehavior.StartTickSelectedPartZero;
        SelectedStopButtonBehavior = AvailableStopButtonBehaviors.FirstOrDefault(b => b.Value == stopBehavior)
                                     ?? AvailableStopButtonBehaviors[0];

        // Monitor changes in the behavior of the Stop button and persist them to Preferences
        this.WhenAnyValue(x => x.SelectedStopButtonBehavior)
            .WhereNotNull()
            .DistinctUntilChanged(option => option.Value)
            .Skip(1)
            .Subscribe(opt =>
            {
                Preferences.Default.StopButtonBehavior = (int)opt.Value;
                Preferences.Save();
            })
            .DisposeWith(_disposables);
    }

    /// <summary>初始化编辑行为设置及变更订阅。</summary>
    private void InitializeEditingSettings()
    {
        // Android 显示行为
        int fullscreenModeValue = Preferences.Default.AndroidFullscreenMode;
        AndroidFullscreenMode fullscreenMode = fullscreenModeValue is >= 0 and <= 2
            ? (AndroidFullscreenMode)fullscreenModeValue
            : AndroidFullscreenMode.Always;
        SelectedAndroidFullscreenMode = AvailableAndroidFullscreenModes
            .FirstOrDefault(option => option.Value == fullscreenMode)
            ?? AvailableAndroidFullscreenModes[0];
        AndroidKeepScreenAwakeWhileEditing = Preferences.Default.AndroidKeepScreenAwakeWhileEditing;

        this.WhenAnyValue(x => x.SelectedAndroidFullscreenMode)
            .WhereNotNull()
            .DistinctUntilChanged(option => option.Value)
            .Skip(1)
            .Subscribe(option =>
            {
                Preferences.Default.AndroidFullscreenMode = (int)option.Value;
                Preferences.Save();
                ServiceHub.PlatformDisplayService?.Refresh();
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.AndroidKeepScreenAwakeWhileEditing)
            .Skip(1)
            .Subscribe(enabled =>
            {
                Preferences.Default.AndroidKeepScreenAwakeWhileEditing = enabled;
                Preferences.Save();
                ServiceHub.PlatformDisplayService?.Refresh();
            })
            .DisposeWith(_disposables);

        DesktopModeEnabled = Preferences.Default.DesktopModeEnabled;
        this.WhenAnyValue(x => x.DesktopModeEnabled)
            .Skip(1)
            .Subscribe(enabled =>
            {
                Preferences.Default.DesktopModeEnabled = enabled;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        // 编辑与行为：回放刷新率 / 立绘开关 / 撤销上限 / 音高线拖拽
        PlaybackRefreshRate = Math.Clamp((int)Math.Round(Preferences.Default.PlaybackRefreshRate), 1, 60);
        ShowPortraitEnabled = Preferences.Default.ShowPortrait;
        UndoLimit = Math.Clamp(Preferences.Default.UndoLimit, 10, 100);
        PitchPenCanvasDragEnabled = Preferences.Default.PitchPenCanvasDragEnabled;
        ShowPitchPenHitArea = Preferences.Default.ShowPitchPenHitArea;
        MagnifierMagnificationFactor = MagnifierSettings.Normalize(Preferences.Default.MagnifierMagnificationFactor);
        MagnifierSliderValue = MagnifierSettings.Snap(MagnifierMagnificationFactor);
        this.WhenAnyValue(x => x.MagnifierSliderValue)
            .Skip(1)
            .Subscribe(value =>
            {
                double snapped = MagnifierSettings.Snap(value);
                if (snapped != value)
                {
                    MagnifierSliderValue = snapped;
                    return;
                }

                MagnifierMagnificationFactor = snapped;
                Preferences.Default.MagnifierMagnificationFactor = snapped;
                Preferences.Save();
            })
            .DisposeWith(_disposables);
        PitchPenNoteHitTickExtension = Math.Clamp(
            Preferences.Default.PitchPenNoteHitTickExtension,
            Preferences.SerializablePreferences.PitchPenNoteHitTickExtensionMinimum,
            Preferences.SerializablePreferences.PitchPenNoteHitTickExtensionMaximum);
        PitchPenNoteHitToneExtension = Math.Clamp(
            Preferences.Default.PitchPenNoteHitToneExtension,
            Preferences.SerializablePreferences.PitchPenNoteHitToneExtensionMinimum,
            Preferences.SerializablePreferences.PitchPenNoteHitToneExtensionMaximum);

        this.WhenAnyValue(x => x.PlaybackRefreshRate)
            .Skip(1)
            .Subscribe(value =>
            {
                int clamped = Math.Clamp(value, 1, 60);
                if (clamped != value)
                {
                    PlaybackRefreshRate = clamped;
                    return;
                }

                Preferences.Default.PlaybackRefreshRate = clamped;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.ShowPortraitEnabled)
            .Skip(1)
            .Subscribe(value =>
            {
                Preferences.Default.ShowPortrait = value;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.UndoLimit)
            .Skip(1)
            .Subscribe(value =>
            {
                int clamped = Math.Clamp(value, 10, 100);
                if (clamped != value)
                {
                    UndoLimit = clamped;
                    return;
                }

                Preferences.Default.UndoLimit = clamped;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.PitchPenCanvasDragEnabled)
            .Skip(1)
            .Subscribe(enabled =>
            {
                Preferences.Default.PitchPenCanvasDragEnabled = enabled;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.ShowPitchPenHitArea)
            .Skip(1)
            .Subscribe(enabled =>
            {
                Preferences.Default.ShowPitchPenHitArea = enabled;
                Preferences.Save();
                MessageBus.Current.SendMessage(new PitchPenHitAreaHintChangedEvent());
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.PitchPenNoteHitTickExtension)
            .Skip(1)
            .Subscribe(value =>
            {
                int clamped = Math.Clamp(
                    value,
                    Preferences.SerializablePreferences.PitchPenNoteHitTickExtensionMinimum,
                    Preferences.SerializablePreferences.PitchPenNoteHitTickExtensionMaximum);
                if (clamped != value)
                {
                    PitchPenNoteHitTickExtension = clamped;
                    return;
                }

                Preferences.Default.PitchPenNoteHitTickExtension = clamped;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.PitchPenNoteHitToneExtension)
            .Skip(1)
            .Subscribe(value =>
            {
                int clamped = Math.Clamp(
                    value,
                    Preferences.SerializablePreferences.PitchPenNoteHitToneExtensionMinimum,
                    Preferences.SerializablePreferences.PitchPenNoteHitToneExtensionMaximum);
                if (clamped != value)
                {
                    PitchPenNoteHitToneExtension = clamped;
                    return;
                }

                Preferences.Default.PitchPenNoteHitToneExtension = clamped;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        // 编辑与行为：自动保存
        AutoSaveEnabled = Preferences.Default.AutoSaveEnabled;
        AutoSaveInterval = Math.Clamp(Preferences.Default.AutoSaveInterval, 30, 600);

        this.WhenAnyValue(x => x.AutoSaveEnabled)
            .Skip(1)
            .Subscribe(enabled =>
            {
                Preferences.Default.AutoSaveEnabled = enabled;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.AutoSaveInterval)
            .Skip(1)
            .Subscribe(value =>
            {
                int clamped = Math.Clamp(value, 30, 600);
                if (clamped != value)
                {
                    AutoSaveInterval = clamped;
                    return;
                }

                Preferences.Default.AutoSaveInterval = clamped;
                Preferences.Save();
            })
            .DisposeWith(_disposables);
    }

    /// <summary>初始化渲染性能设置及变更订阅。</summary>
    private void InitializeRenderingSettings()
    {
        // 渲染与性能初始化
        PerformanceMonitorEnabled = Preferences.Default.PerformanceMonitorEnabled;
        DiffSingerSteps = Preferences.Default.DiffSingerSteps;
        DiffSingerStepsVariance = Preferences.Default.DiffSingerStepsVariance;
        DiffSingerStepsPitch = Preferences.Default.DiffSingerStepsPitch;
        PreRenderEnabled = Preferences.Default.PreRender;
        NumRenderThreads = Preferences.Default.NumRenderThreads;

        this.WhenAnyValue(x => x.PerformanceMonitorEnabled)
            .Skip(1)
            .Subscribe(enabled =>
            {
                Preferences.Default.PerformanceMonitorEnabled = enabled;
                Preferences.Save();
                PerformanceMonitorService.Instance.SetEnabled(enabled);
            })
            .DisposeWith(_disposables);

        // 监听 DiffSinger 步数变化
        this.WhenAnyValue(x => x.DiffSingerSteps)
            .Skip(1)
            .Subscribe(value =>
            {
                Preferences.Default.DiffSingerSteps = value;
                Preferences.Save();
            })
            .DisposeWith(_disposables);
        this.WhenAnyValue(x => x.DiffSingerStepsVariance)
            .Skip(1)
            .Subscribe(value =>
            {
                Preferences.Default.DiffSingerStepsVariance = value;
                Preferences.Save();
            })
            .DisposeWith(_disposables);
        this.WhenAnyValue(x => x.DiffSingerStepsPitch)
            .Skip(1)
            .Subscribe(value =>
            {
                Preferences.Default.DiffSingerStepsPitch = value;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        // 监听预渲染设置变化
        this.WhenAnyValue(x => x.PreRenderEnabled)
            .Skip(1)
            .Subscribe(value =>
            {
                Preferences.Default.PreRender = value;
                Preferences.Save();
            })
            .DisposeWith(_disposables);
        this.WhenAnyValue(x => x.NumRenderThreads)
            .Skip(1)
            .Subscribe(value =>
            {
                Preferences.Default.NumRenderThreads = value;
                Preferences.Save();
            })
            .DisposeWith(_disposables);
    }

    /// <summary>初始化机器学习后端设置及变更订阅。</summary>
    private void InitializeOnnxSettings()
    {
        this.WhenAnyValue(x => x.SelectedOnnxRunner)
            .WhereNotNull()
            .DistinctUntilChanged(option => option.Value)
            .Skip(1)
            .Where(_ => !applyingInitialization)
            .Subscribe(opt =>
            {
                Preferences.Default.OnnxRunner = opt.Value;
                ShowOnnxDeviceSelector = SupportsOnnxDeviceSelection(opt.Value)
                                         && AvailableOnnxDevices.Count > 0;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        this.WhenAnyValue(x => x.SelectedOnnxDevice)
            .WhereNotNull()
            .DistinctUntilChanged(option => option.DeviceId)
            .Skip(1)
            .Where(_ => !applyingInitialization)
            .Subscribe(device =>
            {
                Preferences.Default.OnnxGpu = device.DeviceId;
                Preferences.Save();
            })
            .DisposeWith(_disposables);
    }

    /// <summary>初始化音频后端与设备设置及变更订阅。</summary>
    private void InitializeAudioSettings()
    {
        // 音频后端与设备初始化
        AvailableAudioBackends = GetAvailableAudioBackends();

        string savedBackend = Preferences.Default.AudioBackend;
        SelectedAudioBackend = AvailableAudioBackends.FirstOrDefault(b => b.Value == savedBackend)
                               ?? AvailableAudioBackends[0]; // 默认为自动

        // 监听音频后端变化
        this.WhenAnyValue(x => x.SelectedAudioBackend)
            .WhereNotNull()
            .DistinctUntilChanged(option => option.Value)
            .Skip(1)
            .Subscribe(opt =>
            {
                Preferences.Default.AudioBackend = opt.Value;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        // 初始化音频设备列表
        AvailableAudioDevices = new List<AudioDeviceOption> { new(null, L.S("Settings.Audio.DefaultDevice")) };
        SelectedAudioDevice = AvailableAudioDevices[0];
        RefreshAudioDevicesCommand = ReactiveCommand.CreateFromTask(RefreshAudioDevicesAsync);

        // 监听音频设备变化
        this.WhenAnyValue(x => x.SelectedAudioDevice)
            .WhereNotNull()
            .DistinctUntilChanged(option => (option.Device?.guid, option.Device?.deviceNumber))
            .Skip(1)
            .Where(_ => !applyingInitialization)
            .Subscribe(opt =>
            {
                if (opt.Device != null)
                {
                    Preferences.Default.PlaybackDevice = opt.Device.name ?? string.Empty; // Core 乱写null
                    Preferences.Default.PlaybackDeviceNumber = opt.Device.deviceNumber;
                    // 尝试应用设备选择
                    PlaybackManager.Inst.AudioOutput.SelectDevice(opt.Device.guid, opt.Device.deviceNumber);
                }
                else
                {
                    Preferences.Default.PlaybackDevice = string.Empty;
                    Preferences.Default.PlaybackDeviceNumber = 0;
                }

                Preferences.Save();
            })
            .DisposeWith(_disposables);
    }

    /// <summary>初始化外观与主题设置及变更订阅。</summary>
    private void InitializeAppearanceSettings()
    {
        // 主题初始化
        string savedTheme = Preferences.Default.ThemeName;
        SelectedThemeOption =
            AvailableThemes.FirstOrDefault(t => string.Equals(t.Value, savedTheme, StringComparison.OrdinalIgnoreCase))
            ?? AvailableThemes[1]; // 兼容旧默认值 Light

        string savedSeedHex = Preferences.Default.ThemeColorSeedHex;
        ThemeSeedHexInput = ThemeSeedResolver.TryNormalizeHex(savedSeedHex, out string normalizedSeed)
            ? normalizedSeed
            : "#FF0000";

        SelectedThemeColorMode =
            AvailableThemeColorModes.FirstOrDefault(m => m.Value == Preferences.Default.ThemeColorMode)
            ?? AvailableThemeColorModes[0];
        IsCustomThemeColorMode = SelectedThemeColorMode.Value == (int)ThemeColorMode.Custom;

        string presetId = Preferences.Default.ThemeColorPresetId;
        SelectedThemePreset = PresetSeedColors.FirstOrDefault(p => p.Id == presetId)
                              ?? PresetSeedColors.FirstOrDefault(p =>
                                  string.Equals(p.Hex, ThemeSeedHexInput, StringComparison.OrdinalIgnoreCase));

        // 监听主题变化：写入 Preferences 并即时应用
        this.WhenAnyValue(x => x.SelectedThemeOption)
            .WhereNotNull()
            .DistinctUntilChanged(option => option.Value)
            .Skip(1)
            .Subscribe(opt =>
            {
                Preferences.Default.ThemeName = opt.Value;
                Preferences.Save();

                if (Application.Current is not null)
                {
                    Application.Current.RequestedThemeVariant = ToThemeVariant(opt.Value);
                    ThemeManagerV2.OnThemeVariantChanged();
                }
            })
            .DisposeWith(_disposables);

        // 主题色模式变化：持久化并立即应用
        this.WhenAnyValue(x => x.SelectedThemeColorMode)
            .WhereNotNull()
            .DistinctUntilChanged(option => option.Value)
            .Skip(1)
            .Subscribe(mode =>
            {
                Preferences.Default.ThemeColorMode = mode.Value;
                Preferences.Save();
                IsCustomThemeColorMode = mode.Value == (int)ThemeColorMode.Custom;
                ApplyResolvedThemeColor();
            })
            .DisposeWith(_disposables);

        ApplyThemeSeedHexCommand = ReactiveCommand.Create(ApplyThemeSeedHex);

        ApplyThemePresetCommand = ReactiveCommand.Create<ThemeSeedPresetOption>(preset =>
        {
            if (preset == null)
            {
                return;
            }

            ApplyThemePreset(preset);
        });

        OpenThemeColorPickerCommand = ReactiveCommand.CreateFromTask(OpenThemeColorPickerAsync);

        ResetThemeSeedCommand = ReactiveCommand.Create(() =>
        {
            ThemeSeedHexInput = "#FF0000";
            SelectedThemePreset = PresetSeedColors.FirstOrDefault(p => p.Hex == "#FF0000");
            Preferences.Default.ThemeColorSeedHex = "#FF0000";
            Preferences.Default.ThemeColorPresetId = string.Empty;
            Preferences.Save();
            ApplyResolvedThemeColor();
        });
    }

    /// <summary>
    /// 获取当前平台可用的机器学习运行器后端。
    /// </summary>
    private static List<OnnxRunnerOption> GetAvailableOnnxRunners()
    {
        List<string> runners = Onnx.getRunnerOptions();
        if (runners.Count == 0)
        {
            runners.Add("CPU");
        }

        return runners.Select(runner => new OnnxRunnerOption(runner, runner)).ToList();
    }

    /// <summary>
    /// 获取当前平台可用的机器学习加速设备。
    /// </summary>
    private static List<OnnxDeviceOption> GetAvailableOnnxDevices()
    {
        return Onnx.getGpuInfo()
            .Select(device => new OnnxDeviceOption(device.deviceId, device.ToString()))
            .ToList();
    }

    private static bool SupportsOnnxDeviceSelection(string runner)
    {
        return runner is "DirectML" or "CUDA";
    }

    /// <summary>
    /// 根据当前平台获取可用的音频后端列表。
    /// </summary>
    private static List<AudioBackendOption> GetAvailableAudioBackends()
    {
        List<AudioBackendOption> backends = [new("", L.S("Settings.Audio.AutoSelect"))];

        // 根据平台添加可用后端
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            backends.Add(new AudioBackendOption("MiniAudio", "MiniAudio"));
            backends.Add(new AudioBackendOption("NAudio", "NAudio"));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                 RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            backends.Add(new AudioBackendOption("MiniAudio", "MiniAudio"));
        }
        else if (OperatingSystem.IsAndroid())
        {
            backends.Add(new AudioBackendOption("AudioTrack", "AudioTrack"));
            backends.Add(new AudioBackendOption("MiniAudio", "MiniAudio"));
        }
        else if (OperatingSystem.IsIOS())
        {
            backends.Add(new AudioBackendOption("AVAudioEngine", "AVAudioEngine"));
        }
        // Browser 目前只支持 Dummy

        backends.Add(new AudioBackendOption("Dummy", L.S("Settings.Audio.Dummy")));
        return backends;
    }

    /// <summary>
    /// 刷新音频设备列表（异步）。
    /// </summary>
    private async Task RefreshAudioDevicesAsync()
    {
        if (disposed || IsRefreshingDevices) return;
        IsRefreshingDevices = true;

        try
        {
            // 在后台线程获取设备列表
            List<AudioDeviceOption> devices = await Task.Run(() =>
            {
                List<AudioDeviceOption> list = [new(null, L.S("Settings.Audio.DefaultDevice"))];

                IAudioOutput audioOutput = PlaybackManager.Inst.AudioOutput;
                List<AudioOutputDevice> outputDevices = audioOutput.GetOutputDevices();
                foreach (AudioOutputDevice device in outputDevices)
                {
                    list.Add(new AudioDeviceOption(device,
                        device.name ??
                        string.Format(L.S("Settings.Audio.Device"), device.deviceNumber))); // Core 乱写null
                }

                return list;
            });

            // 回到 UI 线程更新
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (disposed) return;
                applyingInitialization = true;
                try
                {
                    AvailableAudioDevices = devices;
                    // 恢复显示值时不切换设备，也不回写偏好。
                    SelectedAudioDevice = devices.FirstOrDefault(device =>
                        device.Device?.name == Preferences.Default.PlaybackDevice) ?? devices[0];
                }
                finally
                {
                    applyingInitialization = false;
                }
            });
        }
        catch (Exception exception)
        {
            await ReportInitializationErrorAsync("Settings.Nav.RenderAndPerformance", exception);
        }
        finally
        {
            if (!disposed) IsRefreshingDevices = false;
        }
    }

    private async Task RefreshCacheSizeAsync()
    {
        if (disposed) return;
        int version = ++_cacheSizeRefreshVersion;
        CacheSize = "…";
        string size;
        try
        {
            size = await Task.Run(() => PathManager.Inst.GetCacheSize());
        }
        catch (Exception exception)
        {
            await ReportInitializationErrorAsync("Settings.Nav.FileAndStorage", exception);
            size = "—";
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // 忽略旧统计结果，避免覆盖清理后或重新进入分类时的最新结果。
            if (!disposed && version == _cacheSizeRefreshVersion)
            {
                CacheSize = size;
            }
        });
    }

    private void OnToggleNav()
    {
        IsNavExpanded = !IsNavExpanded;
    }

    private void OnSelectCategory(SettingsCategory category)
    {
        SelectedCategory = category;
        if (ArePreferencesInitialized && category == SettingsCategory.FileAndStorage)
        {
            _ = RefreshCacheSizeAsync();
        }
    }

    public void Dispose()
    {
        disposed = true;
        ++_cacheSizeRefreshVersion;
        _disposables.Dispose();
        GC.SuppressFinalize(this);
    }

    private static ThemeVariant ToThemeVariant(string value) => value switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    private void ApplyThemeSeedHex()
    {
        if (!ThemeSeedResolver.TryNormalizeHex(ThemeSeedHexInput, out string normalized))
        {
            ThemeColorHint = L.S("Settings.Appearance.ThemeColor.InvalidHex");
            ApplyResolvedThemeColor();
            return;
        }

        ThemeSeedHexInput = normalized;
        SelectedThemeColorMode = AvailableThemeColorModes.First(m => m.Value == (int)ThemeColorMode.Custom);
        IsCustomThemeColorMode = true;

        ThemeSeedPresetOption? preset = PresetSeedColors.FirstOrDefault(p =>
            string.Equals(p.Hex, normalized, StringComparison.OrdinalIgnoreCase));
        SelectedThemePreset = preset;

        Preferences.Default.ThemeColorMode = (int)ThemeColorMode.Custom;
        Preferences.Default.ThemeColorSeedHex = normalized;
        Preferences.Default.ThemeColorPresetId = preset?.Id ?? string.Empty;
        Preferences.Save();

        ApplyResolvedThemeColor();
    }

    private void ApplyThemePreset(ThemeSeedPresetOption preset)
    {
        SelectedThemeColorMode = AvailableThemeColorModes.First(m => m.Value == (int)ThemeColorMode.Custom);
        IsCustomThemeColorMode = true;
        ThemeSeedHexInput = preset.Hex;
        SelectedThemePreset = preset;
        ThemeColorHint = string.Empty;

        Preferences.Default.ThemeColorMode = (int)ThemeColorMode.Custom;
        Preferences.Default.ThemeColorSeedHex = preset.Hex;
        Preferences.Default.ThemeColorPresetId = preset.Id;
        Preferences.Save();

        ApplyResolvedThemeColor();
    }

    private async Task OpenThemeColorPickerAsync()
    {
        Color originalSeed = ThemeManagerV2.CurrentSeed;
        Color initialSeed = ThemeSeedResolver.TryParseHexSeed(ThemeSeedHexInput, out Color parsed)
            ? parsed
            : originalSeed;

        ThemeColorPickerDialogViewModel vm = new(
            initialSeed,
            preview =>
            {
                ThemeVariant variant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Default;
                ThemeManagerV2.ApplyGlobalTheme(preview, variant);
                ThemeSeedPreviewBrush = new SolidColorBrush(preview);
            });

        string? result = await PopupService.Show<string?>(new ThemeColorPickerDialog(), vm);
        if (ThemeSeedResolver.TryNormalizeHex(result, out string normalized))
        {
            ThemeSeedHexInput = normalized;
            ThemeSeedPresetOption? preset = PresetSeedColors.FirstOrDefault(p =>
                string.Equals(p.Hex, normalized, StringComparison.OrdinalIgnoreCase));
            SelectedThemePreset = preset;

            SelectedThemeColorMode = AvailableThemeColorModes.First(m => m.Value == (int)ThemeColorMode.Custom);
            IsCustomThemeColorMode = true;

            Preferences.Default.ThemeColorMode = (int)ThemeColorMode.Custom;
            Preferences.Default.ThemeColorSeedHex = normalized;
            Preferences.Default.ThemeColorPresetId = preset?.Id ?? string.Empty;
            Preferences.Save();

            ApplyResolvedThemeColor();
            return;
        }

        ThemeVariant restoreVariant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Default;
        ThemeManagerV2.ApplyGlobalTheme(originalSeed, restoreVariant);
        ThemeSeedPreviewBrush = new SolidColorBrush(originalSeed);
    }

    private void ApplyResolvedThemeColor()
    {
        Color seed = ThemeManagerV2.ApplyConfiguredTheme(
            ServiceHub.SystemAccentColorProvider,
            out string source,
            out string fallbackReason);
        UpdateThemeColorPreview(seed, source, fallbackReason);
    }

    /// <summary>同步主题预览说明，进入设置时不重新应用全局主题。</summary>
    private void UpdateThemeColorPreview(Color seed, string source, string fallbackReason)
    {
        ThemeSeedPreviewBrush = new SolidColorBrush(seed);
        ThemeColorSystemSource = source;
        HasThemeColorSystemSource = !string.IsNullOrWhiteSpace(source);

        if (fallbackReason == "SystemUnavailableUseCustom")
        {
            ThemeColorHint = L.S("Settings.Appearance.ThemeColor.SystemUnavailable");
            HasThemeColorHint = true;
            return;
        }

        if (fallbackReason == "CustomInvalidUseSystem")
        {
            ThemeColorHint = L.S("Settings.Appearance.ThemeColor.FallbackToSystem");
            HasThemeColorHint = true;
            return;
        }

        if (fallbackReason == "SystemUnavailableAndCustomInvalid" ||
            fallbackReason == "CustomInvalidAndSystemUnavailable")
        {
            ThemeColorHint = L.S("Settings.Appearance.ThemeColor.SystemUnavailable");
            HasThemeColorHint = true;
            return;
        }

        ThemeColorHint = string.Empty;
        HasThemeColorHint = false;
    }

    private async Task EnableAdditionalSingerPathAsync()
    {
        try
        {
            string path = await FilePicker.PickFolderAsync(L.S("FilePicker.SelectSingerDir"));
            if (string.IsNullOrEmpty(path))
            {
                // 用户取消：回拨开关，不写入
                _suppressToggle = true;
                AdditionalSingerPathEnabled = false;
                return;
            }

            AdditionalSingerPath = path;
            Preferences.Default.AdditionalSingerPath = path;
            Preferences.Save();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "启用额外歌手路径时发生异常");
            _suppressToggle = true;
            AdditionalSingerPathEnabled = false;
            ToastService.Enqueue(L.S("Common.UnexpectedError"));
        }
    }
}
