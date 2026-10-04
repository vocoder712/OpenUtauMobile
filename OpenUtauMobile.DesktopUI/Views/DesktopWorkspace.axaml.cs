using System;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Reactive.Linq;
using OpenUtauMobile.Services;
using System.Windows.Input;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using IconPacks.Avalonia.PhosphorIcons;
using OpenUtauMobile.Tools;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Services.Dialogs;
using OpenUtauMobile.Controls;
using OpenUtauMobile.DesktopUI.Services;
using OpenUtauMobile.Services.Editor;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    public partial class DesktopWorkspace : UserControl, ICmdSubscriber, IDisposable
    {
        private readonly EditorViewModel _editor;
        private readonly DesktopLayoutStore _layout;
        private readonly EditorInputController _input;
        private readonly DesktopInspector _inspector;
        private bool _applyingLayout;
        private readonly DesktopInlineEditor _inline;
        private Window? _mixerWindow;
        private readonly List<(ToggleButton Button, PianoRollEditMode Mode)> _pianoModes = [];
        private ContextMenu? _pianoNotesContextMenu;
        private ContextMenu? _pianoLayoutContextMenu;
        private IDisposable? _hintBinding;
        private string? _hintSignature;
        private DesktopEditorHint? _currentEditorHint;
        private readonly List<DesktopTimelineInput> _timelines = [];
        private readonly List<DesktopTempoEditor> _tempos = [];
        private bool _attached;
        private bool _disposed;

        public DesktopWorkspace(EditorViewModel editor, DesktopLayoutStore layout)
        {
            _editor = editor;
            _layout = layout;
            InitializeComponent();
            DataContext = editor;
            editor.IsTrackHeaderExpanded = true;
            editor.UseDesktopInput = true;
            editor.PianoRollViewModel.UseDesktopMouseInput = true;
            editor.PianoRollViewModel.EditMode = PianoRollEditMode.Note;
            editor.PianoRollViewModel.PhonemePanelMode = PhonemePanelMode.PhonemeAdvanced;
            editor.PianoRollViewModel.KeyHeight = 20;
            editor.PianoRollViewModel.TickWidth = .16;
            editor.PianoRollViewModel.SetPlayMarkerRatio(.08);
            CreateToolbars();
            Arrangement.SetHeaderExpanded(true);
            Arrangement.SetDesktopChrome();
            _tempos.Add(new DesktopTempoEditor(TempoInput, editor, this));
            Button tempoButton = Arrangement.FindControl<Button>("ProjectTempoButton")!;
            StackPanel projectValues = (StackPanel)tempoButton.Parent!;
            TextBox headerTempo = new() { Name = "ArrangementTempoInput", Width = 56, Classes = { "DesktopProjectTempo" }, TextAlignment = Avalonia.Media.TextAlignment.Center };
            ToolTip.SetTip(headerTempo, DesktopUi.Label("ProjectEdit.BpmRange"));
            int tempoIndex = projectValues.Children.IndexOf(tempoButton);
            projectValues.Children.RemoveAt(tempoIndex);
            projectValues.Children.Insert(tempoIndex, headerTempo);
            _tempos.Add(new DesktopTempoEditor(headerTempo, editor, this));
            DesktopResizeGrip parameterGrip = new();
            Piano.SetDesktopChrome(parameterGrip);
            parameterGrip.AddHandler(PointerReleasedEvent, (_, _) => SavePanels(), RoutingStrategies.Bubble, true);
            UPart? contextPart = null;
            Arrangement.PartsCanvas.ContextMenu = CreateContextMenu(false, () => contextPart);
            TrackHeaderCanvas header = Arrangement.LayoutGrid.Children.OfType<TrackHeaderCanvas>().Single();
            UTrack? contextTrack = null;
            header.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (e.GetCurrentPoint(header).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed) return;
                int index = _editor.CanvasYToTrackNo(e.GetPosition(header).Y);
                contextTrack = index >= 0 && index < DocManager.Inst.Project.tracks.Count ? DocManager.Inst.Project.tracks[index] : null;
            }, RoutingStrategies.Tunnel);
            ContextMenu trackMenu = new();
            trackMenu.Opening += (_, _) => trackMenu.ItemsSource = contextTrack?.RendererSettings.renderer == Renderers.CLASSIC ? CreateClassicToolMenus(contextTrack) : [];
            header.ContextMenu = trackMenu;
            _pianoNotesContextMenu = CreateContextMenu(true);
            _pianoLayoutContextMenu = CreateContextMenu(true);
            Piano.NotesCanvas.ContextMenu = _pianoNotesContextMenu;
            Piano.LayoutGrid.ContextMenu = _pianoLayoutContextMenu;
            Arrangement.PartsCanvas.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (e.GetCurrentPoint(Arrangement.PartsCanvas).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed) return;
                contextPart = _editor.HitTestPart(e.GetPosition(Arrangement.PartsCanvas));
                if (contextPart is { } part && !_editor.SelectedParts.Contains(part))
                { _editor.SelectedParts.Clear(); _editor.SelectedParts.Add(part); }
            }, RoutingStrategies.Tunnel);
            Piano.NotesCanvas.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (e.GetCurrentPoint(Piano.NotesCanvas).Properties.PointerUpdateKind != PointerUpdateKind.RightButtonPressed) return;
                if (_editor.PianoRollViewModel.EditMode == PianoRollEditMode.PitchPen) return;
                if (_editor.PianoRollViewModel.HitTestNote(e.GetPosition(Piano.NotesCanvas)) is { } note && !_editor.PianoRollViewModel.SelectedNotes.Contains(note))
                { _editor.PianoRollViewModel.SelectedNotes.Clear(); _editor.PianoRollViewModel.SelectedNotes.Add(note); }
            }, RoutingStrategies.Tunnel);
            _inspector = new DesktopInspector(editor, layout) { HorizontalAlignment = HorizontalAlignment.Left };
            InspectorHost.Child = _inspector;
            _inspector.RequestShowParameters += ShowParameters;
            InspectorHost.SizeChanged += (_, _) =>
                _inspector.Width = Math.Max(_inspector.MinWidth, InspectorHost.Bounds.Width - InspectorHost.BorderThickness.Left - InspectorHost.BorderThickness.Right - InspectorHost.Padding.Left - InspectorHost.Padding.Right);
            _input = new EditorInputController(this, Arrangement, Piano, () => !IsPianoRollVisible, () => null, PrepareFileAction);
            Arrangement.AddHandler(PointerPressedEvent, (_, _) => SetActiveArea(EditorInputController.EditArea.Tracks), RoutingStrategies.Tunnel, true);
            Piano.AddHandler(PointerPressedEvent, (_, _) => SetActiveArea(EditorInputController.EditArea.PianoRoll), RoutingStrategies.Tunnel, true);
            Arrangement.AddHandler(GotFocusEvent, (_, _) => SetActiveArea(EditorInputController.EditArea.Tracks), RoutingStrategies.Bubble, true);
            Piano.AddHandler(GotFocusEvent, (_, _) => SetActiveArea(EditorInputController.EditArea.PianoRoll), RoutingStrategies.Bubble, true);
            AddHandler(LostFocusEvent, (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                UpdateModes();
                NotifyShortcutContextChanged();
            }), RoutingStrategies.Bubble, true);
            Arrangement.AddHandler(PointerReleasedEvent, (_, _) => Dispatcher.UIThread.Post(UpdateModes, DispatcherPriority.Background), RoutingStrategies.Bubble, true);
            Piano.AddHandler(PointerReleasedEvent, (_, _) => Dispatcher.UIThread.Post(UpdateModes, DispatcherPriority.Background), RoutingStrategies.Bubble, true);
            AddHandler(KeyUpEvent, (_, _) => UpdateModes(), RoutingStrategies.Bubble, true);
            _inline = new DesktopInlineEditor(editor.PianoRollViewModel, Piano);
            _timelines.Add(new DesktopTimelineInput(Piano.Ruler, Piano.NotesCanvas, x => _editor.PianoRollViewModel.PointXToTick(x), _input, _editor.PianoRollViewModel));
            _timelines.Add(new DesktopTimelineInput(Arrangement.Ruler, Arrangement.PartsCanvas, x => (int)_editor.CanvasXToTick(x), _input, _editor));
            EditingGrid.RowDefinitions[0].MinHeight = 0;
            EditingGrid.RowDefinitions[2].MinHeight = 0;
            WorkspaceGrid.ColumnDefinitions[0].MinWidth = 600;
            foreach (GridSplitter splitter in new[] { ArrangementSplitter, InspectorSplitter })
                splitter.AddHandler(PointerReleasedEvent, (_, _) => SavePanels(), RoutingStrategies.Bubble, true);
            AttachedToVisualTree += (_, _) =>
            {
                if (_attached || _disposed) return;
                _attached = true;
                PianoRollViewModel.BatchEditRunningChanged += OnBatchEditRunningChanged;
                OnBatchEditRunningChanged(PianoRollViewModel.IsBatchEditRunning);
                _editor.PropertyChanged += OnEditorChanged;
                _editor.PianoRollViewModel.PropertyChanged += OnEditorChanged;
                DocManager.Inst.AddSubscriber(this);
                _editor.SelectedParts.CollectionChanged += OnSelectionChanged;
                _editor.PianoRollViewModel.SelectedNotes.CollectionChanged += OnSelectionChanged;
                _editor.PianoRollViewModel.SelectedAnchors.CollectionChanged += OnSelectionChanged;
                StatusHintHost.IsVisible = ServiceHub.DesktopWindowContext == null;
                UpdateModes();
                NotifyShortcutContextChanged();
                ApplyLayout();
                UpdateTime();
            };
            DetachedFromVisualTree += (_, _) =>
            {
                SavePanels();
                PianoRollViewModel.BatchEditRunningChanged -= OnBatchEditRunningChanged;
                _editor.PropertyChanged -= OnEditorChanged;
                _editor.PianoRollViewModel.PropertyChanged -= OnEditorChanged;
                DocManager.Inst.RemoveSubscriber(this);
                _editor.SelectedParts.CollectionChanged -= OnSelectionChanged;
                _editor.PianoRollViewModel.SelectedNotes.CollectionChanged -= OnSelectionChanged;
                _editor.PianoRollViewModel.SelectedAnchors.CollectionChanged -= OnSelectionChanged;
                ServiceHub.DesktopWindowContext?.SetHint(string.Empty);
                _attached = false;
            };
            SizeChanged += (_, _) => UpdateInspectorVisibility();
        }
        public void CancelInput() { foreach (DesktopTimelineInput timeline in _timelines) timeline.Cancel(); _inline.Close(false); _input.CancelEditorInput(); }
        public bool PrepareFileAction()
        {
            if (PianoRollViewModel.IsBatchEditRunning) return false;
            if (EditorInputController.IsComposing(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual)) return false;
            foreach (DesktopTimelineInput timeline in _timelines) timeline.Cancel();
            _input.CancelEditorInput();
            _inline.Close(true);
            foreach (DesktopTempoEditor tempo in _tempos)
            {
                if (!tempo.Commit()) { tempo.Focus(); return false; }
            }
            Focus();
            return _inspector.CommitPendingInput();
        }

        private void CreateToolbars()
        {
            StackPanel tracks = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
            _editor.TrackEditMode = TrackEditMode.Normal;
            ComboBox snap = new() { ItemsSource = EditorViewModel.QuantizeOptions, MinWidth = 78, DataContext = _editor, Classes = { "DesktopTransportControl" } };
            snap.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<int>((value, _) => new TextBlock { Text = new SnapDivToLabelConverter().Convert(value, typeof(string), null, System.Globalization.CultureInfo.CurrentCulture)?.ToString() });
            snap.Bind(SelectingItemsControl.SelectedItemProperty, new Binding("SnapDiv") { Mode = BindingMode.TwoWay });
            ToolTip.SetTip(snap, DesktopUi.Label("Editor.Quantize")); tracks.Children.Add(snap);
            TrackTools.Content = tracks;
            StackPanel piano = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach ((PianoRollEditMode, PackIconPhosphorIconsKind) item in new[] { (PianoRollEditMode.Note, PackIconPhosphorIconsKind.Cursor), (PianoRollEditMode.Anchor, PackIconPhosphorIconsKind.WaveSine), (PianoRollEditMode.PitchPen, PackIconPhosphorIconsKind.Pencil) })
            {
                ToggleButton button = ModeButton(item.Item2, item.Item1 switch { PianoRollEditMode.Note => "Desktop.NoteTool", PianoRollEditMode.Anchor => "Desktop.TuningTool", _ => "PianoRoll.PitchPen" });
                button.Click += (_, _) => { SetActiveArea(EditorInputController.EditArea.PianoRoll); _editor.PianoRollViewModel.EditMode = item.Item1; UpdateModes(); };
                _pianoModes.Add((button, item.Item1)); piano.Children.Add(button);
            }
            PianoTools.Content = piano;
        }
        private static ToggleButton ModeButton(PackIconPhosphorIconsKind icon, string key)
        {
            ToggleButton button = new() { Content = new PackIconPhosphorIcons { Kind = icon, Width = 16, Height = 16 }, Padding = new Thickness(7, 4), MinHeight = 28, CornerRadius = new CornerRadius(4) };
            ToolTip.SetTip(button, DesktopUi.Label(key));
            return button;
        }
        internal event Action<DesktopEditorHint?>? ShortcutContextChanged;
        public EditorInputController.EditArea ActiveEditArea => _input.ActiveArea;
        internal DesktopEditorHint? CurrentEditorHint => _currentEditorHint;
        private EditorInputController.EditArea? KeyboardEditArea()
        {
            return _input.ActiveArea is EditorInputController.EditArea.Tracks or EditorInputController.EditArea.PianoRoll
                ? _input.ActiveArea : null;
        }
        private void SetActiveArea(EditorInputController.EditArea area)
        {
            _input.ActiveArea = area;
            UpdateModes();
            NotifyShortcutContextChanged();
        }
        private void NotifyShortcutContextChanged() => ShortcutContextChanged?.Invoke(_currentEditorHint);
        private void UpdateModes()
        {
            bool pitchPen = _editor.PianoRollViewModel.EditMode == PianoRollEditMode.PitchPen;
            Piano.NotesCanvas.ContextMenu = pitchPen ? null : _pianoNotesContextMenu;
            Piano.LayoutGrid.ContextMenu = pitchPen ? null : _pianoLayoutContextMenu;
            foreach ((ToggleButton Button, PianoRollEditMode Mode) item in _pianoModes) item.Button.IsChecked = item.Mode == _editor.PianoRollViewModel.EditMode;
            List<IObservable<string>> hints = [];
            List<string> signature = [];
            EditorInputController.EditArea? active = KeyboardEditArea();
            string? toolKey = active == EditorInputController.EditArea.PianoRoll ? _editor.PianoRollViewModel.EditMode switch
            {
                PianoRollEditMode.Note => "Desktop.Notes",
                PianoRollEditMode.Anchor => "Desktop.TuningMode",
                PianoRollEditMode.PitchPen => _editor.PianoRollViewModel.IsPitchEraserMode ? "PianoRoll.Toast.PitchEraser" : "PianoRoll.PitchPen",
                _ => null,
            } : null;
            signature.Add("active:" + active + ":" + toolKey);
            if (active is { } area)
            {
                AddMouseHints(area, hints, signature);
                AddSelectionHints(area, hints, signature);
                AddProjectHints(hints, signature);
            }
            string nextSignature = string.Join("|", signature);
            if (_hintSignature == nextSignature) return;
            _hintSignature = nextSignature;
            _hintBinding?.Dispose();
            if (active == null)
            {
                _currentEditorHint = null;
                InputHint.Text = string.Empty;
                NotifyShortcutContextChanged();
                return;
            }
            IObservable<string> text = hints.Count == 0 ? Observable.Return(string.Empty) :
                Observable.CombineLatest(hints, values => string.Join("  ·  ", values));
            _hintBinding = text.Subscribe(value =>
            {
                _currentEditorHint = new DesktopEditorHint(HeaderKey(active.Value), toolKey, value);
                InputHint.Text = value;
                NotifyShortcutContextChanged();
            });
        }
        private static string HeaderKey(EditorInputController.EditArea area) => area == EditorInputController.EditArea.Tracks ? "Desktop.Arrangement" : "Desktop.PianoRoll";
        private void AddMouseHints(EditorInputController.EditArea area, List<IObservable<string>> hints, List<string> signature)
        {
            if (_editor.IsLoadingProject) return;
            PianoRollViewModel piano = _editor.PianoRollViewModel;
            string key = area == EditorInputController.EditArea.Tracks ? "Desktop.ArrangementHint" :
                piano.EditingVoicePart == null ? "Desktop.NavigationHint" : piano.EditMode switch
                {
                    PianoRollEditMode.Anchor => "Desktop.TuningHint",
                    PianoRollEditMode.PitchPen => piano.IsPitchEraserMode ? "Desktop.PitchEraserHint" : "Desktop.PitchHint",
                    _ => "Desktop.InputHint",
                };
            signature.Add("mouse:" + key);
            hints.Add(InputHint.GetResourceObservable(key).Select(value => value as string ?? string.Empty));
        }
        private void AddSelectionHints(EditorInputController.EditArea area, List<IObservable<string>> hints, List<string> signature)
        {
            if (_editor.IsLoadingProject) return;
            string command = OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";
            Visual? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
            if (focused?.GetSelfAndVisualAncestors().Any(visual => visual is TextBox) == true) return;
            foreach ((Avalonia.Input.Key Key, string Gesture, string Resource) action in new[]
            {
                (Avalonia.Input.Key.C, command + "+C", "Common.Copy"),
                (Avalonia.Input.Key.X, command + "+X", "Common.Cut"),
                (Avalonia.Input.Key.V, command + "+V", "Common.Paste"),
                (Avalonia.Input.Key.A, command + "+A", "Common.SelectAll"),
                (Avalonia.Input.Key.Delete, "Delete", "Common.Delete"),
            })
            {
                if (_input.CanExecuteSelectionAction(area, action.Key)) AddCommandHint(action.Gesture, action.Resource, hints, signature);
            }
        }
        private void AddCommandHint(string gesture, string resource, List<IObservable<string>> hints, List<string> signature)
        {
            signature.Add("key:" + gesture + ":" + resource);
            hints.Add(InputHint.GetResourceObservable(resource).Select(value => $"{gesture}: {value as string ?? string.Empty}"));
        }
        private void AddProjectHints(List<IObservable<string>> hints, List<string> signature)
        {
            if (_editor.IsLoadingProject) return;
            string command = OperatingSystem.IsMacOS() ? "Cmd" : "Ctrl";
            if (((ICommand)_editor.SaveCommand).CanExecute(null))
            {
                AddCommandHint(command + "+S", "Editor.Save", hints, signature);
                AddCommandHint(command + "+Shift+S", "EditorMore.SaveAs", hints, signature);
            }
            Visual? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
            if (focused?.GetSelfAndVisualAncestors().Any(visual => visual is TextBox) == true) return;
            if (!DocManager.Inst.HasOpenUndoGroup && DocManager.Inst.GetUndoState(out _) && ((ICommand)_editor.UndoCommand).CanExecute(null))
                AddCommandHint(command + "+Z", "Editor.Undo", hints, signature);
            if (!DocManager.Inst.HasOpenUndoGroup && DocManager.Inst.GetRedoState(out _) && ((ICommand)_editor.RedoCommand).CanExecute(null))
            {
                AddCommandHint(command + "+Shift+Z", "Editor.Redo", hints, signature);
                AddCommandHint(command + "+Y", "Editor.Redo", hints, signature);
            }
            if (((ICommand)_editor.PlayPauseCommand).CanExecute(null)) AddCommandHint("Space", "Editor.PlayPause", hints, signature);
        }
        private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateModes();
        public void OnNext(UCommand cmd, bool isUndo)
        {
            UpdateModes();
        }
        private ContextMenu CreateContextMenu(bool piano, Func<UPart?>? targetPart = null)
        {
            ContextMenu menu = new();
            menu.Opening += (_, _) =>
            {
                _input.CancelEditorInput();
                if (piano) _editor.PianoRollViewModel.IsContextMenuExpanded = true;
                else _editor.IsContextMenuExpanded = true;
                IReadOnlyList<ContextActionItem> actions = piano ? _editor.PianoRollViewModel.PianoRollContextActions : _editor.TrackContextActions;
                List<MenuItem> items = actions.Where(action => action.Id != "batch-edits")
                    .Select(action => new MenuItem { Header = action.Tip, Command = action.Command }).ToList();
                if (actions.Any(action => action.Id == "batch-edits"))
                    items.Add(DesktopBatchEditMenu.Create(() => _editor.PianoRollViewModel, () => !_editor.IsLoadingProject));
                UPart? target = targetPart?.Invoke();
                int index = target != null && DocManager.Inst.Project.parts.Contains(target) ? target.trackNo : -1;
                if (!piano && index >= 0 && index < DocManager.Inst.Project.tracks.Count && DocManager.Inst.Project.tracks[index].RendererSettings.renderer == Renderers.CLASSIC)
                    items.AddRange(CreateClassicToolMenus(DocManager.Inst.Project.tracks[index]));
                menu.ItemsSource = items;
            };
            return menu;
        }
        private static MenuItem[] CreateClassicToolMenus(UTrack track)
        {
            return new[] { DesktopToolKind.Resampler, DesktopToolKind.Wavtool }.Select(kind =>
            {
                string[] compatible = kind == DesktopToolKind.Resampler
                    ? Renderers.GetSupportedResamplers(track.RendererSettings.Wavtool).Select(t => t.ToString()!).ToArray()
                    : Renderers.GetSupportedWavtools(track.RendererSettings.Resampler).Select(t => t.ToString()!).ToArray();
                MenuItem parent = new() { Header = DesktopUi.Label(kind == DesktopToolKind.Resampler ? "Desktop.Resamplers" : "Desktop.Wavtools") };
                parent.ItemsSource = DesktopToolsService.GetTools(kind).Where(t => t.Available && compatible.Contains(t.Name)).Select(tool =>
                {
                    string? selected = kind == DesktopToolKind.Resampler ? track.RendererSettings.resampler : track.RendererSettings.wavtool;
                    MenuItem item = new() { Header = tool.Name, ToggleType = MenuItemToggleType.Radio, IsChecked = selected == tool.Name };
                    item.Click += (_, _) =>
                    {
                        if (DocManager.Inst.HasOpenUndoGroup || !DocManager.Inst.Project.tracks.Contains(track)) return;
                        URenderSettings settings = track.RendererSettings.Clone();
                        if (kind == DesktopToolKind.Resampler) settings.resampler = tool.Name; else settings.wavtool = tool.Name;
                        DocManager.Inst.StartUndoGroup();
                        try { DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, track, settings)); }
                        finally { DocManager.Inst.EndUndoGroup(); }
                    };
                    return item;
                }).ToArray();
                return parent;
            }).ToArray();
        }
        public void ClipboardAction(Key key)
        {
            _input.ExecuteSelectionAction(key);
        }
        public bool CanClipboardAction(Key key) => _input.CanExecuteSelectionAction(key);
        public void ToggleInspector()
        {
            _layout.State.InspectorVisible = !_layout.State.InspectorVisible;
            if (_layout.State.InspectorVisible && _layout.State.InspectorWidth < 1) _layout.State.InspectorWidth = 320;
            UpdateInspectorVisibility();
            _layout.Save();
        }
        public async void ShowNotes()
        {
            _layout.State.InspectorVisible = true;
            if (_layout.State.InspectorWidth < 260) _layout.State.InspectorWidth = 320;
            UpdateInspectorVisibility();
            if (InspectorHost.IsVisible) { _inspector.SelectNotes(); return; }
            if (_editor.PianoRollViewModel.EditingVoicePart == null || _editor.PianoRollViewModel.SelectedNotes.Count == 0) return;
            try { await _editor.PianoRollViewModel.EditNotePropertiesAsync(); }
            catch (Exception error) { ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(error))); }
        }
        public void ToggleMixer()
        {
            if (_mixerWindow != null) { _mixerWindow.Close(); return; }
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            MixerPanel mixer = new();
            DesktopDensity.ApplyMixer(mixer, _layout.State.MixerFxPaneWidth);
            mixer.Styles.Add(new StyleInclude(new Uri("avares://OpenUtauMobile.DesktopUI/"))
            { Source = new Uri("avares://OpenUtauMobile.DesktopUI/Views/DesktopStyles.axaml") });
            mixer.FindControl<Grid>("MixerHeader")!.IsVisible = false;
            Border detailBorder = mixer.FindControl<Border>("DetailBorder")!;
            DispatcherTimer paneSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
            paneSaveTimer.Tick += (_, _) => { paneSaveTimer.Stop(); if (!_disposed) _layout.Save(); };
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
                if (_disposed) { DialogHostAvalonia.DialogHost.Close(host.Identifier, null); return; }
                e.Cancel = true;
                object? content = DialogHostAvalonia.DialogHost.GetDialogSession(host.Identifier)?.Content;
                (content is ContentControl control ? control.DataContext as IPopupContext : content as IPopupContext)?.RequestBack();
            };
            HashSet<Key> pressed = [];
            window.AddHandler(KeyDownEvent, (_, e) =>
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
        public bool IsArrangementVisible => _layout.State.ArrangementVisible && _layout.State.ArrangementRatio > 0;
        public bool IsPianoRollVisible => _layout.State.PianoRollVisible && _layout.State.ArrangementRatio < 1;
        public bool IsParametersVisible => _layout.State.ParametersVisible && _editor.PianoRollViewModel.PhonemePanelHeight > 0;
        public bool IsInspectorVisible => _layout.State.InspectorVisible;
        public void ToggleArrangement()
        {
            CancelInput(); _layout.State.ArrangementVisible = !IsArrangementVisible;
            if (_layout.State.ArrangementVisible && _layout.State.ArrangementRatio == 0) _layout.State.ArrangementRatio = .28;
            ApplyLayout(); _layout.Save();
        }
        public void TogglePianoRoll()
        {
            CancelInput(); _layout.State.PianoRollVisible = !IsPianoRollVisible;
            if (_layout.State.PianoRollVisible && _layout.State.ArrangementRatio == 1) _layout.State.ArrangementRatio = .28;
            ApplyLayout(); _layout.Save();
        }
        public void ToggleParameters()
        {
            CancelInput(); _layout.State.ParametersVisible = !IsParametersVisible;
            ApplyLayout(); _layout.Save();
        }
        private void ShowParameters()
        {
            _layout.State.PianoRollVisible = _layout.State.ParametersVisible = true;
            if (_layout.State.ArrangementRatio == 1) _layout.State.ArrangementRatio = .28;
            ApplyLayout(); _layout.Save();
        }
        public void ResetLayout()
        {
            _layout.Reset();
            _inspector.ResetSections();
            if (_mixerWindow is { IsVisible: true } mixer && TopLevel.GetTopLevel(this) is Window owner)
            {
                mixer.GetVisualDescendants().OfType<MixerPanel>().FirstOrDefault()?.ResetDesktopFxPaneWidth(288);
                DesktopWindowProfile profile = DesktopWindowProfile.Mixer;
                Size available = owner.Screens.ScreenFromWindow(owner) is { } screen
                    ? new Size(screen.WorkingArea.Width / screen.Scaling, screen.WorkingArea.Height / screen.Scaling)
                    : owner.ClientSize;
                mixer.Width = Math.Min(profile.Width, Math.Max(1, available.Width - 24));
                mixer.Height = Math.Min(profile.Height, Math.Max(1, available.Height - 24));
                DesktopPageLocator.CenterOnOwnerScreen(mixer, owner);
            }
            ApplyLayout();
        }
        private void ApplyLayout()
        {
            _applyingLayout = true;
            try
            {
                bool tracks = IsArrangementVisible, piano = IsPianoRollVisible;
                ArrangementPane.IsVisible = tracks; PianoPane.IsVisible = piano;
                ArrangementSplitter.IsVisible = tracks && piano;
                EditingGrid.RowDefinitions[0].Height = new GridLength(tracks ? piano ? _layout.State.ArrangementRatio : 1 : 0, GridUnitType.Star);
                EditingGrid.RowDefinitions[1].Height = new GridLength(tracks && piano ? 6 : 0);
                EditingGrid.RowDefinitions[2].Height = new GridLength(piano ? tracks ? 1 - _layout.State.ArrangementRatio : 1 : 0, GridUnitType.Star);
                _editor.PianoRollViewModel.PhonemePanelHeight = _layout.State.ParametersVisible ? _layout.State.ParameterHeight : 0;
                WorkspaceGrid.ColumnDefinitions[2].Width = new GridLength(_layout.State.InspectorWidth);
                UpdateInspectorVisibility();
            }
            finally { _applyingLayout = false; }
        }
        private void UpdateInspectorVisibility()
        {
            bool visible = _layout.State.InspectorVisible && Bounds.Width >= 960;
            InspectorHost.IsVisible = InspectorSplitter.IsVisible = visible;
            WorkspaceGrid.ColumnDefinitions[2].MinWidth = 0;
            WorkspaceGrid.ColumnDefinitions[1].Width = new GridLength(visible ? 6 : 0);
            WorkspaceGrid.ColumnDefinitions[2].Width = new GridLength(visible ? Math.Min(_layout.State.InspectorWidth, Math.Max(0, Bounds.Width - 640)) : 0);
        }
        private void SavePanels()
        {
            if (!_attached || Bounds.Height <= 0) return;
            double tracks = EditingGrid.RowDefinitions[0].ActualHeight;
            double piano = EditingGrid.RowDefinitions[2].ActualHeight;
            if (_layout.State.ArrangementVisible && _layout.State.PianoRollVisible && tracks + piano > 0) _layout.State.ArrangementRatio = Math.Clamp(tracks / (tracks + piano), 0, 1);
            if (InspectorHost.IsVisible) _layout.State.InspectorWidth = Math.Clamp(InspectorHost.Bounds.Width, 0, Math.Max(0, Bounds.Width - 640));
            _layout.Save();
        }
        private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_applyingLayout && e.PropertyName == nameof(PianoRollViewModel.PhonemePanelHeight))
            {
                double height = _editor.PianoRollViewModel.PhonemePanelHeight;
                _layout.State.ParametersVisible = height > 0;
                if (height > 0) _layout.State.ParameterHeight = height;
            }
            if (e.PropertyName is nameof(EditorViewModel.TrackEditMode) or nameof(EditorViewModel.IsLoadingProject) or nameof(EditorViewModel.IsPlaying) or nameof(PianoRollViewModel.EditMode) or nameof(PianoRollViewModel.IsPitchEraserMode) or nameof(PianoRollViewModel.EditingTip) or nameof(PianoRollViewModel.EditingVoicePart)) UpdateModes();
            if (e.PropertyName == nameof(EditorViewModel.PlayPosTick)) UpdateTime();
            if (e.PropertyName == nameof(EditorViewModel.IsTrackHeaderExpanded))
                Arrangement.SetHeaderExpanded(_editor.IsTrackHeaderExpanded);
        }
        private void OnBatchEditRunningChanged(bool running)
        {
            IsEnabled = !running;
            if (_mixerWindow?.Content is DialogHostAvalonia.DialogHost host) host.IsEnabled = !running;
        }
        private void UpdateTime()
        {
            double ms = DocManager.Inst.Project.timeAxis.TickPosToMsPos(_editor.PlayPosTick);
            TimeDisplay.Text = TimeSpan.FromMilliseconds(Math.Max(0, ms)).ToString(@"mm\:ss\.fff");
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            PianoRollViewModel.BatchEditRunningChanged -= OnBatchEditRunningChanged;
            _inspector.RequestShowParameters -= ShowParameters;
            _mixerWindow?.Close();
            SavePanels();
            _editor.PropertyChanged -= OnEditorChanged;
            _editor.PianoRollViewModel.PropertyChanged -= OnEditorChanged;
            DocManager.Inst.RemoveSubscriber(this);
            _editor.SelectedParts.CollectionChanged -= OnSelectionChanged;
            _editor.PianoRollViewModel.SelectedNotes.CollectionChanged -= OnSelectionChanged;
            _editor.PianoRollViewModel.SelectedAnchors.CollectionChanged -= OnSelectionChanged;
            foreach (DesktopTimelineInput timeline in _timelines) timeline.Dispose();
            foreach (DesktopTempoEditor tempo in _tempos) tempo.Dispose();
            _hintBinding?.Dispose();
            _inline.Dispose();
            _input.Dispose();
            _editor.UseDesktopInput = false;
            _editor.PianoRollViewModel.UseDesktopMouseInput = false;
            _inspector.Dispose();
        }
    }
}
