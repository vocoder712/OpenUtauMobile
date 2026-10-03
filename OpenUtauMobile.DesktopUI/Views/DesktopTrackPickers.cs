using System;
using System.Collections.Generic;
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
        /// <summary>音素器列表中“最近使用”与“按语言排序”两段的分隔标记。</summary>
        private sealed record PhonemizerSeparator;
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
            list.ItemTemplate = new FuncDataTemplate<object>((item, _) =>
            {
                if (item is PhonemizerSeparator)
                {
                    // 分隔“最近使用”与按语言排序两段的细分隔线。
                    Border divider = new() { Height = 1, Margin = new Thickness(4, 5), HorizontalAlignment = HorizontalAlignment.Stretch };
                    divider.Bind(Border.BackgroundProperty, divider.GetResourceObservable("Sem.Color.OutlineVariant"));
                    return divider;
                }
                return new TextBlock { Text = (item as PhonemizerFactory)?.ToString() ?? "", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            });
            Flyout flyout = new() { Content = new StackPanel { Width = 360, Spacing = 8, Children = { search, language, list } }, Placement = PlacementMode.BottomEdgeAlignedLeft };
            void Refresh()
            {
                string query = search.Text ?? "";
                // 与移动端大面板一致：最近使用置顶（上限 10），其余按语言（及名称）排序；
                // 两组之间以一条分隔线区分。
                IOrderedEnumerable<PhonemizerFactory> ordered = factories.Where(f => (language.SelectedIndex == 0 || (f.language ?? "") == language.SelectedItem as string) &&
                    (f.name + " " + f.language + " " + f.ToString()).Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    .OrderByDescending(f => RecentRank(f))
                    .ThenBy(f => f.language)
                    .ThenBy(f => f.name);
                PhonemizerFactory[] recent = ordered.TakeWhile(f => RecentRank(f) < 10).ToArray();
                List<object> items = [.. recent];
                if (recent.Length > 0) items.Add(new PhonemizerSeparator());
                items.AddRange(ordered.Skip(recent.Length));
                list.ItemsSource = items;
            }
            static int RecentRank(PhonemizerFactory factory)
            {
                int index = Preferences.Default.RecentPhonemizers.IndexOf(factory.type.FullName ?? "");
                return index >= 0 && index < 10 ? index : int.MaxValue;
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
