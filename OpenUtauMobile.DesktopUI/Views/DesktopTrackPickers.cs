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
        public static Task<PhonemizerPickerResult?> PickPhonemizerAsync(PhonemizerPickerRequest request)
        {
            if ((request.Anchor ?? Anchor) is not { } anchor || request.CancellationToken.IsCancellationRequested)
                return Task.FromResult<PhonemizerPickerResult?>(null);
            TaskCompletionSource<PhonemizerPickerResult?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
            StackPanel body = new() { Width = 360, Spacing = 8 };
            Button inherit = new() { Name = "PhonemizerTrackDefault", Content = new TextBlock { Text = request.TrackDefaultLabel ?? OpenUtauMobile.Helpers.L.S("NoteProperties.TrackDefault"), TextWrapping = Avalonia.Media.TextWrapping.Wrap }, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (request.AllowTrackDefault) body.Children.Add(inherit);
            if (request.CurrentName != null)
                body.Children.Add(new TextBlock { Text = NotePhonemizerResolver.Display(request.CurrentName, request.TrackDefaultLabel ?? ""), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            body.Children.Add(search);
            body.Children.Add(language);
            body.Children.Add(list);
            if (factories.Length == 0)
                body.Children.Add(new TextBlock { Text = OpenUtauMobile.Helpers.L.S("Picker.Phonemizer.Empty"), TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            Flyout flyout = new() { Content = body, Placement = PlacementMode.BottomEdgeAlignedLeft };
            inherit.Click += (_, _) => { result.TrySetResult(new(null)); flyout.Hide(); };
            bool refreshing = false;
            void Refresh()
            {
                string query = search.Text ?? "";
                // 最近使用按记录顺序置顶，其余按语言与名称排序。
                IOrderedEnumerable<PhonemizerFactory> ordered = factories.Where(f => (language.SelectedIndex == 0 || (f.language ?? "") == language.SelectedItem as string) &&
                    (f.name + " " + f.language + " " + f.ToString()).Contains(query, StringComparison.CurrentCultureIgnoreCase))
                    .OrderBy(f => RecentRank(f))
                    .ThenBy(f => f.language)
                    .ThenBy(f => f.name);
                PhonemizerFactory[] recent = ordered.TakeWhile(f => RecentRank(f) < 10).ToArray();
                List<object> items = [.. recent];
                PhonemizerFactory[] remaining = ordered.Skip(recent.Length).ToArray();
                if (recent.Length > 0 && remaining.Length > 0) items.Add(new PhonemizerSeparator());
                items.AddRange(remaining);
                refreshing = true;
                try { list.ItemsSource = items; list.SelectedItem = null; }
                finally { refreshing = false; }
            }
            static int RecentRank(PhonemizerFactory factory)
            {
                int index = Preferences.Default.RecentPhonemizers.IndexOf(factory.type.FullName ?? "");
                return index >= 0 && index < 10 ? index : int.MaxValue;
            }
            search.TextChanged += (_, _) => Refresh(); language.SelectionChanged += (_, _) => Refresh();
            list.SelectionChanged += (_, _) =>
            {
                if (refreshing || list.SelectedItem is not PhonemizerFactory factory) return;
                result.TrySetResult(new(factory));
                flyout.Hide();
            };
            System.Threading.CancellationTokenRegistration cancellation = request.CancellationToken.Register(() =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { result.TrySetResult(null); flyout.Hide(); }));
            flyout.Closed += (_, _) => { cancellation.Dispose(); result.TrySetResult(null); };
            body.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.Escape && !OpenUtauMobile.Services.Editor.EditorInputController.IsComposing(e.Source as Visual))
                { e.Handled = true; flyout.Hide(); }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            flyout.Opened += (_, _) => search.Focus();
            Refresh();
            FlyoutBase.GetAttachedFlyout(anchor)?.Hide();
            FlyoutBase.SetAttachedFlyout(anchor, flyout);
            flyout.ShowAt(anchor);
            return result.Task;
        }
    }
}
