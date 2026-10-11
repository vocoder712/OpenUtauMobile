using System;
using System.Threading.Tasks;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogHostAvalonia;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Platform;
using OpenUtauMobile.Services.Editor;
using OpenUtauMobile.DesktopUI.Services;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopPageLocator(MainViewModel main, DesktopLayoutStore layout, Func<Window?> owner,
        Action<string, double> showMessage) : IDataTemplate, IDisposable, IDesktopWindowContext
    {
        private EditorViewModel? _editor;
        private Window? _utility;
        private NavigateViewModelBase? _utilityVm;
        private string? _utilityKey;
        private bool _closing;
        private bool _disposed;
        private Control? _home;
        private HomeViewModel? _homeModel;
        private readonly string _dialogId = "DesktopUtility-" + Guid.NewGuid();
        private DesktopPopupPresenter? _popups;
        private bool _layoutResetSubscribed;
        public event Action<DesktopWorkspace?>? WorkspaceChanged;
        public bool IsUtilityModalOpen => _popups?.IsOpen == true;
        public bool IsModalOpen => IsUtilityModalOpen;
        public TopLevel? ActiveTopLevel => _popups?.ActiveWindow ?? ActiveWindow();
        public string? DialogHostIdentifier => ActiveWindow()?.Content is DialogHost host ? host.Identifier?.ToString() : "MainDialogHost";
        public Task<object?> ShowPopupAsync(Control view, PopupViewModelBase model) => _popups!.ShowAsync(view, model);
        public void ShowMessage(string message, double durationMilliseconds)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => ShowMessage(message, durationMilliseconds)); return; }
            if (_disposed) return;
            showMessage(message, durationMilliseconds);
        }
        public void InitializeWindowProviders() => RegisterWindowProviders();
        public IControlTemplate? PopupTemplate { get; set; }
        public DesktopWorkspace? Workspace { get; private set; }

        public Control? Build(object? data)
        {
            if (_disposed) return null;
            if (data is EditorViewModel editor)
            {
                RegisterWindowProviders();
                CloseUtility();
                if (_editor != editor)
                {
                    Workspace?.Dispose();
                    _editor = editor;
                    Workspace = new DesktopWorkspace(editor, layout);
                    WorkspaceChanged?.Invoke(Workspace);
                }
                return Workspace;
            }
            if (main.ActiveEditor == null && Workspace != null)
            {
                Workspace.Dispose();
                Workspace = null;
                _editor = null;
                WorkspaceChanged?.Invoke(null);
            }
            if (data is NavigateViewModelBase vm && data is not SplashScreenViewModel && (main.ActiveEditor != null || data is not HomeViewModel))
            {
                Dispatcher.UIThread.Post(() => ShowUtility(vm));
                if (Workspace != null) return Workspace;
                return _home;
            }
            CloseUtility();
            if (data is HomeViewModel home)
            {
                if (_homeModel != home)
                {
                    _homeModel = home;
                    _home = BuildPage(home);
                }
                return _home;
            }
            return BuildPage(data);
        }
        private Control? BuildPage(object? data) => DesktopPresentationCatalog.BuildPage(data, main, layout);
        public bool Match(object? data) => data is ViewModelBase;
        private void RegisterWindowProviders()
        {
            _popups ??= new DesktopPopupPresenter(ActiveWindow);
            if (!_layoutResetSubscribed) { layout.LayoutReset += OnLayoutReset; _layoutResetSubscribed = true; }
            if (!_disposed) ServiceHub.DesktopWindowContext = this;
        }
        private Window? ActiveWindow() => owner()?.OwnedWindows.FirstOrDefault(w => w.IsActive) ?? (_utility is { IsVisible: true } ? _utility : owner());
        private void ShowUtility(NavigateViewModelBase vm)
        {
            if (_disposed || main.CurrentViewModel != vm || owner() is not { IsVisible: true } parent || _utilityVm == vm) return;
            CloseUtility();
            _utilityVm = vm;
            Size available = parent.Screens.ScreenFromWindow(parent) is { } screen
                ? new Size(screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling)
                : parent.ClientSize;
            DesktopWindowProfile profile = DesktopPresentationCatalog.WindowFor(vm);
            double width = Math.Min(profile.Width, Math.Max(1, available.Width - 24));
            double height = Math.Min(profile.Height, Math.Max(1, available.Height - 24));
            Window window = new() { Width = width, Height = height,
                MinWidth = Math.Min(profile.MinWidth, width), MinHeight = Math.Min(profile.MinHeight, height), WindowStartupLocation = WindowStartupLocation.CenterScreen };
            DesktopDensity.Apply(window);
            _utility = window;
            window.Styles.Add(new StyleInclude(new Uri("avares://OpenUtauMobile.DesktopUI/")) { Source = new Uri("avares://OpenUtauMobile.DesktopUI/Views/DesktopStyles.axaml") });
            DesktopUi.Paint(window, Window.BackgroundProperty, "Sem.Color.Surface");
            string key = DesktopPresentationCatalog.TitleKeyFor(vm);
            window.Bind(Window.TitleProperty, window.GetResourceObservable(key));
            _utilityKey = vm.GetType().FullName ?? vm.GetType().Name;
            DesktopStoredWindowGeometry? savedGeometry = layout.State.UtilityWindows.TryGetValue(_utilityKey, out DesktopStoredWindowGeometry? saved) ? saved : null;
            RestoreGeometry(window, profile, parent, savedGeometry);
            Grid content = new() { RowDefinitions = new RowDefinitions("*") };
            DesktopUi.Paint(content, Panel.BackgroundProperty, "Sem.Color.Surface");
            Control? page = BuildPage(vm);
            if (page != null) { content.Children.Add(page); }
            window.Content = new DialogHost { BlurBackground = true, PopupTemplate = PopupTemplate, Identifier = _dialogId, IsMultipleDialogsEnabled = true, CloseOnClickAway = false, Content = content };
            RegisterWindowProviders();
            window.KeyDown += (_, e) =>
            {
                if (EditorInputController.IsComposing(window.FocusManager?.GetFocusedElement() as Visual ?? e.Source as Visual)) return;
                if (!e.Handled && (e.Key == Key.Escape || e.Key == Key.W && e.KeyModifiers == (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control)) && !IsUtilityModalOpen && vm is not ClassicSingerSetupViewModel { IsInstalling: true }) { e.Handled = true; vm.OnBackRequested(); }
            };
            window.Closing += (_, e) =>
            {
                if (_closing) return;
                SaveGeometry(window, _utilityKey);
                e.Cancel = true;
                if (!IsUtilityModalOpen && vm is not ClassicSingerSetupViewModel { IsInstalling: true }) vm.OnBackRequested();
            };
            window.Activated += (_, _) => RegisterWindowProviders();
            window.Show(parent);
            content.Focusable = true;
            content.Focus();
        }
        private void CloseUtility()
        {
            _closing = true;
            try
            {
                if (_utility != null)
                {
                    SaveGeometry(_utility, _utilityKey);
                    _utility.Content = null;
                    _utility.Close();
                }
            }
            finally { _utility = null; _utilityVm = null; _utilityKey = null; _closing = false; }
        }

        internal static void RestoreGeometry(Window window, DesktopWindowProfile profile, Window owner, DesktopStoredWindowGeometry? saved)
        {
            Avalonia.Platform.Screen? screen = saved == null ? owner.Screens.ScreenFromWindow(owner) : owner.Screens.All.FirstOrDefault(candidate =>
                saved.X >= candidate.WorkingArea.X && saved.X < candidate.WorkingArea.X + candidate.WorkingArea.Width &&
                saved.Y >= candidate.WorkingArea.Y && saved.Y < candidate.WorkingArea.Y + candidate.WorkingArea.Height)
                ?? owner.Screens.ScreenFromWindow(owner);
            if (screen == null) return;
            double scale = screen.Scaling;
            double inset = 12 * scale;
            double maxWidth = Math.Max(1, screen.WorkingArea.Width / scale - 24);
            double maxHeight = Math.Max(1, screen.WorkingArea.Height / scale - 24);
            double minimumWidth = Math.Min(profile.MinWidth, maxWidth);
            double minimumHeight = Math.Min(profile.MinHeight, maxHeight);
            window.Width = Math.Clamp(saved?.Width ?? profile.Width, minimumWidth, maxWidth);
            window.Height = Math.Clamp(saved?.Height ?? profile.Height, minimumHeight, maxHeight);
            window.MinWidth = Math.Min(profile.MinWidth, window.Width);
            window.MinHeight = Math.Min(profile.MinHeight, window.Height);
            if (saved == null) return;
            int widthPx = (int)Math.Round(window.Width * scale);
            int heightPx = (int)Math.Round(window.Height * scale);
            int minX = (int)Math.Ceiling(screen.WorkingArea.X + inset);
            int minY = (int)Math.Ceiling(screen.WorkingArea.Y + inset);
            int maxX = Math.Max(minX, screen.WorkingArea.X + screen.WorkingArea.Width - widthPx - (int)inset);
            int maxY = Math.Max(minY, screen.WorkingArea.Y + screen.WorkingArea.Height - heightPx - (int)inset);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(Math.Clamp(saved.X, minX, maxX), Math.Clamp(saved.Y, minY, maxY));
        }

        internal void SaveGeometry(Window window, string? key)
        {
            if (string.IsNullOrEmpty(key) || window.WindowState != WindowState.Normal) return;
            layout.State.UtilityWindows[key] = CaptureGeometry(window);
            layout.Save();
        }

        internal static DesktopStoredWindowGeometry CaptureGeometry(Window window) => new()
        {
            X = window.Position.X,
            Y = window.Position.Y,
            Width = window.Bounds.Width,
            Height = window.Bounds.Height,
        };

        private void OnLayoutReset()
        {
            if (_utility is not { IsVisible: true } window || _utilityVm is not { } vm || owner() is not { } parent) return;
            RestoreGeometry(window, DesktopPresentationCatalog.WindowFor(vm), parent, null);
            CenterOnOwnerScreen(window, parent);
        }

        internal static void CenterOnOwnerScreen(Window window, Window owner)
        {
            if (owner.Screens.ScreenFromWindow(owner) is not { } screen) return;
            double scale = screen.Scaling;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Position = new PixelPoint(
                screen.WorkingArea.X + Math.Max(0, (int)((screen.WorkingArea.Width - window.Width * scale) / 2)),
                screen.WorkingArea.Y + Math.Max(0, (int)((screen.WorkingArea.Height - window.Height * scale) / 2)));
        }
        public void Dispose()
        {
            _disposed = true;
            if (_layoutResetSubscribed) { layout.LayoutReset -= OnLayoutReset; _layoutResetSubscribed = false; }
            _popups?.Dispose();
            CloseUtility(); Workspace?.Dispose(); Workspace = null;
            if (ReferenceEquals(ServiceHub.DesktopWindowContext, this)) ServiceHub.DesktopWindowContext = null;
        }
    }
}
