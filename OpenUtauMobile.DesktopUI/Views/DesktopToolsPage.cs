using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using OpenUtauMobile.DesktopUI.Services;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopToolsPage : UserControl
    {
        public DesktopToolsPage(DesktopToolsViewModel tools)
        {
            DataContext = tools;
            Grid root = new() { RowDefinitions = new RowDefinitions("Auto,8,*,16,Auto") };
            List<ListBox> lists = [];
            ListBox? resamplersList = null;
            ListBox? wavtoolsList = null;
            WrapPanel actions = new() { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8 };
            actions.Children.Add(DesktopUi.Command("Desktop.InstallFile", "InstallFileCommand"));
            actions.Children.Add(DesktopUi.Command("Desktop.InstallFolder", "InstallFolderCommand"));
            actions.Children.Add(DesktopUi.Command("Common.Refresh", "RefreshCommand"));
            Button uninstall = DesktopUi.Command("Common.Uninstall", "UninstallCommand");
            uninstall.Bind(IsEnabledProperty, new Binding("CanUninstall"));
            actions.Children.Add(uninstall);
            actions.Children.Add(DesktopUi.Action("Desktop.OpenFolder", () =>
            {
                string folder = DesktopToolsService.Folder(tools.Kind); Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }));
            actions.Bind(IsEnabledProperty, new Binding("!Busy"));
            root.Children.Add(actions);
            TabControl tabs = new();
            ListBox CreateList(string path)
            {
                ListBox list = new(); list.Bind(ItemsControl.ItemsSourceProperty, new Binding(path));
                list.ItemTemplate = new FuncDataTemplate<DesktopTool>((tool, _) =>
                {
                    if (tool == null) return new TextBlock();
                    TextBlock status = tool.StatusKey == null ? new TextBlock { Text = tool.Status } : DesktopUi.Label(tool.StatusKey);
                    status.Classes.Add(tool.StatusKey switch
                    {
                        "Desktop.Unavailable" or "Desktop.NotExecutable" or "Desktop.NativeWavtoolUnavailable" => "DesktopToolUnavailable",
                        "Desktop.ExperimentalWine" => "DesktopToolWarning",
                        _ => "DesktopMetadata",
                    });
                    TextBlock path = new() { Text = tool.FilePath ?? string.Empty, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
                    path.Classes.Add("DesktopMetadata");
                    ToolTip.SetTip(path, tool.FilePath);
                    return new StackPanel { Spacing = 2, MinHeight = 48, VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = tool.Name, FontWeight = Avalonia.Media.FontWeight.SemiBold, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis }, status, path } };
                });
                list.SelectionChanged += (_, _) => tools.Selected = list.SelectedItem as DesktopTool;
                lists.Add(list);
                return list;
            }
            resamplersList = CreateList("Resamplers");
            wavtoolsList = CreateList("Wavtools");
            tabs.ItemsSource = new[] { new TabItem { Header = DesktopUi.Label("Desktop.Resamplers"), Content = resamplersList }, new TabItem { Header = DesktopUi.Label("Desktop.Wavtools"), Content = wavtoolsList } };
            tabs.SelectionChanged += (_, e) =>
            {
                if (e.Source != tabs) return;
                tools.Kind = tabs.SelectedIndex == 1 ? DesktopToolKind.Wavtool : DesktopToolKind.Resampler;
                foreach (ListBox list in lists) list.SelectedItem = null;
                tools.Selected = tabs.SelectedIndex == 1 ? wavtoolsList?.SelectedItem as DesktopTool : resamplersList?.SelectedItem as DesktopTool;
            };
            Border toolPanel = DesktopUi.Panel(tabs);
            toolPanel.Name = "ToolsListPanel";
            Grid.SetRow(toolPanel, 2);
            root.Children.Add(toolPanel);
            WrapPanel wine = new() { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, IsVisible = !OperatingSystem.IsWindows() };
            wine.Children.Add(DesktopUi.Label("Desktop.WinePath"));
            TextBox pathBox = new() { Width = 360 }; pathBox.Bind(TextBox.TextProperty, new Binding("WinePath") { Mode = BindingMode.TwoWay }); wine.Children.Add(pathBox);
            wine.Children.Add(DesktopUi.Command("Common.Apply", "WineCommand"));
            StackPanel status = new() { Name = "ToolsFooter", Spacing = 8 };
            status.Children.Add(wine);
            TextBlock compatibility = DesktopUi.Label("Desktop.ToolsCompatibility");
            compatibility.Name = "ToolsCompatibility";
            compatibility.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
            status.Children.Add(compatibility);
            TextBlock message = new() { Name = "ToolsStatusMessage", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            message.Classes.Add("ToolsStatusMessage");
            message.Bind(TextBlock.TextProperty, new Binding("Status"));
            message.Bind(IsVisibleProperty, new Binding("Status") { Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty });
            void UpdateStatusKind()
            {
                message.Classes.Remove("status-info");
                message.Classes.Remove("status-success");
                message.Classes.Remove("status-warning");
                message.Classes.Remove("status-error");
                message.Classes.Add(tools.StatusKind switch
                {
                    DesktopToolStatusKind.Success => "status-success",
                    DesktopToolStatusKind.Warning => "status-warning",
                    DesktopToolStatusKind.Error => "status-error",
                    _ => "status-info",
                });
            }
            PropertyChangedEventHandler statusChanged = (_, e) =>
            {
                if (e.PropertyName == nameof(tools.StatusKind)) UpdateStatusKind();
            };
            AttachedToVisualTree += (_, _) => { tools.PropertyChanged += statusChanged; UpdateStatusKind(); };
            DetachedFromVisualTree += (_, _) => tools.PropertyChanged -= statusChanged;
            UpdateStatusKind();
            status.Children.Add(message);
            Button cancel = DesktopUi.Command("Common.Cancel", "CancelCommand");
            cancel.Bind(IsVisibleProperty, new Binding("Busy"));
            status.Children.Add(cancel); Grid.SetRow(status, 4); root.Children.Add(status);
            Content = root;
        }
    }
}
