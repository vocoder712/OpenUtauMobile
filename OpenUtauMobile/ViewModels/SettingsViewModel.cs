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
using OpenUtauMobile.Storage;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
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

/// <summary>走带自动翻页行为选项（用于绑定到选择器）。</summary>
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
    public IReadOnlyList<ThemeSeedPresetOption> PresetSeedColors { get; }

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

    public ReactiveCommand<Unit, Unit> ApplyThemeSeedHexCommand { get; }
    public ReactiveCommand<ThemeSeedPresetOption, Unit> ApplyThemePresetCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenThemeColorPickerCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetThemeSeedCommand { get; }

    // ── File & Storage ────────────────────────────────────────────────
    /// <summary>额外歌手路径开关，开启时表示已设置了有效路径。</summary>
    [Reactive]
    public bool AdditionalSingerPathEnabled { get; set; }

    /// <summary>额外歌手路径，实时与 Preferences 同步。</summary>
    [Reactive]
    public string AdditionalSingerPath { get; set; }

    /// <summary>重新选择额外歌手路径命令。</summary>
    public ReactiveCommand<Unit, Unit> ChangeAdditionalSingerPathCommand { get; }

    /// <summary>清除渲染缓存命令。</summary>
    public ReactiveCommand<Unit, Unit> ClearRenderCacheCommand { get; }

    // ── Edit & Behaviour ─────────────────────────────────────────────
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

    /// <summary>当前 SoundFont 文件路径。</summary>
    [Reactive]
    public string SoundFontPath { get; set; }

    /// <summary>SoundFont 是否已成功加载。</summary>
    [Reactive]
    public bool IsSoundFontLoaded { get; set; }

    /// <summary>更改 SoundFont 路径命令。</summary>
    public ReactiveCommand<Unit, Unit> ChangeSoundFontPathCommand { get; }

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

    /// <summary>自动保存间隔秒数（0=禁用，30-600）。</summary>
    [Reactive]
    public int AutoSaveInterval { get; set; }

    /// <summary>是否启用自动保存。</summary>
    [Reactive]
    public bool AutoSaveEnabled { get; set; }

    // ── Render & Performance ────────────────────────────────────────
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
    public IReadOnlyList<OnnxRunnerOption> AvailableOnnxRunners { get; }

    /// <summary>当前选中的机器学习运行器后端。</summary>
    [Reactive]
    public OnnxRunnerOption? SelectedOnnxRunner { get; set; }

    // ── Audio Backend & Device ──────────────────────────────────────
    /// <summary>可选音频后端列表（根据平台动态生成）。</summary>
    public IReadOnlyList<AudioBackendOption> AvailableAudioBackends { get; }

    /// <summary>当前选中的音频后端选项。</summary>
    [Reactive]
    public AudioBackendOption? SelectedAudioBackend { get; set; }

    /// <summary>可用音频设备列表。</summary>
    [Reactive]
    public IReadOnlyList<AudioDeviceOption> AvailableAudioDevices { get; set; }

    /// <summary>当前选中的音频设备选项。</summary>
    [Reactive]
    public AudioDeviceOption? SelectedAudioDevice { get; set; }

    /// <summary>刷新音频设备列表命令。</summary>
    public ReactiveCommand<Unit, Unit> RefreshAudioDevicesCommand { get; }

    /// <summary>是否正在刷新设备列表。</summary>
    [Reactive]
    public bool IsRefreshingDevices { get; set; }

    // ── Disposables ───────────────────────────────────────────────────
    private readonly CompositeDisposable _disposables = new();

    /// <summary>防止开关回拨时再次触发 async 订阅的标志。</summary>
    private bool _suppressToggle;

    public SettingsViewModel(MainViewModel navigator) : base(navigator)
    {
        BackCommand = ReactiveCommand.Create(() => Navigator.NavigateBack(this));
        ToggleNavCommand = ReactiveCommand.Create(OnToggleNav);
        SelectCategoryCommand = ReactiveCommand.Create<SettingsCategory>(OnSelectCategory);
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

        // 语言切换：写入 Preferences 并热加载语言资源字典
        this.WhenAnyValue(x => x.SelectedLanguageOption)
            .Skip(1) // 跳过初始值，避免重复加载
            .WhereNotNull()
            .Subscribe(opt =>
            {
                LocalizationManager.LoadLanguage(opt.Code);
                Preferences.Default.Language = opt.Code;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

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

        ClearRenderCacheCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await Task.Run(() =>
            {
                Directory.CreateDirectory(PathManager.Inst.CachePath);
                PathManager.Inst.ClearCache();
            });
        });

        // 钢琴键行为初始化
        int behaviorVal = Preferences.Default.PianoKeyBehavior;
        PianoKeyBehavior behavior = behaviorVal is >= 0 and <= 2
            ? (PianoKeyBehavior)behaviorVal
            : PianoKeyBehavior.SineWave;
        SelectedPianoKeyBehavior = AvailablePianoKeyBehaviors.FirstOrDefault(b => b.Value == behavior)
                                   ?? AvailablePianoKeyBehaviors[1]; // 默认正弦波

        SoundFontPath = Preferences.Default.SoundFontPath;
        IsSoundFontLoaded = SoundFontPlayer.Instance.IsReady;

        // 监听钢琴键行为变化
        this.WhenAnyValue(x => x.SelectedPianoKeyBehavior)
            .Skip(1)
            .WhereNotNull()
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
            string path = await FilePicker.PickSingleFileAsync(L.S("FilePicker.SelectSF2"), new[] { "*.sf2" });
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

        // 走带自动翻页行为初始化
        int autoScrollVal = Preferences.Default.PlaybackAutoScroll;
        SelectedAutoScrollBehavior = AvailableAutoScrollBehaviors.FirstOrDefault(b => b.Value == autoScrollVal)
                                     ?? AvailableAutoScrollBehaviors[1];

        // 监听走带自动翻页行为变化
        this.WhenAnyValue(x => x.SelectedAutoScrollBehavior)
            .Skip(1)
            .WhereNotNull()
            .Subscribe(opt =>
            {
                Preferences.Default.PlaybackAutoScroll = opt.Value;
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
            .Skip(1)
            .WhereNotNull()
            .Subscribe(opt =>
            {
                Preferences.Default.StopButtonBehavior = (int)opt.Value;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        // 编辑与行为：回放刷新率 / 立绘开关 / 撤销上限
        PlaybackRefreshRate = Math.Clamp((int)Math.Round(Preferences.Default.PlaybackRefreshRate), 1, 60);
        ShowPortraitEnabled = Preferences.Default.ShowPortrait;
        UndoLimit = Math.Clamp(Preferences.Default.UndoLimit, 10, 100);

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

        // 渲染与性能初始化
        DiffSingerSteps = Preferences.Default.DiffSingerSteps;
        DiffSingerStepsVariance = Preferences.Default.DiffSingerStepsVariance;
        DiffSingerStepsPitch = Preferences.Default.DiffSingerStepsPitch;
        PreRenderEnabled = Preferences.Default.PreRender;
        NumRenderThreads = Preferences.Default.NumRenderThreads;

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

        // 机器学习运行器后端初始化
        AvailableOnnxRunners = GetAvailableOnnxRunners();
        string savedOnnxRunner = Preferences.Default.OnnxRunner;
        SelectedOnnxRunner = AvailableOnnxRunners.FirstOrDefault(r =>
                                 string.Equals(r.Value, savedOnnxRunner, StringComparison.OrdinalIgnoreCase))
                             ?? AvailableOnnxRunners[0];

        this.WhenAnyValue(x => x.SelectedOnnxRunner)
            .Skip(1)
            .WhereNotNull()
            .Subscribe(opt =>
            {
                Preferences.Default.OnnxRunner = opt.Value;
                Preferences.Save();
            })
            .DisposeWith(_disposables);

        // 音频后端与设备初始化
        AvailableAudioBackends = GetAvailableAudioBackends();

        string savedBackend = Preferences.Default.AudioBackend;
        SelectedAudioBackend = AvailableAudioBackends.FirstOrDefault(b => b.Value == savedBackend)
                               ?? AvailableAudioBackends[0]; // 默认为自动

        // 监听音频后端变化
        this.WhenAnyValue(x => x.SelectedAudioBackend)
            .Skip(1)
            .WhereNotNull()
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
        _ = RefreshAudioDevicesAsync();

        // 监听音频设备变化
        this.WhenAnyValue(x => x.SelectedAudioDevice)
            .Skip(1)
            .WhereNotNull()
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
            .Skip(1)
            .WhereNotNull()
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
            .Skip(1)
            .WhereNotNull()
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

        ApplyResolvedThemeColor();
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
        // iOS/Browser 目前只支持 Dummy

        backends.Add(new AudioBackendOption("Dummy", L.S("Settings.Audio.Dummy")));
        return backends;
    }

    /// <summary>
    /// 刷新音频设备列表（异步）。
    /// </summary>
    private async Task RefreshAudioDevicesAsync()
    {
        IsRefreshingDevices = true;

        try
        {
            // 在后台线程获取设备列表
            List<AudioDeviceOption> devices = await Task.Run(() =>
            {
                List<AudioDeviceOption> list = [new(null, L.S("Settings.Audio.DefaultDevice"))];

                try
                {
                    IAudioOutput audioOutput = PlaybackManager.Inst.AudioOutput;

                    List<AudioOutputDevice> outputDevices = audioOutput.GetOutputDevices();
                    foreach (AudioOutputDevice device in outputDevices)
                    {
                        list.Add(new AudioDeviceOption(device,
                            device.name ??
                            string.Format(L.S("Settings.Audio.Device"), device.deviceNumber))); // Core 乱写null
                    }
                }
                catch (Exception)
                {
                    // 获取设备失败，保持默认列表
                }

                return list;
            });

            // 回到 UI 线程更新
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableAudioDevices = devices;

                // 尝试恢复之前选择的设备
                string savedDeviceName = Preferences.Default.PlaybackDevice;
                if (!string.IsNullOrEmpty(savedDeviceName))
                {
                    AudioDeviceOption? matched = devices.FirstOrDefault(d => d.Device?.name == savedDeviceName);
                    if (matched != null)
                    {
                        SelectedAudioDevice = matched;
                        return;
                    }
                }

                SelectedAudioDevice = devices[0];
            });
        }
        finally
        {
            IsRefreshingDevices = false;
        }
    }

    private void OnToggleNav()
    {
        IsNavExpanded = !IsNavExpanded;
    }

    private void OnSelectCategory(SettingsCategory category)
    {
        SelectedCategory = category;
    }

    public void Dispose()
    {
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
        ThemeVariant variant = Application.Current?.ActualThemeVariant ?? ThemeVariant.Default;
        Color seed = ThemeSeedResolver.ResolveSeed(ServiceHub.SystemAccentColorProvider, out string source,
            out string fallbackReason);
        ThemeManagerV2.ApplyGlobalTheme(seed, variant);
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