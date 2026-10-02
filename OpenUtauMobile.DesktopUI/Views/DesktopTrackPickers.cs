using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using OpenUtau.Api;
using OpenUtau.Core.Util;
using OpenUtauMobile.Services;

namespace OpenUtauMobile.DesktopUI.Views
{
    /// <summary>轨道画布入口使用锚定的轻量选择器，取消时不改变轨道。</summary>
    internal static class DesktopTrackPickers
    {
        private static Control? Anchor => AppService.GetTopLevel()?.FocusManager?.GetFocusedElement() as Control ?? AppService.GetTopLevel();
        public static Task<string?> PickTrackNameAsync(string currentName)
        {
            if (Anchor is not { } anchor) return Task.FromResult<string?>(null);
            TaskCompletionSource<string?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TextBox input = new() { Name = "InlineTrackName", Text = currentName, Width = 280, MinHeight = 28 };
            Flyout flyout = new() { Content = input, Placement = PlacementMode.BottomEdgeAlignedLeft };
            input.KeyDown += (_, e) =>
            {
                if (OpenUtauMobile.Services.Editor.EditorInputController.IsComposing(input)) return;
                if (e.Key == Key.Escape) { e.Handled = true; flyout.Hide(); }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    if (string.IsNullOrWhiteSpace(input.Text))
                    { DataValidationErrors.SetErrors(input, new[] { OpenUtauMobile.Helpers.L.S("TrackRename.Toast.Empty") }); return; }
                    result.TrySetResult(input.Text.Trim()); flyout.Hide();
                }
            };
            input.TextChanged += (_, _) => DataValidationErrors.ClearErrors(input);
            flyout.Closed += (_, _) => result.TrySetResult(null);
            flyout.Opened += (_, _) => { input.Focus(); input.SelectAll(); };
            FlyoutBase.GetAttachedFlyout(anchor)?.Hide();
            FlyoutBase.SetAttachedFlyout(anchor, flyout); flyout.ShowAt(anchor);
            return result.Task;
        }
        public static Task<OpenUtau.Core.Ustx.USinger?> PickSingerAsync()
        {
            if (Anchor is not { } anchor) return Task.FromResult<OpenUtau.Core.Ustx.USinger?>(null);
            TaskCompletionSource<OpenUtau.Core.Ustx.USinger?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
            MenuFlyout flyout = new();
            foreach (OpenUtau.Core.Ustx.USinger singer in OpenUtau.Core.SingerManager.Inst.Singers.Values.OrderBy(s => s.Name))
            {
                MenuItem item = new() { Header = new DesktopSingerItem(singer), MinWidth = 260 };
                item.Click += (_, _) => { result.TrySetResult(singer); flyout.Hide(); };
                flyout.Items.Add(item);
            }
            flyout.Closed += (_, _) => result.TrySetResult(null);
            FlyoutBase.GetAttachedFlyout(anchor)?.Hide();
            FlyoutBase.SetAttachedFlyout(anchor, flyout);
            flyout.ShowAt(anchor);
            return result.Task;
        }
        public static Task<string?> PickRendererAsync(string[] names)
        {
            if (Anchor is not { } anchor) return Task.FromResult<string?>(null);
            TaskCompletionSource<string?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
            MenuFlyout flyout = new();
            foreach (string name in names)
            {
                MenuItem item = new() { Header = name };
                item.Click += (_, _) => { result.TrySetResult(name); flyout.Hide(); };
                flyout.Items.Add(item);
            }
            flyout.Closed += (_, _) => result.TrySetResult(null);
            FlyoutBase.GetAttachedFlyout(anchor)?.Hide();
            FlyoutBase.SetAttachedFlyout(anchor, flyout);
            flyout.ShowAt(anchor);
            return result.Task;
        }
        public static Task<Phonemizer?> PickPhonemizerAsync()
        {
            if (Anchor is not { } anchor) return Task.FromResult<Phonemizer?>(null);
            TaskCompletionSource<Phonemizer?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
            PhonemizerFactory[] factories = PhonemizerFactory.GetAll();
            TextBox search = new() { Name = "DesktopPhonemizerSearch", MinWidth = 280 };
            search.Bind(TextBox.PlaceholderTextProperty, search.GetResourceObservable("Desktop.Search"));
            ComboBox language = new() { ItemsSource = new[] { OpenUtauMobile.Helpers.L.S("Desktop.AllLanguages") }.Concat(factories.Select(f => f.language ?? "").Distinct().OrderBy(s => s)).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            ListBox list = new() { Name = "DesktopPhonemizerList", Height = 300 };
            list.ItemTemplate = new FuncDataTemplate<PhonemizerFactory>((factory, _) => new TextBlock { Text = factory?.ToString() ?? "", TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            Flyout flyout = new() { Content = new StackPanel { Width = 360, Spacing = 8, Children = { search, language, list } }, Placement = PlacementMode.BottomEdgeAlignedLeft };
            void Refresh()
            {
                string query = search.Text ?? "";
                list.ItemsSource = factories.Where(f => (language.SelectedIndex == 0 || (f.language ?? "") == language.SelectedItem as string) &&
                    (f.name + " " + f.language + " " + f.ToString()).Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    .OrderByDescending(f => Preferences.Default.RecentPhonemizers.Contains(f.type.FullName ?? ""))
                    .ThenBy(f => f.name).ToArray();
            }
            search.TextChanged += (_, _) => Refresh(); language.SelectionChanged += (_, _) => Refresh();
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is not PhonemizerFactory factory) return;
                try { result.TrySetResult(factory.Create()); }
                catch (Exception error) { result.TrySetException(error); }
                flyout.Hide();
            };
            flyout.Closed += (_, _) => result.TrySetResult(null);
            flyout.Opened += (_, _) => search.Focus();
            Refresh();
            FlyoutBase.GetAttachedFlyout(anchor)?.Hide();
            FlyoutBase.SetAttachedFlyout(anchor, flyout);
            flyout.ShowAt(anchor);
            return result.Task;
        }
    }
}
