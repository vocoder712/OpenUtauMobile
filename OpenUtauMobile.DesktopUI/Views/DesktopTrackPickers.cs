using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
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
        private sealed record PhonemizerTrackDefault;
        private static Control? Anchor => AppService.GetTopLevel()?.FocusManager?.GetFocusedElement() as Control ?? AppService.GetTopLevel();
        public static Button CreatePhonemizerButton(string name, string valueBinding, string? detailBinding = null)
        {
            TextBlock label = new() { TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            label.Bind(TextBlock.TextProperty, new Binding(valueBinding));
            Button button = new() { Name = name, Content = CreatePickerContent(label), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            button.Classes.Add("DesktopPicker");
            button.Bind(ToolTip.TipProperty, new Binding(detailBinding ?? valueBinding));
            return button;
        }
        private static Grid CreatePickerContent(Control label)
        {
            Grid content = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            content.Children.Add(label);
            IconPacks.Avalonia.PhosphorIcons.PackIconPhosphorIcons chevron = new()
            {
                Kind = IconPacks.Avalonia.PhosphorIcons.PackIconPhosphorIconsKind.CaretDown,
                Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(chevron, 1);
            content.Children.Add(chevron);
            return content;
        }
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
            return PickSingerAsync(null, anchor);
        }
        public static Task<OpenUtau.Core.Ustx.USinger?> PickSingerAsync(OpenUtau.Core.Ustx.USinger? currentSinger) => PickSingerAsync(currentSinger, null);
        public static Button CreateSingerButton(OpenUtau.Core.Ustx.USinger? singer)
        {
            Button button = new() { Name = "TrackSingerPicker", Content = CreatePickerContent(new DesktopSingerItem(singer, 56)), MinHeight = 72, Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            button.Classes.Add("DesktopPicker");
            ToolTip.SetTip(button, OpenUtauMobile.Helpers.L.S("TrackSettings.Singer"));
            return button;
        }
        private static StackPanel CreatePickerBody(double width)
        {
            StackPanel body = new() { Width = width, Spacing = 8 };
            DesktopDensity.Apply(body);
            body.Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://OpenUtauMobile.DesktopUI/"))
            {
                Source = new Uri("avares://OpenUtauMobile.DesktopUI/Views/DesktopStyles.axaml")
            });
            return body;
        }
        public static Task<OpenUtau.Core.Ustx.USinger?> PickSingerAsync(OpenUtau.Core.Ustx.USinger? currentSinger, Control? anchor = null)
        {
            anchor ??= Anchor;
            if (anchor == null) return Task.FromResult<OpenUtau.Core.Ustx.USinger?>(null);
            TaskCompletionSource<OpenUtau.Core.Ustx.USinger?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
            OpenUtau.Core.Ustx.USinger[] singers = OpenUtau.Core.SingerManager.Inst.Singers.Values
                .Concat(currentSinger == null ? Array.Empty<OpenUtau.Core.Ustx.USinger>() : new[] { currentSinger })
                .DistinctBy(singer => singer.Id ?? singer.Name)
                .OrderBy(singer => singer.LocalizedName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            TextBox search = new() { Name = "SingerPickerSearch", MinHeight = 30, PlaceholderText = OpenUtauMobile.Helpers.L.S("Desktop.Search") };
            ListBox list = new() { Name = "SingerPickerList", MaxHeight = 320, ItemTemplate = new FuncDataTemplate<OpenUtau.Core.Ustx.USinger>((singer, _) => singer == null ? null : new DesktopSingerItem(singer, 56)) };
            list.Classes.Add("DesktopSingerList");
            TextBlock empty = new() { Text = OpenUtauMobile.Helpers.L.S("SingerManagement.NoResults"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
            StackPanel body = CreatePickerBody(320);
            body.Children.Add(search); body.Children.Add(list); body.Children.Add(empty);
            Flyout flyout = new()
            {
                Content = body, Placement = PlacementMode.BottomEdgeAlignedLeft,
                FlyoutPresenterTheme = (Avalonia.Styling.ControlTheme)body.FindResource("DesktopTrackPickerFlyoutTheme")!
            };
            bool refreshing = false;
            bool keyboardNavigation = false;
            void Refresh()
            {
                string query = search.Text?.Trim() ?? string.Empty;
                OpenUtau.Core.Ustx.USinger[] filtered = singers.Where(singer =>
                    (singer.LocalizedName + " " + singer.Id + " " + singer.Author).Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
                refreshing = true;
                try
                {
                    list.ItemsSource = filtered;
                    list.SelectedItem = currentSinger != null && filtered.Contains(currentSinger) ? currentSinger : null;
                    list.IsVisible = filtered.Length > 0;
                    empty.IsVisible = filtered.Length == 0;
                }
                finally { refreshing = false; }
            }
            void AcceptSelected()
            {
                if (list.SelectedItem is not OpenUtau.Core.Ustx.USinger selected) return;
                result.TrySetResult(selected);
                flyout.Hide();
            }
            search.TextChanged += (_, _) => Refresh();
            list.SelectionChanged += (_, _) => { if (!refreshing && !keyboardNavigation) AcceptSelected(); };
            list.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                keyboardNavigation = false;
                if (e.GetCurrentPoint(list).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed) list.SelectedItem = null;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            list.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (OpenUtauMobile.Services.Editor.EditorInputController.IsComposing(e.Source as Visual)) return;
                if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End) keyboardNavigation = true;
                else if (e.Key == Key.Enter) { e.Handled = true; AcceptSelected(); }
                else if (e.Key == Key.Escape) { e.Handled = true; flyout.Hide(); }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            flyout.Closed += (_, _) => result.TrySetResult(null);
            flyout.Opened += (_, _) => search.Focus();
            Refresh();
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
            if (request.Anchor is not { } anchor || TopLevel.GetTopLevel(anchor) == null || request.CancellationToken.IsCancellationRequested)
                return Task.FromResult<PhonemizerPickerResult?>(null);
            TaskCompletionSource<PhonemizerPickerResult?> result = new(TaskCreationOptions.RunContinuationsAsynchronously);
            PhonemizerFactory[] factories = PhonemizerFactory.GetAll();
            TextBox search = new() { Name = "DesktopPhonemizerSearch", MinWidth = 280 };
            search.Bind(TextBox.PlaceholderTextProperty, search.GetResourceObservable("Desktop.Search"));
            ComboBox language = new() { ItemsSource = new[] { OpenUtauMobile.Helpers.L.S("Desktop.AllLanguages") }.Concat(factories.Select(f => f.language ?? "").Distinct().OrderBy(s => s)).ToArray(), SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            ListBox list = new() { Name = "DesktopPhonemizerList", MaxHeight = 300 };
            list.Classes.Add("DesktopPhonemizerList");
            list.ContainerPrepared += (_, e) =>
            {
                bool separator = list.Items[e.Index] is PhonemizerSeparator;
                e.Container.Classes.Set("PhonemizerSeparator", separator);
                e.Container.IsEnabled = !separator;
            };
            list.ItemTemplate = new FuncDataTemplate<object>((item, _) =>
            {
                if (item is PhonemizerSeparator)
                {
                    // 分隔“最近使用”与按语言排序两段的细分隔线。
                    Border divider = new() { Height = 1, Margin = new Thickness(4, 5), HorizontalAlignment = HorizontalAlignment.Stretch };
                    divider.Bind(Border.BackgroundProperty, divider.GetResourceObservable("Sem.Color.OutlineVariant"));
                    return divider;
                }
                TextBlock label = new()
                {
                    Text = item is PhonemizerTrackDefault ? OpenUtauMobile.Helpers.L.S("NoteProperties.TrackDefault") : (item as PhonemizerFactory)?.ToString() ?? "",
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                ToolTip.SetTip(label, item is PhonemizerTrackDefault ? request.TrackDefaultLabel : label.Text);
                return label;
            });
            StackPanel body = CreatePickerBody(360);
            body.Children.Add(search);
            body.Children.Add(language);
            body.Children.Add(list);
            Flyout flyout = new()
            {
                Content = body,
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                FlyoutPresenterTheme = (Avalonia.Styling.ControlTheme)body.FindResource("DesktopTrackPickerFlyoutTheme")!
            };
            bool refreshing = false;
            bool keyboardNavigation = false;
            TextBlock? noResults = null;
            void AcceptSelected()
            {
                if (list.SelectedItem is PhonemizerFactory factory) result.TrySetResult(new(factory));
                else if (list.SelectedItem is PhonemizerTrackDefault) result.TrySetResult(new(null));
                else return;
                flyout.Hide();
            }
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
                List<object> items = [];
                if (request.AllowTrackDefault)
                {
                    items.Add(new PhonemizerTrackDefault());
                    if (ordered.Any()) items.Add(new PhonemizerSeparator());
                }
                items.AddRange(recent);
                PhonemizerFactory[] remaining = ordered.Skip(recent.Length).ToArray();
                if (recent.Length > 0 && remaining.Length > 0) items.Add(new PhonemizerSeparator());
                items.AddRange(remaining);
                refreshing = true;
                try
                {
                    list.ItemsSource = items;
                    list.SelectedItem = items.OfType<PhonemizerFactory>().FirstOrDefault(factory => factory.name == request.CurrentName || factory.type.FullName == request.CurrentName);
                    bool hasFactories = items.OfType<PhonemizerFactory>().Any();
                    bool hasChoices = items.Any(item => item is PhonemizerFactory or PhonemizerTrackDefault);
                    list.IsVisible = hasChoices;
                    if (noResults != null) noResults.IsVisible = !hasFactories;
                }
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
                if (refreshing || keyboardNavigation) return;
                AcceptSelected();
            };
            list.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                keyboardNavigation = false;
                if (e.GetCurrentPoint(list).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed) list.SelectedItem = null;
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            list.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (OpenUtauMobile.Services.Editor.EditorInputController.IsComposing(e.Source as Visual)) return;
                if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
                {
                    keyboardNavigation = true;
                }
                else if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    AcceptSelected();
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            noResults = new TextBlock { Text = OpenUtauMobile.Helpers.L.S("Picker.Phonemizer.Empty"), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            noResults.IsVisible = factories.Length == 0;
            body.Children.Add(noResults);
            System.Threading.CancellationTokenRegistration cancellation = request.CancellationToken.Register(() =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => { result.TrySetResult(null); flyout.Hide(); }));
            void OnAnchorDetached(object? sender, VisualTreeAttachmentEventArgs e) => flyout.Hide();
            anchor.DetachedFromVisualTree += OnAnchorDetached;
            flyout.Closed += (_, _) =>
            {
                anchor.DetachedFromVisualTree -= OnAnchorDetached;
                cancellation.Dispose();
                result.TrySetResult(null);
            };
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
