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
        private readonly EditorInputController _input;
        private readonly DesktopInspector _inspector;
        private readonly DesktopWorkspaceLayoutController _layoutController;
        private readonly DesktopInlineEditor _inline;
        private readonly DesktopMixerWindow _mixer;
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
            _mixer = new DesktopMixerWindow(editor, layout, () => _disposed);
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
            _layoutController = new DesktopWorkspaceLayoutController(editor, layout, WorkspaceGrid, EditingGrid,
                ArrangementPane, PianoPane, ArrangementSplitter, InspectorHost, InspectorSplitter, _inspector,
                _mixer, () => _attached, () => Bounds.Size, CancelInput);
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
                MenuItem parent = new() { Header = DesktopUi.Label(kind == DesktopToolKind.Resampler ? "Desktop.Resamplers" : "Desktop.Wavtools") };
                parent.ItemsSource = DesktopTrackToolActions.GetCompatibleTools(track, kind).Select(tool =>
                {
                    string? selected = kind == DesktopToolKind.Resampler ? track.RendererSettings.resampler : track.RendererSettings.wavtool;
                    MenuItem item = new() { Header = tool.Name, ToggleType = MenuItemToggleType.Radio, IsChecked = selected == tool.Name };
                    item.Click += (_, _) =>
                    {
                        DesktopTrackToolActions.TryApply(track, tool);
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
            => _layoutController.ToggleInspector();
        public async void ShowNotes()
        {
            _layoutController.ShowInspector();
            if (InspectorHost.IsVisible) { _inspector.SelectNotes(); return; }
            if (_editor.PianoRollViewModel.EditingVoicePart == null || _editor.PianoRollViewModel.SelectedNotes.Count == 0) return;
            try { await _editor.PianoRollViewModel.EditNotePropertiesAsync(); }
            catch (Exception error) { ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(error))); }
        }
        public void ToggleMixer() => _mixer.Toggle(this);
        public bool IsArrangementVisible => _layoutController.IsArrangementVisible;
        public bool IsPianoRollVisible => _layoutController.IsPianoRollVisible;
        public bool IsParametersVisible => _layoutController.IsParametersVisible;
        public bool IsInspectorRequested => _layoutController.IsInspectorRequested;
        public void ToggleArrangement() => _layoutController.ToggleArrangement();
        public void TogglePianoRoll() => _layoutController.TogglePianoRoll();
        public void ToggleParameters() => _layoutController.ToggleParameters();
        private void ShowParameters() => _layoutController.ShowParameters();
        public void ResetLayout() => _layoutController.ResetLayout(this);
        private void ApplyLayout() => _layoutController.ApplyLayout();
        private void UpdateInspectorVisibility() => _layoutController.UpdateInspectorVisibility();
        private void SavePanels() => _layoutController.SavePanels();
        private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
        {
            _layoutController.OnEditorChanged(e);
            if (e.PropertyName is nameof(EditorViewModel.TrackEditMode) or nameof(EditorViewModel.IsLoadingProject) or nameof(EditorViewModel.IsPlaying) or nameof(PianoRollViewModel.EditMode) or nameof(PianoRollViewModel.IsPitchEraserMode) or nameof(PianoRollViewModel.EditingTip) or nameof(PianoRollViewModel.EditingVoicePart)) UpdateModes();
            if (e.PropertyName == nameof(EditorViewModel.PlayPosTick)) UpdateTime();
            if (e.PropertyName == nameof(EditorViewModel.IsTrackHeaderExpanded))
                Arrangement.SetHeaderExpanded(_editor.IsTrackHeaderExpanded);
        }
        private void OnBatchEditRunningChanged(bool running)
        {
            IsEnabled = !running;
            _mixer.SetBatchEditRunning(running);
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
            _mixer.Dispose();
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
