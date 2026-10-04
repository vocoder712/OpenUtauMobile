using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Controls;
using OpenUtauMobile.DesktopUI.Services;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Services.Editor;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopMixerWindow : IDisposable
    {
        private readonly EditorViewModel _editor;
        private readonly DesktopLayoutStore _layout;
        private readonly Func<bool> _isWorkspaceDisposed;
        private Window? _mixerWindow;

        public DesktopMixerWindow(EditorViewModel editor, DesktopLayoutStore layout, Func<bool> isWorkspaceDisposed)
        {
            _editor = editor;
            _layout = layout;
            _isWorkspaceDisposed = isWorkspaceDisposed;
        }
        public void Toggle(Control ownerControl)
        {
            if (_mixerWindow != null) { _mixerWindow.Close(); return; }
            if (TopLevel.GetTopLevel(ownerControl) is not Window owner) return;
            MixerPanel mixer = new();
            DesktopDensity.ApplyMixer(mixer, _layout.State.MixerFxPaneWidth);
            mixer.Styles.Add(new StyleInclude(new Uri("avares://OpenUtauMobile.DesktopUI/"))
            { Source = new Uri("avares://OpenUtauMobile.DesktopUI/Views/DesktopStyles.axaml") });
            mixer.FindControl<Grid>("MixerHeader")!.IsVisible = false;
            Border detailBorder = mixer.FindControl<Border>("DetailBorder")!;
            DispatcherTimer paneSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
            paneSaveTimer.Tick += (_, _) => { paneSaveTimer.Stop(); if (!_isWorkspaceDisposed()) _layout.Save(); };
            detailBorder.SizeChanged += (_, e) =>
            {
                if (e.NewSize.Width < 220 || Math.Abs(_layout.State.MixerFxPaneWidth - e.NewSize.Width) < 1) return;
                _layout.State.MixerFxPaneWidth = e.NewSize.Width;
                paneSaveTimer.Stop();
                paneSaveTimer.Start();
            };
            DesktopWindowProfile profile = DesktopWindowProfile.Mixer;
            Size available = owner.Screens.ScreenFromWindow(owner) is { } screen
                ? new Size(screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling)
                : owner.ClientSize;
            double width = Math.Min(profile.Width, Math.Max(1, available.Width - 24));
            double height = Math.Min(profile.Height, Math.Max(1, available.Height - 24));
            Window window = new() { Width = width, Height = height, MinWidth = Math.Min(profile.MinWidth, width), MinHeight = Math.Min(profile.MinHeight, height), WindowStartupLocation = WindowStartupLocation.CenterScreen };
            DesktopPageLocator.RestoreGeometry(window, profile, owner, _layout.State.MixerWindow);
            window.Bind(Window.TitleProperty, window.GetResourceObservable("Mixer.Title"));
            DesktopUi.Paint(window, Window.BackgroundProperty, "Sem.Color.Surface");
            DialogHostAvalonia.DialogHost host = new() { Identifier = "DesktopMixer-" + Guid.NewGuid(), Content = mixer, IsMultipleDialogsEnabled = true, IsEnabled = !PianoRollViewModel.IsBatchEditRunning };
            window.Content = host;
            _mixerWindow = window;
            window.Closing += (_, e) =>
            {
                if (window.WindowState == WindowState.Normal)
                {
                    _layout.State.MixerWindow = DesktopPageLocator.CaptureGeometry(window);
                    _layout.Save();
                }
                if (!host.IsOpen) return;
                if (_isWorkspaceDisposed()) { DialogHostAvalonia.DialogHost.Close(host.Identifier, null); return; }
                e.Cancel = true;
                object? content = DialogHostAvalonia.DialogHost.GetDialogSession(host.Identifier)?.Content;
                (content is ContentControl control ? control.DataContext as IPopupContext : content as IPopupContext)?.RequestBack();
            };
            HashSet<Key> pressed = [];
            window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                Visual? source = window.FocusManager?.GetFocusedElement() as Visual ?? e.Source as Visual;
                if (e.Handled || host.IsOpen || PianoRollViewModel.IsBatchEditRunning || EditorInputController.IsComposing(source) || EditorShortcuts.IsTextInput(source)) return;
                bool command = EditorShortcuts.IsCommandModifier(e.KeyModifiers, OperatingSystem.IsMacOS());
                if (e.Key == Key.Escape || command && e.Key == Key.W) { e.Handled = true; window.Close(); return; }
                ICommand? action = EditorShortcuts.GetAction(_editor, e.Key, e.KeyModifiers, OperatingSystem.IsMacOS());
                if (action == null) return;
                e.Handled = true;
                if (pressed.Add(e.Key) && !DocManager.Inst.HasOpenUndoGroup && action.CanExecute(null)) action.Execute(null);
            }, RoutingStrategies.Tunnel);
            window.KeyUp += (_, e) => pressed.Remove(e.Key);
            window.Deactivated += (_, _) => pressed.Clear();
            window.Closed += (_, _) => { paneSaveTimer.Stop(); window.Content = null; _mixerWindow = null; };
            window.Show(owner);
        }
        public void SetBatchEditRunning(bool running)
        {
            if (_mixerWindow?.Content is DialogHostAvalonia.DialogHost host) host.IsEnabled = !running;
        }

        public void ResetWindowGeometry(Control ownerControl)
        {
            if (_mixerWindow is not { IsVisible: true } mixer || TopLevel.GetTopLevel(ownerControl) is not Window owner) return;
            mixer.GetVisualDescendants().OfType<MixerPanel>().FirstOrDefault()?.ResetDesktopFxPaneWidth(288);
            DesktopWindowProfile profile = DesktopWindowProfile.Mixer;
            Size available = owner.Screens.ScreenFromWindow(owner) is { } screen
                ? new Size(screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling)
                : owner.ClientSize;
            mixer.Width = Math.Min(profile.Width, Math.Max(1, available.Width - 24));
            mixer.Height = Math.Min(profile.Height, Math.Max(1, available.Height - 24));
            DesktopPageLocator.CenterOnOwnerScreen(mixer, owner);
        }

        public void Dispose() => _mixerWindow?.Close();
    }
}
