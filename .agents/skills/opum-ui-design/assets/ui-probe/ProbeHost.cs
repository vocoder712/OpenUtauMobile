using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using ReactiveUI.Avalonia;

namespace UiProbe;

/// <summary>每个进程只启动一个隔离宿主；业务断言放在场景中。</summary>
internal sealed class ProbeHost : IDisposable
{
    private readonly List<Window> windows = new();
    private readonly List<IDisposable> subscriptions = new();
    public string Output { get; }

    public ProbeHost(string scenario)
    {
        if (scenario is not ("basic" or "magnifier" or "settings"))
        {
            throw new ArgumentException("Unknown scenario: " + scenario);
        }
        // 当前已验证 Windows 的便携数据路径；其他平台先审查 PathManager 的初始化副作用。
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Review PathManager isolation for this platform first.");
        }
        string project = ProjectDirectory();
        GuardPath(project, AppContext.BaseDirectory);
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "installed.txt")))
        {
            throw new InvalidOperationException("Installed-mode marker found in probe output.");
        }
        // 此处先访问 PathManager，再允许任何 Preferences 或 ViewModel 初始化。
        GuardPath(project, PathManager.Inst.DataPath);
        GuardPath(project, PathManager.Inst.CachePath);
        GuardPath(project, PathManager.Inst.PrefsFilePath);
        Output = Path.Combine(project, "output", scenario);
        GuardPath(project, Output);
        Directory.CreateDirectory(Output);
        AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .UseReactiveUI(_ => { })
            .SetupWithoutStarting();
        Application.Current!.Styles.Add(new StyleInclude(new Uri("avares://OpenUtauMobile/"))
        {
            Source = new Uri("avares://OpenUtauMobile/Themes/OpenUtauMobile/OpenUtauMobileTheme.axaml")
        });
        LocalizationManager.LoadLanguage("zh-Hans");
        Console.WriteLine("ISOLATED_PREFS " + PathManager.Inst.PrefsFilePath);
    }

    private static string ProjectDirectory([CallerFilePath] string source = "")
    {
        return Path.GetDirectoryName(Path.GetFullPath(source))!;
    }

    public static void GuardPath(string root, string candidate)
    {
        string full = Path.GetFullPath(candidate);
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path outside probe project: " + full);
        }
        for (string? ancestor = full; ancestor != null; ancestor = Path.GetDirectoryName(ancestor))
        {
            if ((File.Exists(ancestor) || Directory.Exists(ancestor)) &&
                (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Linked path: " + ancestor);
            }
        }
    }

    public Window Mount(Control content, double width, double height)
    {
        Window window = new() { Content = content, Width = width, Height = height };
        windows.Add(window);
        window.Show();
        Flush(window);
        return window;
    }

    public void Track(IDisposable subscription) => subscriptions.Add(subscription);

    public static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    public static void Check(bool result, string name)
    {
        if (!result) { throw new InvalidOperationException("FAIL " + name); }
        Console.WriteLine("PASS " + name);
    }

    public static void WaitUntil(Window window, Func<bool> condition, string name, int timeoutMs = 2000)
    {
        System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            Flush(window);
            if (condition()) { Check(true, name); return; }
            Thread.Sleep(10);
        } while (timer.ElapsedMilliseconds < timeoutMs);
        throw new TimeoutException("Condition not reached: " + name);
    }

    public void Capture(Control control, string filename, double dpi = 96)
    {
        Check(control.Bounds.Width > 0 && control.Bounds.Height > 0, "capture-has-layout");
        string path = Path.Combine(Output, filename);
        GuardPath(Output, path);
        PixelSize pixels = new((int)Math.Ceiling(control.Bounds.Width * dpi / 96),
            (int)Math.Ceiling(control.Bounds.Height * dpi / 96));
        using RenderTargetBitmap bitmap = new(pixels, new Vector(dpi, dpi));
        bitmap.Render(control);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        using Bitmap reopened = new(path);
        Check(reopened.PixelSize == pixels, "png-reopened-" + filename);
        Console.WriteLine("CAPTURE " + path);
    }

    public static void Reveal(Window window, Control target)
    {
        ScrollViewer scroll = target.GetVisualAncestors().OfType<ScrollViewer>().First();
        target.BringIntoView();
        Flush(window);
        // 依据当前布局偏移完整卡片，避免只露出标题或依赖固定滚动距离。
        Point position = target.TranslatePoint(default, scroll)!.Value;
        scroll.Offset = new Vector(scroll.Offset.X, scroll.Offset.Y + position.Y);
        Flush(window);
        Point visible = target.TranslatePoint(default, scroll)!.Value;
        Check(visible.Y >= -1 && visible.Y + target.Bounds.Height <= scroll.Viewport.Height + 1,
            "target-fully-visible");
    }

    public void Dispose()
    {
        foreach (IDisposable subscription in subscriptions) { subscription.Dispose(); }
        foreach (Window window in windows) { window.Close(); }
    }
}
