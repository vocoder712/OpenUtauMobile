using System;
using System.IO;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Styling;
using OpenUtauMobile.Controls;
using OpenUtau.Core;
using OpenUtauMobile.DesktopUI.Services;
using OpenUtauMobile.DesktopUI.Views;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views;

namespace OpenUtauMobile.DesktopUI
{
    public sealed class DesktopWindow : MainWindow, ICmdSubscriber
    {
        private readonly MainViewModel _main;
        private readonly DesktopLayoutStore _layout = new();
        private readonly DesktopShell _shell;
        private bool _titleQueued;
        private bool _closed;

        public DesktopWindow(MainViewModel main)
        {
            _main = main;
            DataContext = main;
            Width = _layout.State.Width;
            Height = _layout.State.Height;
            MinWidth = 800;
            MinHeight = 500;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 36;
            if (this.TryFindResource(typeof(WindowDrawnDecorations), out object? decorations) && decorations is ControlTheme theme)
            {
                ControlTheme immersive = new(typeof(WindowDrawnDecorations)) { BasedOn = theme };
                immersive.Setters.Add(new Setter(WindowChrome.ShowTitleProperty, false));
                immersive.Setters.Add(new Setter(WindowChrome.ShowFullScreenButtonProperty, false));
                WindowDecorationsTheme = immersive;
            }
            _shell = new DesktopShell(main, _layout);
            DesktopDensity.Apply(this);
            Content = _shell;
            _layout.LayoutReset += OnLayoutReset;
            Opened += (_, _) =>
            {
                DesktopWindowProfile profile = new(_layout.State.Width, _layout.State.Height, 800, 500);
                DesktopPageLocator.RestoreGeometry(this, profile, this, _layout.State.MainWindowGeometry);
                WindowState = _layout.State.WindowState;
                DocManager.Inst.AddSubscriber(this);
                _main.PropertyChanged += OnMainChanged;
                UpdateTitle();
            };
            SizeChanged += (_, _) =>
            {
                if (IsVisible && WindowState == WindowState.Normal && Bounds.Width > 0 && Bounds.Height > 0)
                { _layout.State.Width = Bounds.Width; _layout.State.Height = Bounds.Height; }
            };
            Closed += (_, _) =>
            {
                _closed = true;
                if (WindowState == WindowState.Normal)
                {
                    _layout.State.Width = Bounds.Width;
                    _layout.State.Height = Bounds.Height;
                    _layout.State.MainWindowGeometry = DesktopPageLocator.CaptureGeometry(this);
                }
                _layout.State.WindowState = WindowState;
                _layout.Save();
                _main.PropertyChanged -= OnMainChanged;
                _layout.LayoutReset -= OnLayoutReset;
                DocManager.Inst.RemoveSubscriber(this);
                _shell.Dispose();
            };
        }
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            if (!_shell.PrepareFileAction()) { e.Cancel = true; return; }
            base.OnClosing(e);
        }
        private void OnMainChanged(object? sender, PropertyChangedEventArgs e) => UpdateTitle();
        private void OnLayoutReset()
        {
            WindowState = WindowState.Normal;
            DesktopWindowProfile profile = new(_layout.State.Width, _layout.State.Height, 800, 500);
            DesktopPageLocator.RestoreGeometry(this, profile, this, null);
            DesktopPageLocator.CenterOnOwnerScreen(this, this);
        }
        private void UpdateTitle()
        {
            string name = _main.ActiveEditor == null ? string.Empty : Path.GetFileName(DocManager.Inst.Project.FilePath);
            if (_main.ActiveEditor != null && name.Length == 0) name = OpenUtauMobile.Helpers.L.S("FilePicker.DefaultProjectName");
            Title = "OpenUtau Mobile" + (name.Length == 0 ? string.Empty : " - " + name + (DocManager.Inst.ChangesSaved ? "" : " *"));
        }
        public void OnNext(UCommand cmd, bool isUndo)
        {
            if (_closed || _titleQueued || cmd is SetPlayPosTickNotification or SeekPlayPosTickNotification or ProgressBarNotification) return;
            _titleQueued = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _titleQueued = false;
                if (!_closed) UpdateTitle();
            });
        }
    }
}
