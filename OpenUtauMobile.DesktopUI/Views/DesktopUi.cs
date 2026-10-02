using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using System.Threading.Tasks;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal static class DesktopUi
    {
        public static TextBlock Label(string key)
        {
            TextBlock text = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            text.Bind(TextBlock.TextProperty, text.GetResourceObservable(key));
            return text;
        }
        public static Button Action(string key, Action action)
        {
            Button button = new() { Content = Label(key) };
            button.Classes.Add("DesktopAction");
            button.Click += (_, _) =>
            {
                try { action(); }
                catch (Exception e) { ErrorDialogService.Show(new ErrorDialogViewModel(new OpenUtau.Core.ErrorMessageNotification(e))); }
            };
            return button;
        }
        public static Button Action(string key, Func<Task> action)
        {
            Button button = new() { Content = Label(key) };
            button.Classes.Add("DesktopAction");
            button.Click += async (_, _) =>
            {
                if (!button.IsEnabled) return;
                button.IsEnabled = false;
                try { await action(); }
                catch (Exception e) { ErrorDialogService.Show(new ErrorDialogViewModel(new OpenUtau.Core.ErrorMessageNotification(e))); }
                finally { button.IsEnabled = true; }
            };
            return button;
        }
        public static Button Command(string key, string path)
        {
            Button button = new() { Content = Label(key) };
            button.Classes.Add("DesktopAction");
            button.Bind(Button.CommandProperty, new Binding(path));
            return button;
        }
        public static Border Panel(Control child, bool inset = false)
        {
            Border border = new() { Child = child, Padding = inset ? new Thickness(12) : default };
            border.Classes.Add("DesktopPanel");
            return border;
        }
        public static ScrollViewer Scroll(Control child, bool inset = true) => new()
        {
            Content = new Border { Child = child, Padding = inset ? new Thickness(12) : default },
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
        public static void Paint(Control control, AvaloniaProperty property, string key) => control.Bind(property, control.GetResourceObservable(key));
    }
}
