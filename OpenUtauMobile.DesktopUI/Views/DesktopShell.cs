using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform.Storage;
using DialogHostAvalonia;
using OpenUtauMobile.Controls;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using OpenUtauMobile.DesktopUI.Services;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using OpenUtauMobile.Services.Editor;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Services.Tracks;
using OpenUtauMobile.Storage;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views;
using Serilog;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopShell : UserControl, IDisposable
    {
        private readonly MainViewModel _main;
        private readonly DesktopPageLocator _pages;
        private readonly DialogHost _dialogHost;
        private readonly Menu _menu = new();
        private readonly DesktopStatusBar _status = new();
        private readonly Action<string, double> _showStatus;
        private bool _fileAction;
        private IDisposable? _chromeSubscription;
        private IDisposable? _titleSubscription;
        private Window? _statusWindow;
        private DesktopWorkspace? _shortcutWorkspace;
        private DesktopShortcutContext? _shortcuts;
        private readonly List<(MenuItem Item, Func<bool> Available)> _menuAvailability = [];

        public DesktopShell(MainViewModel main, DesktopLayoutStore layout)
        {
            _main = main;
            _status.MessageVisibilityChanged += UpdateStatusVisibility;
            DesktopDensity.Apply(this);
            _showStatus = _status.ShowMessage;
            _pages = new DesktopPageLocator(main, layout, () => TopLevel.GetTopLevel(this) as Window, _showStatus);
            _pages.WorkspaceChanged += OnWorkspaceChanged;
            Styles.Add(new StyleInclude(new Uri("avares://OpenUtauMobile.DesktopUI/")) { Source = new Uri("avares://OpenUtauMobile.DesktopUI/Views/DesktopStyles.axaml") });
            DesktopUi.Paint(this, BackgroundProperty, "Sem.Color.Surface");
            MainView host = new(_pages) { DataContext = main };
            _dialogHost = host.GetLogicalDescendants().OfType<DialogHost>().Single();
            _pages.PopupTemplate = _dialogHost.PopupTemplate;
            Grid root = new() { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
            Grid menuBar = new() { Height = 36, Background = Avalonia.Media.Brushes.Transparent };
            WindowDecorationProperties.SetElementRole(menuBar, WindowDecorationsElementRole.TitleBar);
            WindowDecorationProperties.SetElementRole(_menu, WindowDecorationsElementRole.User);
            _menu.VerticalAlignment = VerticalAlignment.Center;
            _menu.HorizontalAlignment = HorizontalAlignment.Left;
            menuBar.Children.Add(_menu);
            TextBlock title = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12, 0), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis, FontSize = 12, IsHitTestVisible = false };
            menuBar.Children.Add(title);
            void UpdateChrome()
            {
                if (TopLevel.GetTopLevel(this) is not Window window) return;
                double left = window.IsExtendedIntoWindowDecorations && OperatingSystem.IsMacOS() ? 86 : 0;
                double right = window.IsExtendedIntoWindowDecorations && !OperatingSystem.IsMacOS() ? 138 : 0;
                _menu.Margin = new Thickness(left, 0, right, 0);
                // 两侧保留相同空间，标题始终以整个窗口为中心，窄窗口仅截断标题。
                double inset = Math.Max(left + _menu.Bounds.Width + 12, right);
                title.Margin = new Thickness(inset, 0);
            }
            _menu.SizeChanged += (_, _) => UpdateChrome();
            AttachedToVisualTree += (_, _) =>
            {
                if (TopLevel.GetTopLevel(this) is not Window window) return;
                if (_statusWindow != window)
                {
                    DetachStatusWindow();
                    _statusWindow = window;
                    window.Activated += OnWindowActivated;
                    _shortcuts = new DesktopShortcutContext(this, _status, ShowWorkspaceHint);
                }
                _pages.InitializeWindowProviders();
                _titleSubscription?.Dispose();
                _titleSubscription = title.Bind(TextBlock.TextProperty, new Binding(nameof(Window.Title)) { Source = window });
                UpdateChrome();
                _chromeSubscription?.Dispose();
                _chromeSubscription = window.GetObservable(Window.IsExtendedIntoWindowDecorationsProperty).Subscribe(_ => UpdateChrome());
            };
            DetachedFromVisualTree += (_, _) => DetachStatusWindow();
            root.Children.Add(menuBar);
            Grid.SetRow(host, 1);
            root.Children.Add(host);
            Grid.SetRow(_status, 2); root.Children.Add(_status);
            Content = root;
            _pages.InitializeWindowProviders();
            BuildMenus();
            _main.PropertyChanged += OnMainChanged;
            AddHandler(KeyDownEvent, OnShellKeyDown, RoutingStrategies.Tunnel);
            DragDrop.SetAllowDrop(this, true);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
            AddHandler(DragDrop.DropEvent, OnDrop);
            UpdateNavigation();
        }
        private bool Ready => !_fileAction && !PianoRollViewModel.IsBatchEditRunning && _main.CurrentViewModel is not (SplashScreenViewModel or ClassicSingerSetupViewModel { IsInstalling: true }) && !_pages.IsUtilityModalOpen && !_dialogHost.IsOpen && _main.ActiveEditor is not { IsLoadingProject: true };
        public void CancelInput() => _pages.Workspace?.CancelInput();
        private void OnWindowActivated(object? sender, EventArgs e) => _pages.InitializeWindowProviders();
        public bool PrepareFileAction() => _pages.Workspace?.PrepareFileAction() != false;
        private void Show(NavigateViewModelBase vm) => _main.NavigateDesktopUtility(vm);
        private MenuItem Menu(string key, params MenuItem[] items)
        {
            MenuItem menu = new() { Header = DesktopUi.Label(key), ItemsSource = items };
            return menu;
        }
        private MenuItem Item(string key, Func<Task> action, Func<bool>? available = null, KeyGesture? gesture = null)
        {
            MenuItem item = new() { Header = DesktopUi.Label(key), InputGesture = gesture };
            Func<bool> canExecute = () => Ready && (available?.Invoke() ?? true);
            _menuAvailability.Add((item, canExecute));
            item.Click += async (_, _) => { if (canExecute()) await RunAsync(action); };
            return item;
        }
        private MenuItem Item(string key, Action action, Func<bool>? available = null, KeyGesture? gesture = null) => Item(key, () => { action(); return Task.CompletedTask; }, available, gesture);
        private MenuItem PanelItem(string key, Action<DesktopWorkspace> toggle, Func<DesktopWorkspace, bool> visible)
        {
            MenuItem item = Item(key, () => { if (_pages.Workspace is { } workspace) toggle(workspace); }, () => _pages.Workspace != null);
            item.ToggleType = MenuItemToggleType.CheckBox;
            _panelChecks.Add(() => item.IsChecked = _pages.Workspace is { } workspace && visible(workspace));
            item.PointerEntered += (_, _) => item.IsChecked = _pages.Workspace is { } workspace && visible(workspace);
            return item;
        }
        private Task EditorAction(EditorMoreAction action) => _main.ActiveEditor?.ExecuteEditorActionAsync(action) ?? Task.CompletedTask;
        private bool HasEditor => _main.ActiveEditor != null;
        private bool HasNotes => _main.ActiveEditor?.PianoRollViewModel.SelectedNotes.Count > 0;
        private bool CommandAvailable(Func<EditorViewModel, System.Windows.Input.ICommand> command) => _main.ActiveEditor is { } editor && command(editor).CanExecute(null);
        private KeyGesture Gesture(Key key, bool shift = false) => new(key, (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) | (shift ? KeyModifiers.Shift : KeyModifiers.None));
        private MenuItem SelectionItem(string key, Key action) => Item(key, () => _pages.Workspace?.ClipboardAction(action),
            () => _pages.Workspace?.CanClipboardAction(action) == true, action == Key.Delete ? new KeyGesture(Key.Delete) : Gesture(action));
        private readonly List<Action> _panelChecks = [];
        private void BuildMenus()
        {
            _menu.AddHandler(MenuItem.SubmenuOpenedEvent, (_, _) =>
            {
                foreach (Action update in _panelChecks) update();
                foreach ((MenuItem item, Func<bool> available) in _menuAvailability) item.IsEnabled = available();
            });
            MenuItem recent = Menu("Home.Recent.Title");
            MenuItem templates = Menu("Desktop.Templates");
            MenuItem recovery = Item("Home.Recovery.OpenAction", () => OpenAsync(new(Preferences.Default.RecoveryPath, ProjectOpenKind.ExternalCopy)));
            MenuItem file = Menu("Common.File", Item("Home.Action.New", NewProjectAsync, gesture: Gesture(Key.N)), Item("Home.Action.Open", PickProjectAsync, gesture: Gesture(Key.O)), recent, templates, recovery,
                Item("Editor.Save", () => SaveAsync(false), () => HasEditor, Gesture(Key.S)), Item("EditorMore.SaveAs", () => SaveAsync(true), () => HasEditor, Gesture(Key.S, true)),
                Item("EditorMore.ImportAudio", () => EditorAction(EditorMoreAction.ImportAudio), () => HasEditor), Item("EditorMore.ImportTrack", () => EditorAction(EditorMoreAction.ImportTrack), () => HasEditor),
                Item("EditorMore.ExportAudio", () => EditorAction(EditorMoreAction.ExportAudio), () => HasEditor), Item("Desktop.CloseProject", CloseProjectAsync, () => HasEditor, Gesture(Key.W)));
            file.SubmenuOpened += (_, e) =>
            {
                if (e.Source != file) return;
                recent.ItemsSource = Preferences.Default.RecentFiles.Select(path => PathItem(path, ProjectOpenKind.Normal)).ToArray();
                recent.IsEnabled = Preferences.Default.RecentFiles.Count > 0;
                string[] files = Directory.Exists(PathManager.Inst.TemplatesPath) ? Directory.GetFiles(PathManager.Inst.TemplatesPath, "*.ustx") : [];
                templates.ItemsSource = files.OrderBy(Path.GetFileName).Select(path => PathItem(path, ProjectOpenKind.Template)).ToArray();
                templates.IsEnabled = files.Length > 0;
                recovery.IsVisible = !string.IsNullOrWhiteSpace(Preferences.Default.RecoveryPath) && File.Exists(Preferences.Default.RecoveryPath);
            };
            MenuItem batchEdits = DesktopBatchEditMenu.Create(() => _main.ActiveEditor?.PianoRollViewModel, () => Ready);
            MenuItem edit = Menu("Common.Edit", Item("Editor.Undo", () => _main.ActiveEditor?.UndoCommand.Execute().Subscribe(), () => CommandAvailable(editor => editor.UndoCommand), Gesture(Key.Z)),
                Item("Editor.Redo", () => _main.ActiveEditor?.RedoCommand.Execute().Subscribe(), () => CommandAvailable(editor => editor.RedoCommand), Gesture(OperatingSystem.IsMacOS() ? Key.Z : Key.Y, OperatingSystem.IsMacOS())),
                SelectionItem("Common.Cut", Key.X), SelectionItem("Common.Copy", Key.C), SelectionItem("Common.Paste", Key.V),
                SelectionItem("Common.SelectAll", Key.A), SelectionItem("Common.Delete", Key.Delete),
                Item("Editor.Action.Merge", () => _main.ActiveEditor?.MergeSelectedParts(), () => _main.ActiveEditor?.CanMergeSelectedParts == true),
                batchEdits,
                Item("BulkLyricEdit.Title", () => _main.ActiveEditor?.PianoRollViewModel.ShowBulkLyricEditPopupAsync() ?? Task.CompletedTask, () => HasNotes),
                Item("Desktop.NoteProperties", () => _pages.Workspace?.ShowNotes(), () => HasNotes));
            edit.SubmenuOpened += (_, e) =>
            {
                if (e.Source == edit) DesktopBatchEditMenu.Refresh(batchEdits, _main.ActiveEditor?.PianoRollViewModel, () => Ready);
            };
            _menu.ItemsSource = new[]
            {
                file,
                edit,
                Menu("Desktop.View",
                    PanelItem("Desktop.Arrangement", w => w.ToggleArrangement(), w => w.IsArrangementVisible),
                    PanelItem("Desktop.PianoRoll", w => w.TogglePianoRoll(), w => w.IsPianoRollVisible),
                    PanelItem("Desktop.Parameters", w => w.ToggleParameters(), w => w.IsParametersVisible),
                    PanelItem("Desktop.Inspector", w => w.ToggleInspector(), w => w.IsInspectorRequested),
                    Item("Mixer.Title", () => _pages.Workspace?.ToggleMixer(), () => HasEditor), Item("Desktop.ResetLayout", () => _pages.Workspace?.ResetLayout(), () => HasEditor)),
                Menu("Desktop.Project", Item("TrackAdder.AddTrack", () => _main.ActiveEditor?.AddTrackCommand.Execute().Subscribe(), () => HasEditor),
                    Item("EditorMore.SaveAsTemplate", () => EditorAction(EditorMoreAction.SaveAsTemplate), () => HasEditor)),
                Menu("Desktop.Transport", Item("Editor.PlayPause", () => _main.ActiveEditor?.PlayPauseCommand.Execute().Subscribe(), () => CommandAvailable(editor => editor.PlayPauseCommand), new KeyGesture(Key.Space)),
                    Item("Editor.Stop", () => _main.ActiveEditor?.StopCommand.Execute().Subscribe(), () => CommandAvailable(editor => editor.StopCommand))),
                Menu("Desktop.Tools", Item("SingerManagement.Title", () => Show(new SingerManagementViewModel(_main))), Item("Desktop.Tools", () => Show(new DesktopToolsViewModel(_main))),
                    Item("DependencyManager.Title", () => Show(new DependencyManagerViewModel(_main))), Item("Settings.Title", () => Show(new SettingsViewModel(_main)))),
                Menu("Desktop.Help", Item("About.Title", () => Show(new AboutViewModel(_main))), Item("ExportLogs.Title", () => Show(new ExportLogsViewModel(_main))))
            };
        }
        private MenuItem PathItem(string path, ProjectOpenKind kind)
        {
            MenuItem item = new() { Header = Path.GetFileNameWithoutExtension(path) };
            ToolTip.SetTip(item, path);
            item.Click += async (_, _) => { if (Ready) await RunAsync(() => OpenAsync(new(path, kind))); };
            return item;
        }
        private async Task RunAsync(Func<Task> action)
        {
            try { await action(); }
            catch (Exception exception)
            {
                Log.Error(exception, "桌面操作失败");
                ErrorDialogService.Show(new ErrorDialogViewModel(new OpenUtau.Core.ErrorMessageNotification(exception)));
            }
        }
        private Task RunFileAsync(Func<Task> action)
        {
            if (_fileAction) return Task.CompletedTask;
            return ExecuteAsync();
            async Task ExecuteAsync()
            {
                _fileAction = true;
                try { if (PrepareFileAction()) await action(); }
                finally { _fileAction = false; }
            }
        }
        private Task PickProjectAsync() => RunFileAsync(async () =>
        {
            string file = await FilePicker.PickSingleFileAsync(L.S("Home.Action.Open"), TrackImportService.FilePatterns);
            if (!string.IsNullOrEmpty(file)) await _main.OpenDesktopProjectAsync(new(file, Path.GetExtension(file).Equals(".ustx", StringComparison.OrdinalIgnoreCase) ? ProjectOpenKind.Normal : ProjectOpenKind.ExternalCopy));
        });
        private Task NewProjectAsync()
        {
            string template = Path.Combine(OpenUtau.Core.PathManager.Inst.TemplatesPath, "default.ustx");
            return OpenAsync(File.Exists(template) ? new(template, ProjectOpenKind.Template) : new());
        }
        private Task OpenAsync(ProjectOpenOptions options) => RunFileAsync(async () => { await _main.OpenDesktopProjectAsync(options); });
        private Task SaveAsync(bool saveAs) => RunFileAsync(async () =>
        {
            if (_main.ActiveEditor is { } editor) await editor.SaveFromInputAsync(saveAs);
        });
        private Task CloseProjectAsync() => RunFileAsync(async () =>
        {
            if (_main.ActiveEditor is not { } editor || !await editor.ConfirmExitAsync()) return;
            await _main.CloseDesktopProjectAsync(editor);
        });
        private async void OnShellKeyDown(object? sender, KeyEventArgs e)
        {
            if (!e.Handled && Ready && e.Key == Key.Escape && _main.CurrentViewModel != _main.ActiveEditor && _main.CurrentViewModel is not HomeViewModel)
            { e.Handled = true; _main.CurrentViewModel.OnBackRequested(); return; }
            KeyModifiers command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            bool modifier = e.KeyModifiers == command || e.Key == Key.S && e.KeyModifiers == (command | KeyModifiers.Shift);
            if (e.Handled || !Ready || !modifier) return;
            if (e.Key != Key.S && OpenUtauMobile.Services.Editor.EditorInputController.IsComposing(e.Source as Visual)) return;
            Func<Task>? action = e.Key switch { Key.N => NewProjectAsync, Key.O => PickProjectAsync, Key.W => CloseProjectAsync, Key.S => () => SaveAsync(e.KeyModifiers.HasFlag(KeyModifiers.Shift)), _ => null };
            if (action == null) return;
            e.Handled = true;
            await RunAsync(action);
        }
        private static bool Supported(string path) => TrackImportService.FilePatterns.Any(p => Path.GetExtension(path).Equals(p[1..], StringComparison.OrdinalIgnoreCase)) || IsAudio(path) || IsArchive(path) || IsTool(path);
        private static bool IsArchive(string path) => new[] { ".zip", ".rar", ".uar" }.Contains(Path.GetExtension(path).ToLowerInvariant());
        private static bool IsTool(string path) => new[] { ".exe", ".bat", ".sh" }.Contains(Path.GetExtension(path).ToLowerInvariant()) || !OperatingSystem.IsWindows() && Path.GetExtension(path).Length == 0 && DesktopToolsService.IsExecutable(path);
        private static bool IsAudio(string path) => new[] { ".wav", ".mp3", ".flac", ".ogg", ".aac", ".aiff", ".aif", ".aifc" }.Contains(Path.GetExtension(path).ToLowerInvariant());
        private void OnDragOver(object? sender, DragEventArgs e)
        {
            e.DragEffects = Ready && e.DataTransfer.TryGetFiles()?.Any(f => f.TryGetLocalPath() is { } path && Supported(path)) == true ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
        private async void OnDrop(object? sender, DragEventArgs e)
        {
            if (!Ready) return;
            string[] paths = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().Where(Supported).ToArray() ?? [];
            e.Handled = true;
            await RunAsync(() => HandleDroppedPathsAsync(paths));
        }
        internal async Task HandleDroppedPathsAsync(string[] paths)
        {
            if (paths.Count(p => IsArchive(p) || IsTool(p)) > 1) { ToastService.Enqueue(L.S("Desktop.DropOneInstaller")); return; }
            string[] projects = paths.Where(p => Supported(p) && !IsAudio(p) && !IsArchive(p) && !IsTool(p)).ToArray();
            if (projects.Length > 1) { ToastService.Enqueue(L.S("Desktop.DropOneProject")); return; }
            await RunFileAsync(async () =>
            {
                if (projects.Length == 1 && !await _main.OpenDesktopProjectAsync(new(projects[0], Path.GetExtension(projects[0]).Equals(".ustx", StringComparison.OrdinalIgnoreCase) ? ProjectOpenKind.Normal : ProjectOpenKind.ExternalCopy))) return;
                if (paths.Any(IsAudio) && _main.ActiveEditor == null && !await _main.OpenDesktopProjectAsync(new())) return;
                foreach (string path in paths.Where(IsAudio)) EditorViewModel.ImportAudioFile(path);
            });
            string? archive = paths.FirstOrDefault(IsArchive);
            if (archive != null)
            {
                Show(new ClassicSingerSetupViewModel(_main) { ArchiveFilePath = archive });
                return;
            }
            string? tool = paths.FirstOrDefault(IsTool);
            if (tool == null) return;
            string filename = Path.GetFileName(tool);
            DesktopToolKind? kind = filename.Contains("wavtool", StringComparison.OrdinalIgnoreCase) ? DesktopToolKind.Wavtool
                : filename.Contains("resamp", StringComparison.OrdinalIgnoreCase) || filename.Contains("straycat", StringComparison.OrdinalIgnoreCase) ? DesktopToolKind.Resampler : null;
            if (kind == null)
            {
                string? choice = await OptionConfirmPopupService.ShowAsync(L.S("Desktop.InstallFile"), filename,
                    new[] { new OptionConfirmOption(L.S("Desktop.Resamplers"), "resampler"), new OptionConfirmOption(L.S("Desktop.Wavtools"), "wavtool"), new OptionConfirmOption(L.S("Common.Cancel"), "cancel", isDefault: true) });
                kind = choice == "resampler" ? DesktopToolKind.Resampler : choice == "wavtool" ? DesktopToolKind.Wavtool : null;
            }
            if (kind is not { } selectedKind) return;
            DesktopToolsViewModel tools = new(_main);
            Show(tools);
            _ = tools.InstallDroppedFileAsync(tool, selectedKind);
        }
        private void OnMainChanged(object? sender, PropertyChangedEventArgs e) => UpdateNavigation();
        private void OnWorkspaceChanged(DesktopWorkspace? workspace) => UpdateNavigation();
        private void UpdateNavigation()
        {
            _menu.IsEnabled = _main.CurrentViewModel is not SplashScreenViewModel;
            EditorViewModel? editor = _main.ActiveEditor;
            DesktopWorkspace? workspace = editor == null ? null : _pages.Workspace;
            if (!ReferenceEquals(_shortcutWorkspace, workspace))
            {
                if (_shortcutWorkspace != null) _shortcutWorkspace.ShortcutContextChanged -= OnShortcutContextChanged;
                _shortcutWorkspace = workspace;
                if (_shortcutWorkspace != null) _shortcutWorkspace.ShortcutContextChanged += OnShortcutContextChanged;
            }
            if (_shortcuts == null) ShowWorkspaceHint();
            else _shortcuts.Refresh();
            UpdateStatusVisibility();
        }
        private void UpdateStatusVisibility() => _status.IsVisible = _main.ActiveEditor != null || _status.HasMessage;
        private void ShowWorkspaceHint()
        {
            _status.SetEditorHint(_shortcutWorkspace?.CurrentEditorHint);
        }
        private void OnShortcutContextChanged(DesktopEditorHint? hint)
        {
            if (_shortcutWorkspace != null && _main.CurrentViewModel == _main.ActiveEditor)
                _status.SetEditorHint(hint);
        }
        private void DetachStatusWindow()
        {
            if (_statusWindow == null) return;
            _statusWindow.Activated -= OnWindowActivated;
            _shortcuts?.Dispose();
            _shortcuts = null;
            _statusWindow = null;
        }
        public void Dispose()
        {
            _chromeSubscription?.Dispose(); _titleSubscription?.Dispose(); _main.PropertyChanged -= OnMainChanged; _pages.WorkspaceChanged -= OnWorkspaceChanged; _pages.Dispose();
            DetachStatusWindow();
            if (_shortcutWorkspace != null) _shortcutWorkspace.ShortcutContextChanged -= OnShortcutContextChanged;
            _status.MessageVisibilityChanged -= UpdateStatusVisibility;
            _status.Dispose();
        }
    }
}
