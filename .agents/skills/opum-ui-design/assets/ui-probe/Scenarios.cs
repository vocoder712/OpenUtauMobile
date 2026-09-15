using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Newtonsoft.Json;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views;

namespace UiProbe;

internal static class Scenarios
{
    public static void Basic(ProbeHost host)
    {
        Border card = new()
        {
            Padding = new Thickness(24), CornerRadius = new CornerRadius(16),
            Child = new TextBlock { Text = "UI 探针 / UI probe" }
        };
        host.Track(card.Bind(Border.BackgroundProperty, card.GetResourceObservable("Sem.Color.SurfaceContainer")));
        Window window = host.Mount(card, 360, 180);
        foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            window.RequestedThemeVariant = theme;
            ProbeHost.Flush(window);
            host.Capture(card, "basic-" + theme + ".png");
        }
    }

    public static void Magnifier(ProbeHost host)
    {
        Canvas source = new() { Width = 600, Height = 400, Background = Brushes.WhiteSmoke };
        for (int x = 0; x < 600; x += 50)
        {
            Border line = new() { Width = 1, Height = 400, Background = Brushes.LightGray };
            Canvas.SetLeft(line, x);
            source.Children.Add(line);
        }
        source.Children.Add(new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M0,180 C70,270 95,90 145,190 C170,245 225,220 260,200 C300,170 320,180 360,170"),
            Stroke = Brushes.MediumOrchid, StrokeThickness = 2
        });
        // 独立 VisualBrush 源不在窗口布局树中，需要显式布局。
        source.Measure(new Size(600, 400));
        source.Arrange(new Rect(0, 0, 600, 400));
        MagnifierControl lens = new() { Source = source, LensSize = new Size(200, 200), CornerRadius = new CornerRadius(16) };
        host.Track(lens.Bind(MagnifierControl.BackgroundProperty, lens.GetResourceObservable("Sem.Color.SurfaceContainer")));
        host.Track(lens.Bind(MagnifierControl.BorderBrushProperty, lens.GetResourceObservable("Sem.Color.OutlineVariant")));
        host.Track(lens.Bind(MagnifierControl.ShadowColorProperty, lens.GetResourceObservable("Sem.Value.Shadow")));
        Border frame = new() { Padding = new Thickness(12), Child = lens };
        Window window = host.Mount(frame, 224, 224);
        ProbeHost.Check(!lens.IsHitTestVisible && !lens.Focusable, "lens-input-properties");
        foreach (double factor in new[] { .75, 1, 1.25, 1.5, 1.75, 2 })
        {
            lens.MagnificationFactor = factor;
            lens.UpdateView(new Point(180, 200));
            ProbeHost.Check(Math.Abs(lens.SourceRect.Width - lens.Bounds.Width / factor) < 1e-9, "sampling-" + factor);
        }
        foreach (ThemeVariant theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            window.RequestedThemeVariant = theme;
            source.Background = theme == ThemeVariant.Dark ? Brushes.Black : Brushes.WhiteSmoke;
            lens.UpdateView(new Point(180, 200));
            ProbeHost.Flush(window);
            host.Capture(frame, "magnifier-" + theme + ".png", 144);
        }
        lens.MagnificationFactor = .75;
        foreach (Point point in new[] { new Point(0, 0), new Point(600, 400) })
        {
            lens.UpdateView(point);
            ProbeHost.Check(lens.SourceRect.X >= 0 && lens.SourceRect.Y >= 0 &&
                lens.SourceRect.Right <= 600 && lens.SourceRect.Bottom <= 400, "source-boundary");
        }
        lens.Source = null;
        ProbeHost.Check(lens.Source == null, "source-detached");
    }

    public static void Settings(ProbeHost host)
    {
        // 宿主已经验证数据目录；此时才触发 Preferences 的静态初始化。
        Preferences.Default = new Preferences.SerializablePreferences { MagnifierMagnificationFactor = 1.075 };
        Preferences.Save();
        // 仅验证此页的显示与设置，不触发依赖 Navigator 的返回、弹窗或导航命令。
        using (SettingsViewModel vm = new(null!))
        {
            SettingsView view = new() { DataContext = vm };
            Window window = host.Mount(view, 850, 820);
            ProbeHost.Check(vm.MagnifierMagnificationFactor == 1.075 && vm.MagnifierSliderValue == 1,
                "continuous-value-projection");
            ProbeHost.Check(ReadFactor() == 1.075, "opening-page-preserves-disk-value");
            string name = (string)view.FindResource("Settings.Edit.MagnifierFactor")!;
            Slider slider = view.GetVisualDescendants().OfType<Slider>()
                .Single(control => AutomationProperties.GetName(control) == name);
            foreach (double factor in new[] { .75, 1, 1.25, 1.5, 1.75, 2 })
            {
                // 保留 Value 的双向绑定；这验证绑定与落盘，不模拟鼠标或触摸事件。
                slider.SetCurrentValue(Slider.ValueProperty, factor);
                ProbeHost.Flush(window);
                ProbeHost.Check(vm.MagnifierMagnificationFactor == factor && ReadFactor() == factor,
                    "binding-and-save-" + factor);
            }
            Border card = slider.GetVisualAncestors().OfType<Border>().First(control => control.Classes.Contains("SettingCard"));
            Border navigation = view.GetVisualDescendants().OfType<Border>().First(control =>
                control.Transitions?.Any(transition => transition is Avalonia.Animation.DoubleTransition tween &&
                    tween.Property == Control.WidthProperty) == true);
            foreach (double width in new[] { 850d, 430d })
            {
                window.Width = width;
                ProbeHost.Flush(window);
                ProbeHost.WaitUntil(window, () => Math.Abs(navigation.Bounds.Width - vm.NavWidth) < 1,
                    "navigation-width-settled");
                ProbeHost.Reveal(window, card);
                host.Capture(view, "settings-" + width + ".png");
            }
            window.Close();
        }
        using SettingsViewModel reopened = new(null!);
        ProbeHost.Check(reopened.MagnifierMagnificationFactor == 2 && ReadFactor() == 2, "settings-reopened");
    }

    private static double ReadFactor()
    {
        return JsonConvert.DeserializeObject<Preferences.SerializablePreferences>(
            File.ReadAllText(PathManager.Inst.PrefsFilePath))!.MagnifierMagnificationFactor;
    }
}
