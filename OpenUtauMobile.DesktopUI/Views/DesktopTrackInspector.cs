using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReactiveUI;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using OpenUtauMobile.DesktopUI.Services;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.Editor;
using OpenUtauMobile.Services;
using OpenUtauMobile.Controls;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopTrackInspector : UserControl, IDisposable
    {
        private readonly EditorViewModel _editor;
        private readonly Action _queueRefresh;
        private TrackHeaderViewModel? _trackVm;
        private UTrack? _track;
        private string? _renderSettingsKey;

        public DesktopTrackInspector(EditorViewModel editor, Action queueRefresh)
        {
            _editor = editor;
            _queueRefresh = queueRefresh;
        }

        public void Invalidate()
        {
            _track = null;
            _renderSettingsKey = null;
        }

        public void RefreshMix() => _trackVm?.RefreshMix();
        public void Refresh()
        {
            UProject project = DocManager.Inst.Project;
            int index = _editor.PianoRollViewModel.EditingVoicePart?.trackNo ?? _editor.PianoRollViewModel.EditingWavePart?.trackNo ?? _editor.SelectedParts.LastOrDefault()?.trackNo ?? 0;
            UTrack? track = index >= 0 && index < project.tracks.Count ? project.tracks[index] : null;
            string? renderKey = track == null ? null : $"{track.Singer?.Id ?? track.Singer?.Name}|{track.RendererSettings.renderer}|{track.RendererSettings.resampler}|{track.RendererSettings.wavtool}";
            if (_track == track && _trackVm != null && _renderSettingsKey == renderKey) { _trackVm.Refresh(); return; }
            _renderSettingsKey = renderKey;
            _trackVm?.Dispose(); _trackVm = null; _track = track;
            if (track == null) { Content = DesktopUi.Label("Desktop.NoTrack"); return; }
            _trackVm = new TrackHeaderViewModel(track);
            StackPanel body = new() { Spacing = 6, DataContext = _trackVm };
            TrackHeaderViewModel nameVm = _trackVm;
            TextBox name = new() { Name = "TrackNameInput", HorizontalAlignment = HorizontalAlignment.Stretch };
            name.Bind(TextBox.TextProperty, new Binding("TrackName") { Mode = BindingMode.OneWay });
            void CommitName()
            {
                if (nameVm != _trackVm || _track != track) return;
                if (string.IsNullOrWhiteSpace(name.Text))
                {
                    DataValidationErrors.SetErrors(name, new[] { L.S("TrackRename.Toast.Empty") });
                    return;
                }
                DataValidationErrors.ClearErrors(name);
                nameVm.SetTrackName(name.Text);
            }
            name.LostFocus += (_, _) => CommitName();
            name.KeyDown += (_, e) =>
            {
                if (EditorInputController.IsComposing(name)) return;
                if (e.Key == Key.Enter) { CommitName(); e.Handled = true; }
                else if (e.Key == Key.Escape) { name.Text = track.TrackName; DataValidationErrors.ClearErrors(name); e.Handled = true; }
            };
            ToolTip.SetTip(name, DesktopUi.Label("Picker.TrackRename.Title"));
            Grid title = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
            title.Children.Add(name);
            Button color = DesktopUi.Command("Desktop.Color", "ChangeColorCommand");
            color.Content = null; color.Width = 24; color.Height = 24; color.MinHeight = 24; color.Padding = default;
            color.Bind(Button.BackgroundProperty, new Binding("TrackColorBrush"));
            ToolTip.SetTip(color, DesktopUi.Label("Desktop.Color"));
            Grid.SetColumn(color, 1); title.Children.Add(color); body.Children.Add(title);
            TrackHeaderViewModel singerVm = _trackVm;
            Button singer = DesktopTrackPickers.CreateSingerButton(track.Singer);
            singer.Classes.Add("DesktopTrackChoice");
            singer.Click += async (_, _) =>
            {
                USinger? chosen = await DesktopTrackPickers.PickSingerAsync(track.Singer, singer);
                if (chosen == null || singerVm != _trackVm || _track != track) return;
                singerVm.SetSinger(chosen);
            };
            ToolTip.SetTip(singer, DesktopUi.Label("TrackSettings.Singer"));
            body.Children.Add(singer);
            Button phonemizer = DesktopTrackPickers.CreatePhonemizerButton("TrackPhonemizerPicker", "PhonemizerTag");
            phonemizer.Classes.Add("DesktopTrackChoice");
            phonemizer.Bind(Button.CommandProperty, new Binding("SelectPhonemizerCommand"));
            phonemizer.CommandParameter = phonemizer;
            body.Children.Add(DesktopInspectorControls.FieldRow(DesktopUi.Label("TrackSettings.Phonemizer"), phonemizer));
            TrackHeaderViewModel rendererVm = _trackVm;
            string[] renderers = track.Singer is { Found: true } singerModel ? Renderers.GetSupportedRenderers(singerModel.SingerType) : [];
            ComboBox renderer = new() { Name = "TrackRendererPicker", ItemsSource = renderers, SelectedItem = track.RendererSettings.renderer, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = renderers.Length > 0 };
            renderer.Classes.Add("DesktopTrackChoice");
            renderer.SelectionChanged += (_, _) =>
            {
                if (rendererVm == _trackVm && renderer.SelectedItem is string selected) rendererVm.SetRenderer(selected);
            };
            body.Children.Add(DesktopInspectorControls.FieldRow(DesktopUi.Label("TrackSettings.Renderer"), renderer));
            if (track.RendererSettings.renderer == Renderers.CLASSIC)
            {
                body.Children.Add(ToolPicker(track, DesktopToolKind.Resampler));
                body.Children.Add(ToolPicker(track, DesktopToolKind.Wavtool));
            }
            TrackHeaderViewModel capturedMix = _trackVm;
            foreach (bool volume in new[] { true, false })
                body.Children.Add(new DesktopMixField(track, capturedMix, volume,
                    () => capturedMix == _trackVm && _track == track && DocManager.Inst.Project.tracks.Contains(track)));
            Grid mixActions = new() { Name = "TrackMixActions", ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
            ToggleButton mute = new() { Name = "InspectorMuteToggle", Content = DesktopUi.Label("Mixer.Mute") }, solo = new() { Name = "InspectorSoloToggle", Content = DesktopUi.Label("Mixer.Solo") };
            mute.Bind(Button.CommandProperty, new Binding("MuteCommand")); solo.Bind(Button.CommandProperty, new Binding("SoloCommand"));
            mute.Bind(ToggleButton.IsCheckedProperty, new Binding("Mute") { Mode = BindingMode.OneWay });
            solo.Bind(ToggleButton.IsCheckedProperty, new Binding("Solo") { Mode = BindingMode.OneWay });
            mute.HorizontalAlignment = solo.HorizontalAlignment = HorizontalAlignment.Stretch;
            Grid.SetColumn(solo, 1); mixActions.Children.Add(mute); mixActions.Children.Add(solo);
            body.Children.Add(mixActions);
            Content = body;
        }
        private Control ToolPicker(UTrack track, DesktopToolKind kind)
        {
            StackPanel body = new() { Spacing = 4 };
            TextBlock label = DesktopUi.Label(kind == DesktopToolKind.Resampler ? "Desktop.Resamplers" : "Desktop.Wavtools");
            DesktopTool[] tools = DesktopTrackToolActions.GetCompatibleTools(track, kind);
            string? current = kind == DesktopToolKind.Resampler ? track.RendererSettings.resampler : track.RendererSettings.wavtool;
            ComboBox picker = new() { ItemsSource = tools, SelectedItem = tools.FirstOrDefault(t => t.Name == current), HorizontalAlignment = HorizontalAlignment.Stretch };
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is not DesktopTool tool || !DesktopTrackToolActions.TryApply(track, tool)) return;
                Invalidate(); _queueRefresh();
            };
            Grid toolRow = new() { ColumnDefinitions = new ColumnDefinitions("100,*,28"), ColumnSpacing = 4 };
            toolRow.Children.Add(label); Grid.SetColumn(picker, 1); toolRow.Children.Add(picker);
            body.Children.Add(toolRow);
            if (!string.IsNullOrEmpty(current) && picker.SelectedItem == null)
                body.Children.Add(new TextBlock { Text = L.S("Desktop.Unavailable") + ": " + current, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            Button useDefault = DesktopUi.Action("Desktop.UseDefault", () =>
            {
                if (picker.SelectedItem is not DesktopTool tool) return;
                (kind == DesktopToolKind.Resampler ? Preferences.Default.DefaultResamplers : Preferences.Default.DefaultWavtools)[Renderers.CLASSIC] = tool.Name;
                Preferences.Save();
            });
            useDefault.Content = new PackIconPhosphorIcons { Kind = PackIconPhosphorIconsKind.Star, Width = 16, Height = 16 };
            useDefault.Padding = default; ToolTip.SetTip(useDefault, DesktopUi.Label("Desktop.UseDefault"));
            Grid.SetColumn(useDefault, 2); toolRow.Children.Add(useDefault);
            return body;
        }
        public void Dispose() => _trackVm?.Dispose();
    }

    internal static class DesktopInspectorControls
    {
        public static Grid FieldRow(Control label, Control input)
        {
            Grid row = new() { ColumnDefinitions = new ColumnDefinitions("100,*"), ColumnSpacing = 6 };
            row.Children.Add(label); Grid.SetColumn(input, 1); row.Children.Add(input);
            return row;
        }
    }
}
