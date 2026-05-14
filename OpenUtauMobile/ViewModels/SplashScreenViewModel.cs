using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Themes.OpenUtauMobile.Runtime;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels;

public class SplashScreenViewModel : NavigateViewModelBase, IDisposable
{
    private const int SimulatedDelayMs = 2000;
    private readonly CancellationTokenSource _cts = new();

    [Reactive] public string InitState { get; set; } = "OpenUtau Mobile";
    [Reactive] public double ProgressPercent { get; set; }

    public string Version { get; }
    public string CoreVersion { get; }

    public SplashScreenViewModel(MainViewModel navigator) : base(navigator)
    {
        Assembly assembly = typeof(SplashScreenViewModel).Assembly;

        Version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "Unknown";

        CoreVersion = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == "CoreVersion")?
            .Value ?? "Unknown";

        _ = InitializeAsync(_cts.Token);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        Thread mainThread = Thread.CurrentThread;
        TaskScheduler mainScheduler = TaskScheduler.FromCurrentSynchronizationContext();
        DocManager.Inst.PostOnUIThread = action => Avalonia.Threading.Dispatcher.UIThread.Post(action);

        await Task.Run(() =>
        {
            Log.Information("==========开始初始化OpenUtau后端==========");

            try
            {
                // ---- 阶段 1：主题 ----
                ct.ThrowIfCancellationRequested();
                PostToUI(() =>
                {
                    Application.Current?.RequestedThemeVariant = ToThemeVariant(Preferences.Default.ThemeName);
                    ThemeManagerV2.OnThemeVariantChanged();
                    InitState = L.S("Splash.Localization");
                    ProgressPercent = 14;
                });
                Log.Information("主题初始化完成");

                // ---- 阶段 2：本地化 ----
                ct.ThrowIfCancellationRequested();
                string languagePreference = Preferences.Default.Language;
                bool followSystem = string.IsNullOrWhiteSpace(languagePreference) ||
                                    string.Equals(languagePreference, LocalizationManager.FollowSystemLanguageCode,
                                        StringComparison.OrdinalIgnoreCase);
                CultureInfo culture = CultureInfo.CurrentCulture;
                if (!followSystem)
                {
                    try
                    {
                        culture = new CultureInfo(languagePreference);
                    }
                    catch (CultureNotFoundException)
                    {
                        culture = new CultureInfo("zh-Hans");
                    }
                }
                string resolvedLanguage = LocalizationManager.ResolveLanguagePreference(languagePreference);
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                PostToUI(() => LocalizationManager.LoadLanguage(languagePreference));
                Log.Information($"语言偏好：{languagePreference}, 实际加载：{resolvedLanguage}");
                PostToUI(() => ProgressPercent = 28);
                Log.Information("语言本地化初始化完成");

                // ---- 阶段 3：DocManager ----
                ct.ThrowIfCancellationRequested();
                PostToUI(() => { InitState = L.S("Splash.DocManager"); ProgressPercent = 42; });
                DocManager.Inst.Initialize(mainThread, mainScheduler);
                GlobalErrorSubscriber.Instance.Register();
                Log.Information("DocManager初始化完成");

                // ---- 阶段 4：插件 ----
                ct.ThrowIfCancellationRequested();
                PostToUI(() => { InitState = L.S("Splash.Phonemizer"); ProgressPercent = 57; });
                EnsureBuiltinLoaded();
                Log.Information("插件加载完成");

                // ---- 阶段 5：ToolManager ----
                ct.ThrowIfCancellationRequested();
                PostToUI(() => { InitState = L.S("Splash.ToolManager"); ProgressPercent = 71; });
                ToolsManager.Inst.Initialize();
                Log.Information("ToolsManager初始化完成");

                // ---- 阶段 6：SingerManager ----
                ct.ThrowIfCancellationRequested();
                PostToUI(() => { InitState = L.S("Splash.SingerManager"); ProgressPercent = 85; });
                SingerManager.Inst.Initialize();
                foreach (List<USinger> group in SingerManager.Inst.SingerGroups.Values)
                {
                    if (group == null) continue;
                    foreach (USinger singer in group)
                    {
                        if (singer != null)
                        {
                            singer.Reload();
                        }
                    }
                }
                Log.Information("SingerManager初始化完成");

                // ---- 阶段 7：音频后端 ----
                ct.ThrowIfCancellationRequested();
                PostToUI(() => { InitState = L.S("Splash.AudioBackend"); ProgressPercent = 92; });
                if (ServiceHub.InitAudioOutput != null)
                {
                    ServiceHub.InitAudioOutput.Invoke();
                }
                else
                {
                    Log.Warning("未设置初始化音频输出的委托，跳过音频后端初始化");
                }
                Log.Information("PlaybackManager初始化完成");

                // ---- 全部完成，导航到主页 ----
                ct.ThrowIfCancellationRequested();
                Log.Information("==========OpenUtau后端初始化完成==========");
                PostToUI(() =>
                {
                    ProgressPercent = 100;
                    InitState = L.S("Splash.Complete");
                    Navigator.Navigate(new HomeViewModel(Navigator));
                });
            }
            catch (OperationCanceledException)
            {
                Log.Information("初始化已取消");
            }
            catch (Exception e)
            {
                Log.Error($"OpenUtau后端初始化失败: {e}");
                PostToUI(() =>
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e)));
            }
        }, ct);
    }

    private static void PostToUI(Action action)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(action);
    }

    private static ThemeVariant ToThemeVariant(string value) => value switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    public override void OnBackRequested()
    {
        // 在SplashScreen页面禁用返回操作
    }

    private static void EnsureBuiltinLoaded()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            return; // 桌面平台已经在core通过dll加载了
        }

        Assembly builtinAssembly = Assembly.Load("OpenUtau.Plugin.Builtin");
        Assembly.Load("OpenUtauMobile.Plugin.Renderers");

        foreach (Type type in builtinAssembly.GetExportedTypes())
        {
            if (!type.IsAbstract && type.IsSubclassOf(typeof(Phonemizer)))
            {
                PhonemizerFactory.Get(type);
            }
        }

        PhonemizerFactory.BuildList();

        Log.Information("内建插件程序集合并成功。");
    }
}