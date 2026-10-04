using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Globalization;
using System.Reactive.Linq;
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
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    internal sealed class DesktopInspector : UserControl, ICmdSubscriber, IDisposable
    {
        private readonly EditorViewModel _editor;
        private readonly DesktopLayoutStore _layout;
        public event Action? RequestShowParameters;
        private readonly List<IDisposable> _paletteSubscriptions = [];
        private readonly ScrollViewer _scroll;
        private readonly Expander _notesSection = new();
        private readonly ContentControl _trackHost = new();
        private readonly ContentControl _notesHost = new();
        private readonly ContentControl _phonemeHost = new();
        private NotePropertyEditor? _draft;
        private UVoicePart? _part;
        private UNote[] _notes = [];
        private TrackHeaderViewModel? _trackVm;
        private UTrack? _track;
        private UNote? _phonemeNote;
        private int _phonemeIndex;
        private string? _renderSettingsKey;
        private bool _queued;
        private bool _notesChangedExternally;
        private bool _phonemesChanged = true;
        private bool _refreshingNotes;
        private StackPanel? _noteControls;
        private string? _noteSchema;
        private readonly List<Action<NotePropertyEditor>> _noteRebind = [];
        private readonly List<DesktopExpressionSlider> _expressionSliders = [];
        private bool _attached;
        private bool _disposed;
        private bool _committing;
        private bool _ownsExpressionUndoGroup;
        private NotePropertyEditor? _liveExpressionDraft;
        private NotePropertyField? _liveExpressionField;
        private decimal? _liveExpressionValue;
        private IDisposable? _languageSubscription;

        public DesktopInspector(EditorViewModel editor, DesktopLayoutStore layout)
        {
            _editor = editor;
            _layout = layout;
            Classes.Add("DesktopInspector");
            MinWidth = 260;
            Resources["TextControlThemeMinHeight"] = 28d;
            Resources["TextControlThemePadding"] = new Thickness(6, 3);
            Resources["ButtonSpinnerButtonMinWidth"] = 24d;
            Resources["ExpanderMinHeight"] = 30d;
            Resources["ExpanderChevronButtonSize"] = 24d;
            Resources["ExpanderChevronMargin"] = new Thickness(4, 0, 4, 0);
            Resources["ExpanderHeaderPadding"] = new Thickness(8, 0, 0, 0);
            Resources["ExpanderContentPadding"] = new Thickness(8);
            _notesSection.Header = DesktopUi.Label("Desktop.Notes");
            _notesSection.Content = _notesHost;
            RememberSection(_notesSection, "Notes", true);
            Content = _scroll = DesktopUi.Scroll(new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Section("Track", DesktopUi.Label("Desktop.Track"), _trackHost, true),
                    _notesSection,
                    Section("Phoneme", DesktopUi.Label("Desktop.Phoneme"), _phonemeHost, false)
                }
            });
            _notesSection.HorizontalAlignment = HorizontalAlignment.Stretch;
            AttachedToVisualTree += (_, _) => Attach();
            DetachedFromVisualTree += (_, _) => Detach();
            LostFocus += (_, _) => QueueRefresh();
        }
        private Expander Section(string key, object header, Control content, bool expanded)
        {
            Expander section = new() { Header = header, Content = content, HorizontalAlignment = HorizontalAlignment.Stretch };
            RememberSection(section, key, expanded);
            return section;
        }
        private void RememberSection(Expander section, string key, bool expanded)
        {
            section.Tag = key;
            section.IsExpanded = _layout.State.InspectorSections.GetValueOrDefault(key, expanded);
            section.PropertyChanged += (_, e) =>
            {
                if (e.Property != Expander.IsExpandedProperty) return;
                _layout.State.InspectorSections[key] = section.IsExpanded;
                _layout.Save();
            };
        }
        public void ResetSections()
        {
            foreach (Expander section in this.GetVisualDescendants().OfType<Expander>().ToArray())
            {
                if (section.Tag is string key) section.IsExpanded = key is "Track" or "Notes" or "Notes.Basic";
            }
        }
        public bool CommitPendingInput()
        {
            foreach (DesktopExpressionSlider slider in _expressionSliders.ToArray())
                if (!slider.CommitPendingEdit()) return false;
            if (_draft != null && !CommitCurrentDraft()) return false;
            return string.IsNullOrEmpty(_draft?.Error) && !this.GetVisualDescendants().OfType<Control>().Any(DataValidationErrors.GetHasErrors);
        }
        public void SelectNotes()
        {
            _notesSection.IsExpanded = true;
            Dispatcher.UIThread.Post(() =>
            {
                _notesSection.BringIntoView();
                if (_scroll.Content is Visual content && _notesSection.TranslatePoint(default, content) is Point point)
                    _scroll.Offset = new Vector(0, Math.Clamp(point.Y, 0, Math.Max(0, _scroll.Extent.Height - _scroll.Viewport.Height)));
            }, DispatcherPriority.Loaded);
        }
        private void Attach()
        {
            if (_attached || _disposed) return;
            _attached = true;
            foreach ((string role, string semantic) in new[]
            {
                ("ExpanderContentBackground", "Sem.Color.SurfaceContainerLow"),
                ("ExpanderContentBorderBrush", "Sem.Color.OutlineVariant"),
                ("ExpanderHeaderBackground", "Sem.Color.SurfaceContainer"),
                ("ExpanderHeaderBackgroundPointerOver", "Sem.Color.SurfaceContainerHigh"),
                ("ExpanderHeaderBackgroundPressed", "Sem.Color.SurfaceContainerHighest"),
                ("ExpanderHeaderBorderBrush", "Sem.Color.OutlineVariant")
            }) _paletteSubscriptions.Add(this.GetResourceObservable(semantic).Subscribe(value => { if (value != null && (!Resources.TryGetValue(role, out object? existing) || !Equals(existing, value))) Resources[role] = value; }));
            DocManager.Inst.AddSubscriber(this);
            _editor.PropertyChanged += OnEditorChanged;
            _editor.PianoRollViewModel.PropertyChanged += OnEditorChanged;
            _editor.PianoRollViewModel.SelectedNotes.CollectionChanged += OnSelectionChanged;
            _editor.SelectedParts.CollectionChanged += OnSelectionChanged;
            DesktopToolsService.Instance.Changed += OnToolsChanged;
            _languageSubscription = this.GetResourceObservable("Desktop.Notes").Subscribe(_ => { _renderSettingsKey = null; _notesChangedExternally = true; _phonemesChanged = true; QueueRefresh(); });
            QueueRefresh();
        }
        private void Detach()
        {
            if (!_attached) return;
            CancelExpressionSliderPreviews();
            _attached = false;
            foreach (IDisposable subscription in _paletteSubscriptions) subscription.Dispose();
            _paletteSubscriptions.Clear();
            _languageSubscription?.Dispose();
            _languageSubscription = null;
            DocManager.Inst.RemoveSubscriber(this);
            _editor.PropertyChanged -= OnEditorChanged;
            _editor.PianoRollViewModel.PropertyChanged -= OnEditorChanged;
            _editor.PianoRollViewModel.SelectedNotes.CollectionChanged -= OnSelectionChanged;
            _editor.SelectedParts.CollectionChanged -= OnSelectionChanged;
            DesktopToolsService.Instance.Changed -= OnToolsChanged;
        }
        private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(PianoRollViewModel.EditingVoicePart) or nameof(PianoRollViewModel.EditingWavePart))
            {
                CancelExpressionSliderPreviews();
                QueueRefresh();
            }
        }
        private void OnToolsChanged() { _renderSettingsKey = null; QueueRefresh(); }
        private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            CancelExpressionSliderPreviews();
            QueueRefresh();
        }
        private void CancelExpressionSliderPreviews()
        {
            foreach (DesktopExpressionSlider slider in _expressionSliders) slider.CancelPendingEdit();
        }
        public void OnNext(UCommand cmd, bool isUndo)
        {
            if (cmd is SetPlayPosTickNotification or SeekPlayPosTickNotification or ProgressBarNotification) return;
            if (cmd is MixCommand) { _trackVm?.RefreshMix(); return; }
            if (!_committing && (cmd is not UNotification || cmd is LoadProjectNotification))
            {
                _notesChangedExternally = true;
                CancelExpressionSliderPreviews();
            }
            // 音素生成结果也会改变继承的表达式值，不能只刷新音素面板。
            if (cmd is PhonemizedNotification phonemized && phonemized.part == _part)
                _notesChangedExternally = true;
            if (cmd is NoteCommand or PartCommand or PhonemizedNotification or TrackCommand or LoadProjectNotification) _phonemesChanged = true;
            if (cmd is not UNotification || cmd is LoadProjectNotification or PhonemizedNotification or PreRenderNotification) QueueRefresh();
        }
        private void QueueRefresh()
        {
            if (_queued || _disposed) return;
            _queued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _queued = false;
                if (!_attached || _disposed || DocManager.Inst.HasOpenUndoGroup) return;
                RefreshTrack();
                // 输入中的草稿保留；选择改变则立即失效，不能写入下一次选择。
                bool same = SelectionMatches();
                if (!same || _notesChangedExternally && !HasOpenPicker(_notesHost) && !HasPendingDraftInput()) RefreshNotes();
                if (!same) _phonemesChanged = true;
                if (_phonemesChanged && !HasPhonemeInputFocus()) RefreshPhonemes();
            }, DispatcherPriority.Background);
        }
        private bool HasDraftInputFocus() => HasOpenPicker(_notesHost) || TopLevel.GetTopLevel(this)?.FocusManager.GetFocusedElement() is Visual visual && visual.GetSelfAndVisualAncestors().Contains(_notesHost);
        private bool HasPhonemeInputFocus() => HasOpenPicker(_phonemeHost) || TopLevel.GetTopLevel(this)?.FocusManager.GetFocusedElement() is Visual visual && visual.GetSelfAndVisualAncestors().Contains(_phonemeHost);
        private bool HasTrackInputFocus() => TopLevel.GetTopLevel(this)?.FocusManager.GetFocusedElement() is Visual visual && visual.GetSelfAndVisualAncestors().Contains(_trackHost) && (visual.GetSelfAndVisualAncestors().Any(v => v is TextBox) || EditorInputController.IsComposing(visual));
        private static bool HasOpenPicker(Control host) => host.GetVisualDescendants().OfType<ComboBox>().Any(picker => picker.IsDropDownOpen);
        private bool HasPendingDraftInput() => _draft != null && _draft.Groups.SelectMany(group => group.Fields.Concat(group.Vibrato == null ? [] : new[] { group.Vibrato.Duration }))
            .Any(field => field.IsEdited || field.HasInvalidInput || field.IsNumber && field.NumberText != field.Number?.ToString(CultureInfo.CurrentCulture));
        private bool SelectionMatches() => _part == _editor.PianoRollViewModel.EditingVoicePart && _notes.SequenceEqual(_editor.PianoRollViewModel.SelectedNotes.OrderBy(n => n.position).ThenBy(n => n.tone));
        private void RefreshNotes()
        {
            foreach (DesktopExpressionSlider slider in _expressionSliders) slider.CancelPendingEdit();
            _notesChangedExternally = false;
            _refreshingNotes = true;
            NotePropertyEditor? old = _draft;
            try
            {
                _part = _editor.PianoRollViewModel.EditingVoicePart;
                _notes = _editor.PianoRollViewModel.SelectedNotes.OrderBy(n => n.position).ThenBy(n => n.tone).ToArray();
                _draft = _part == null || _notes.Length == 0 ? null : new NotePropertyEditor(_part, _notes);
                if (_draft == null) { _expressionSliders.Clear(); _noteControls = null; _noteSchema = null; _notesHost.Content = DesktopUi.Label("Desktop.NoNotes"); return; }
                NotePropertyEditor draft = _draft;
                string schema = string.Join("|", draft.Groups.Select(group => $"{group.Key}:{group.Title}:{group.EmptyMessage}:{group.Vibrato != null}:{group.Presets != null}:" +
                    string.Join(",", group.Fields.Select(field => $"{field.Label}:{field.IsNumber}:{field.IsChoice}:{field.CanReset}:{group.Key == "Expressions" && field.IsNumber}"))));
                // 选择改变时复用控件和模板，只替换草稿绑定，避免大量分配打断音频回调。
                if (_noteControls != null && _noteSchema == schema)
                {
                    foreach (Action<NotePropertyEditor> rebind in _noteRebind) rebind(draft);
                    _notesHost.Content = _noteControls;
                    return;
                }
                _noteSchema = schema;
                _noteRebind.Clear();
                _expressionSliders.Clear();
                StackPanel sections = new() { Spacing = 6, DataContext = draft };
                _noteControls = sections;
                _noteRebind.Add(next => sections.DataContext = next);
                TextBlock summary = new(); summary.Bind(TextBlock.TextProperty, new Binding("Summary")); sections.Children.Add(summary);
                TextBlock error = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                error.Bind(TextBlock.TextProperty, new Binding("Error"));
                error.Bind(IsVisibleProperty, new Binding("Error") { Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty });
                DesktopUi.Paint(error, TextBlock.ForegroundProperty, "Sem.Color.Error");
                sections.Children.Add(error);
                for (int index = 0; index < draft.Groups.Count; index++)
                {
                    int groupIndex = index;
                    NotePropertyGroup group = draft.Groups[index];
                    StackPanel body = new() { Spacing = 6 };
                    if (group.Vibrato is { } vibrato)
                    {
                        StackPanel toggles = new() { Spacing = 6, DataContext = vibrato };
                        _noteRebind.Add(next => { vibrato = next.Groups[groupIndex].Vibrato!; toggles.DataContext = vibrato; });
                        CheckBox enabled = new() { Content = DesktopUi.Label("NoteProperties.VibratoEnabled"), IsThreeState = true };
                        enabled.Bind(CheckBox.IsCheckedProperty, new Binding("Enabled") { Mode = BindingMode.OneWay });
                        enabled.IsCheckedChanged += (_, _) => { if (_refreshingNotes) return; vibrato.Enabled = enabled.IsChecked; CommitCurrentDraft(); };
                        toggles.Children.Add(enabled);
                        CheckBox automatic = new() { Content = DesktopUi.Label("NoteProperties.AutoVibrato") };
                        automatic.Bind(CheckBox.IsCheckedProperty, new Binding("AutoEnabled") { Mode = BindingMode.OneWay });
                        automatic.IsCheckedChanged += (_, _) => { if (_refreshingNotes) return; vibrato.AutoEnabled = automatic.IsChecked == true; CommitCurrentDraft(); };
                        toggles.Children.Add(automatic);
                        body.Children.Add(toggles);
                        body.Children.Add(CreateField(vibrato.Duration, next => next.Groups[groupIndex].Vibrato!.Duration));
                    }
                    for (int fieldIndex = 0; fieldIndex < group.Fields.Count; fieldIndex++)
                    {
                        int currentField = fieldIndex;
                        body.Children.Add(CreateField(group.Fields[fieldIndex], next => next.Groups[groupIndex].Fields[currentField], group.Key == "Expressions"));
                    }
                    if (group.Key == "Expressions")
                    {
                        ComboBox expressionLane = new() { Name = "InspectorExpressionLane", HorizontalAlignment = HorizontalAlignment.Stretch };
                        expressionLane.Bind(ItemsControl.ItemsSourceProperty, new Binding("AvailableExpressions") { Source = _editor.PianoRollViewModel });
                        expressionLane.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ExpressionOption>((value, _) => new TextBlock { Text = value?.Descriptor?.ToString() ?? value?.DisplayName });
                        ToolTip.SetTip(expressionLane, DesktopUi.Label("Desktop.EditExpressionLane"));
                        expressionLane.SelectionChanged += (_, _) =>
                        {
                            if (expressionLane.SelectedItem is not ExpressionOption expression) return;
                            _editor.PianoRollViewModel.PrimaryExpressionKey = expression.Key;
                            _editor.PianoRollViewModel.PhonemePanelMode = PhonemePanelMode.ParameterDraw;
                            RequestShowParameters?.Invoke();
                        };
                        body.Children.Add(DesktopUi.Label("Desktop.EditExpressionLane")); body.Children.Add(expressionLane);
                    }
                    if (group.Presets is { } presets)
                    {
                        StackPanel presetHost = new() { Spacing = 6, DataContext = presets };
                        _noteRebind.Add(next => { presets = next.Groups[groupIndex].Presets!; presetHost.DataContext = presets; });
                        ComboBox picker = new() { Name = "NotePreset" + (group.Vibrato != null ? "Vibrato" : "Portamento"), HorizontalAlignment = HorizontalAlignment.Stretch };
                        picker.Bind(ItemsControl.ItemsSourceProperty, new Binding("Items"));
                        picker.Bind(ComboBox.PlaceholderTextProperty, picker.GetResourceObservable("NoteProperties.Choose"));
                        ToolTip.SetTip(picker, DesktopUi.Label("NoteProperties.Preset"));
                        picker.Bind(SelectingItemsControl.SelectedItemProperty, new Binding("Selected") { Mode = BindingMode.TwoWay });
                        picker.SelectionChanged += (_, _) => { if (!_refreshingNotes && picker.SelectedItem != null) { presets.Selected = picker.SelectedItem; CommitCurrentDraft(); } };
                        presetHost.Children.Add(picker);
                        TextBox name = new(); name.Bind(TextBox.TextProperty, new Binding("Name") { Mode = BindingMode.TwoWay });
                        name.Bind(TextBox.PlaceholderTextProperty, name.GetResourceObservable("NoteProperties.PresetName"));
                        name.Bind(AutomationProperties.NameProperty, name.GetResourceObservable("NoteProperties.PresetName"));
                        presetHost.Children.Add(name);
                        Button save = DesktopUi.Action("NoteProperties.SavePreset", () =>
                        {
                            if (_refreshingNotes || !SelectionMatches()) return;
                            presets.SaveCommand.Execute().Subscribe();
                            if (presets.Commit()) NotePresets.Save();
                        });
                        Button remove = DesktopUi.Action("NoteProperties.RemovePreset", () =>
                        {
                            if (_refreshingNotes || !SelectionMatches() || presets.Selected == null) return;
                            presets.RemoveCommand.Execute().Subscribe();
                            if (presets.Commit()) NotePresets.Save();
                        });
                        presetHost.Children.Add(new WrapPanel { ItemSpacing = 6, LineSpacing = 6, Children = { save, remove } });
                        TextBlock presetError = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
                        presetError.Bind(TextBlock.TextProperty, new Binding("Error"));
                        presetError.Bind(IsVisibleProperty, new Binding("Error") { Converter = Avalonia.Data.Converters.StringConverters.IsNotNullOrEmpty });
                        DesktopUi.Paint(presetError, TextBlock.ForegroundProperty, "Sem.Color.Error");
                        presetHost.Children.Add(presetError);
                        body.Children.Add(Section("Notes." + group.Key + ".Presets", DesktopUi.Label("NoteProperties.Preset"), presetHost, false));
                    }
                    if (!string.IsNullOrEmpty(group.EmptyMessage)) body.Children.Add(new TextBlock { Text = group.EmptyMessage, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
                    sections.Children.Add(Section("Notes." + group.Key, group.Title, body, group.Key == "Basic"));
                }
                _notesHost.Content = sections;
            }
            finally { old?.Dispose(); _refreshingNotes = false; }
        }
        private bool CommitCurrentDraft()
        {
            return !_refreshingNotes && _draft is { } draft ? CommitDraft(draft) : false;
        }
        private Control CreateField(NotePropertyField field, Func<NotePropertyEditor, NotePropertyField> select, bool addExpressionSlider = false)
        {
            Grid row = new()
            {
                ColumnDefinitions = new ColumnDefinitions(addExpressionSlider ? "*,Auto" : "100,*"),
                RowDefinitions = new RowDefinitions(addExpressionSlider ? "Auto,Auto" : "Auto"),
                ColumnSpacing = 6,
                RowSpacing = addExpressionSlider ? 2 : 0,
                DataContext = field
            };
            DesktopExpressionSlider? expressionSlider = null;
            _noteRebind.Add(next => { field = select(next); row.DataContext = field; expressionSlider?.Rebind(field); });
            row.Bind(IsEnabledProperty, new Binding("IsEnabled"));
            row.Bind(ToolTip.TipProperty, new Binding("Hint")
            {
                Converter = new Avalonia.Data.Converters.FuncValueConverter<string?, string?>(hint => string.IsNullOrWhiteSpace(hint) ? null : hint)
            });
            ToolTip.SetShowOnDisabled(row, true);
            row.Children.Add(new TextBlock { Text = field.Label, FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            Control input;
            if (field.IsChoice)
            {
                ComboBox choice = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
                choice.Bind(ItemsControl.ItemsSourceProperty, new Binding("Options"));
                choice.Bind(SelectingItemsControl.SelectedIndexProperty, new Binding("SelectedIndex") { Mode = BindingMode.OneWay });
                choice.Bind(ComboBox.PlaceholderTextProperty, choice.GetResourceObservable("NoteProperties.Mixed"));
                choice.SelectionChanged += (_, _) => { if (_refreshingNotes) return; field.SelectedIndex = choice.SelectedIndex; if (field.IsEdited) CommitCurrentDraft(); };
                input = choice;
            }
            else if (field.IsNumber)
            {
                if (addExpressionSlider)
                {
                    TextBox number = new();
                    number.Bind(TextBox.PlaceholderTextProperty, number.GetResourceObservable("NoteProperties.Mixed"));
                    input = number;
                    expressionSlider = new DesktopExpressionSlider(field, number,
                        CommitExpressionSlider,
                        () => IsExpressionDraftCurrent(field),
                        PreviewExpressionSlider);
                    _expressionSliders.Add(expressionSlider);
                }
                else
                {
                    NumericUpDown number = new() { Increment = field.Increment };
                    number.Bind(NumericUpDown.IncrementProperty, new Binding("Increment"));
                    number.Bind(NumericUpDown.MinimumProperty, new Binding("Minimum"));
                    number.Bind(NumericUpDown.MaximumProperty, new Binding("Maximum"));
                    number.Bind(NumericUpDown.ValueProperty, new Binding("Number") { Mode = BindingMode.TwoWay });
                    number.Bind(NumericUpDown.TextProperty, new Binding("NumberText") { Mode = BindingMode.TwoWay });
                    number.Bind(NumericUpDown.PlaceholderTextProperty, number.GetResourceObservable("NoteProperties.Mixed"));
                    input = number;
                }
            }
            else
            {
                TextBox text = new();
                text.Bind(TextBox.TextProperty, new Binding("Text") { Mode = BindingMode.TwoWay });
                text.Bind(TextBox.PlaceholderTextProperty, text.GetResourceObservable("NoteProperties.Mixed"));
                input = text;
            }
            input.LostFocus += (_, _) => { if (!_expressionSliders.Any(slider => slider.IsEditing) && (field.IsEdited || field.HasInvalidInput)) CommitCurrentDraft(); };
            input.AddHandler(KeyDownEvent, (_, e) =>
            {
                if (EditorInputController.IsComposing(e.Source as Visual)) return;
                if (e.Key == Key.Escape) { e.Handled = true; RefreshNotes(); }
                else if (e.Key == Key.Enter) { e.Handled = true; CommitCurrentDraft(); }
            }, RoutingStrategies.Bubble);
            StackPanel inputs = new() { Spacing = 2 };
            if (addExpressionSlider)
            {
                Grid.SetRow(inputs, 1);
                Grid.SetColumnSpan(inputs, 2);
            }
            else Grid.SetColumn(inputs, 1);
            inputs.Children.Add(expressionSlider ?? input);
            row.Children.Add(inputs);
            if (field.CanReset)
            {
                Button reset = DesktopUi.Action("NoteProperties.Reset", () =>
                {
                    if (_refreshingNotes || !SelectionMatches()) return;
                    NotePropertyEditor? resetDraft = _draft;
                    field.ResetCommand.Execute().Subscribe(_ =>
                    {
                        if (resetDraft == null || resetDraft != _draft || !SelectionMatches()) return;
                        // 重置完成后立即读取实际值，不能依赖可能被输入保护跳过的延迟刷新。
                        if (CommitDraft(resetDraft)) RefreshNotes();
                    });
                });
                reset.Classes.Add("DesktopInspectorReset");
                if (addExpressionSlider)
                {
                    reset.Content = new PackIconPhosphorIcons { Kind = PackIconPhosphorIconsKind.ArrowCounterClockwise, Width = 14, Height = 14 };
                    reset.Width = 28;
                    reset.Padding = new Thickness(4);
                    reset.HorizontalAlignment = HorizontalAlignment.Right;
                    reset.Bind(ToolTip.TipProperty, reset.GetResourceObservable("NoteProperties.Reset"));
                    reset.Bind(AutomationProperties.NameProperty, reset.GetResourceObservable("NoteProperties.Reset"));
                    Grid.SetColumn(reset, 1);
                    row.Children.Add(reset);
                }
                else
                {
                    reset.HorizontalAlignment = HorizontalAlignment.Stretch;
                    reset.HorizontalContentAlignment = HorizontalAlignment.Center;
                    inputs.Children.Add(reset);
                }
            }
            return row;
        }
        private bool CommitExpressionSlider(NotePropertyField field, decimal value)
        {
            if (!PreviewExpressionSlider(field, value))
            {
                FinishLiveExpressionEdit(false);
                return false;
            }
            return FinishLiveExpressionEdit(true);
        }
        private bool IsExpressionDraftCurrent(NotePropertyField field)
        {
            return !_refreshingNotes && _draft is { } draft && _part is { } part && SelectionMatches() &&
                draft.MatchesCurrentSelection(DocManager.Inst.Project, part, _editor.PianoRollViewModel.SelectedNotes) &&
                draft.Groups.SelectMany(group => group.Fields).Contains(field) &&
                (!DocManager.Inst.HasOpenUndoGroup || _ownsExpressionUndoGroup && _liveExpressionDraft == draft && _liveExpressionField == field);
        }
        private bool PreviewExpressionSlider(NotePropertyField field, decimal? value)
        {
            if (!value.HasValue)
            {
                return !_ownsExpressionUndoGroup || FinishLiveExpressionEdit(false);
            }
            if (!IsExpressionDraftCurrent(field) || _draft is not { } draft || field.ExpressionKey == null) return false;
            if (!_ownsExpressionUndoGroup && HasPendingDraftInput() && !CommitDraft(draft)) return false;
            if (!_ownsExpressionUndoGroup && field.Number == value)
            {
                return true;
            }
            if (_ownsExpressionUndoGroup && _liveExpressionValue == value) return true;
            UCommand? command = draft.CreateExpressionEditCommand(field, value.Value);
            if (command == null) return false;
            _committing = true;
            try
            {
                if (!_ownsExpressionUndoGroup)
                {
                    DocManager.Inst.StartUndoGroup();
                    _ownsExpressionUndoGroup = true;
                    _liveExpressionDraft = draft;
                    _liveExpressionField = field;
                }
                DocManager.Inst.ExecuteCmd(command);
                _liveExpressionValue = value;
                return true;
            }
            catch (Exception exception)
            {
                Serilog.Log.Error(exception, "Failed to update expression during inspector slider drag");
                FinishLiveExpressionEdit(false);
                return false;
            }
            finally { _committing = false; }
        }
        private bool FinishLiveExpressionEdit(bool apply)
        {
            if (!_ownsExpressionUndoGroup) return true;
            bool wasCommitting = _committing;
            _committing = true;
            bool completed = false;
            try
            {
                if (DocManager.Inst.HasOpenUndoGroup)
                {
                    if (!apply) DocManager.Inst.RollBackUndoGroup();
                    DocManager.Inst.EndUndoGroup();
                    completed = true;
                }
            }
            catch (Exception exception)
            {
                Serilog.Log.Error(exception, "Failed to finish inspector expression drag");
            }
            finally
            {
                _ownsExpressionUndoGroup = false;
                _liveExpressionDraft = null;
                _liveExpressionField = null;
                _liveExpressionValue = null;
                _committing = wasCommitting;
            }
            if (completed && apply) RefreshNotes();
            return completed;
        }
        private bool CommitDraft(NotePropertyEditor draft)
        {
            if (_refreshingNotes || _committing || draft != _draft || draft.Groups.Any(group => group.Presets?.IsApplying == true) || !SelectionMatches() || DocManager.Inst.HasOpenUndoGroup) return false;
            _committing = true;
            try
            {
                bool committed = draft.Commit(accept: true);
                if (committed)
                {
                    _notesChangedExternally = true;
                    QueueRefresh();
                }
                return committed;
            }
            finally { _committing = false; }
        }
        private void RefreshTrack()
        {
            UProject project = DocManager.Inst.Project;
            int index = _editor.PianoRollViewModel.EditingVoicePart?.trackNo ?? _editor.PianoRollViewModel.EditingWavePart?.trackNo ?? _editor.SelectedParts.LastOrDefault()?.trackNo ?? 0;
            UTrack? track = index >= 0 && index < project.tracks.Count ? project.tracks[index] : null;
            string? renderKey = track == null ? null : $"{track.Singer?.Id ?? track.Singer?.Name}|{track.RendererSettings.renderer}|{track.RendererSettings.resampler}|{track.RendererSettings.wavtool}";
            if (_track == track && _trackVm != null && _renderSettingsKey == renderKey) { _trackVm.Refresh(); return; }
            _renderSettingsKey = renderKey;
            _trackVm?.Dispose(); _trackVm = null; _track = track;
            if (track == null) { _trackHost.Content = DesktopUi.Label("Desktop.NoTrack"); return; }
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
            USinger[] singers = SingerManager.Inst.Singers.Values.Concat(track.Singer != null ? new[] { track.Singer } : Array.Empty<USinger>()).DistinctBy(s => s.Id ?? s.Name).OrderBy(s => s.Name).ToArray();
            TrackHeaderViewModel singerVm = _trackVm;
            ComboBox singer = new() { Name = "TrackSingerPicker", ItemsSource = singers, SelectedItem = track.Singer, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
            singer.Classes.Add("DesktopTrackChoice");
            singer.MinHeight = 72;
            singer.Padding = new Thickness(8);
            singer.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<USinger>((value, _) => value == null ? null : new DesktopSingerItem(value, 56));
            singer.SelectionBoxItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate(value => value is null or USinger, (value, _) => new DesktopSingerItem(value as USinger, 56));
            singer.SelectionChanged += (_, _) => { if (singerVm == _trackVm && singer.SelectedItem is USinger chosen) singerVm.SetSinger(chosen); };
            ToolTip.SetTip(singer, DesktopUi.Label("Desktop.Singer"));
            body.Children.Add(singer);
            Grid phonemizerContent = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
            phonemizerContent.Children.Add(BoundText("PhonemizerTag"));
            IconPacks.Avalonia.PhosphorIcons.PackIconPhosphorIcons chevron = new IconPacks.Avalonia.PhosphorIcons.PackIconPhosphorIcons { Kind = IconPacks.Avalonia.PhosphorIcons.PackIconPhosphorIconsKind.CaretDown, Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(chevron, 1); phonemizerContent.Children.Add(chevron);
            Button phonemizer = new() { Name = "TrackPhonemizerPicker", Content = phonemizerContent, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            phonemizer.Classes.Add("DesktopPicker");
            phonemizer.Classes.Add("DesktopTrackChoice");
            phonemizer.Bind(Button.CommandProperty, new Binding("SelectPhonemizerCommand"));
            body.Children.Add(FieldRow(DesktopUi.Label("Desktop.Phonemizer"), phonemizer));
            TrackHeaderViewModel rendererVm = _trackVm;
            string[] renderers = track.Singer is { Found: true } singerModel ? Renderers.GetSupportedRenderers(singerModel.SingerType) : [];
            ComboBox renderer = new() { Name = "TrackRendererPicker", ItemsSource = renderers, SelectedItem = track.RendererSettings.renderer, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = renderers.Length > 0 };
            renderer.Classes.Add("DesktopTrackChoice");
            renderer.SelectionChanged += (_, _) =>
            {
                if (rendererVm == _trackVm && renderer.SelectedItem is string selected) rendererVm.SetRenderer(selected);
            };
            body.Children.Add(FieldRow(DesktopUi.Label("Desktop.Renderer"), renderer));
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
            _trackHost.Content = body;
        }
        private static Grid FieldRow(Control label, Control input)
        {
            Grid row = new() { ColumnDefinitions = new ColumnDefinitions("100,*"), ColumnSpacing = 6 };
            row.Children.Add(label); Grid.SetColumn(input, 1); row.Children.Add(input);
            return row;
        }
        private static TextBlock BoundText(string path) { TextBlock text = new(); text.Bind(TextBlock.TextProperty, new Binding(path)); return text; }
        private Control ToolPicker(UTrack track, DesktopToolKind kind)
        {
            StackPanel body = new() { Spacing = 4 };
            TextBlock label = DesktopUi.Label(kind == DesktopToolKind.Resampler ? "Desktop.Resamplers" : "Desktop.Wavtools");
            string[] compatible = kind == DesktopToolKind.Resampler
                ? Renderers.GetSupportedResamplers(track.RendererSettings.Wavtool).Select(t => t.ToString()!).ToArray()
                : Renderers.GetSupportedWavtools(track.RendererSettings.Resampler).Select(t => t.ToString()!).ToArray();
            DesktopTool[] tools = DesktopToolsService.GetTools(kind).Where(t => t.Available && compatible.Contains(t.Name)).ToArray();
            string? current = kind == DesktopToolKind.Resampler ? track.RendererSettings.resampler : track.RendererSettings.wavtool;
            ComboBox picker = new() { ItemsSource = tools, SelectedItem = tools.FirstOrDefault(t => t.Name == current), HorizontalAlignment = HorizontalAlignment.Stretch };
            picker.SelectionChanged += (_, _) =>
            {
                if (picker.SelectedItem is not DesktopTool tool || DocManager.Inst.HasOpenUndoGroup || !DocManager.Inst.Project.tracks.Contains(track)) return;
                URenderSettings settings = track.RendererSettings.Clone();
                if (kind == DesktopToolKind.Resampler) settings.resampler = tool.Name; else settings.wavtool = tool.Name;
                DocManager.Inst.StartUndoGroup();
                try { DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, track, settings)); }
                finally { DocManager.Inst.EndUndoGroup(); }
                _track = null; QueueRefresh();
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
        private void RefreshPhonemes()
        {
            _phonemesChanged = false;
            UVoicePart? part = _editor.PianoRollViewModel.EditingVoicePart;
            UPhoneme[] phonemes = part?.phonemes.Where(p => _editor.PianoRollViewModel.IsNoteSelected(p.Parent)).ToArray() ?? [];
            if (part == null || phonemes.Length == 0) { _phonemeNote = null; _phonemeHost.Content = DesktopUi.Label("Desktop.NoPhoneme"); return; }
            StackPanel body = new() { Spacing = 6 };
            int selectedIndex = Array.FindIndex(phonemes, p => p.Parent == _phonemeNote && p.index == _phonemeIndex);
            ComboBox selector = new() { ItemsSource = phonemes, SelectedIndex = Math.Max(0, selectedIndex), HorizontalAlignment = HorizontalAlignment.Stretch };
            body.Children.Add(selector);
            ContentControl fields = new(); body.Children.Add(fields);
            void Build()
            {
                if (selector.SelectedItem is not UPhoneme phoneme) return;
                UNote note = phoneme.Parent;
                _phonemeNote = note; _phonemeIndex = phoneme.index;
                StackPanel panel = new() { Spacing = 8 };
                bool Valid() => _editor.PianoRollViewModel.EditingVoicePart == part && part.notes.Contains(note) && part.phonemes.Contains(phoneme) && _editor.PianoRollViewModel.SelectedNotes.Contains(note) && !DocManager.Inst.HasOpenUndoGroup;
                void Commit(Func<UCommand> command)
                {
                    if (!Valid()) return;
                    DocManager.Inst.StartUndoGroup();
                    try { DocManager.Inst.ExecuteCmd(command()); }
                    finally { DocManager.Inst.EndUndoGroup(); }
                    Dispatcher.UIThread.Post(RefreshPhonemes, DispatcherPriority.Background);
                }
                TextBox alias = new() { Text = phoneme.phoneme };
                void SaveAlias()
                {
                    string? value = string.IsNullOrWhiteSpace(alias.Text) ? null : alias.Text.Trim();
                    string? current = note.phonemeOverrides.FirstOrDefault(o => o.index == phoneme.index)?.phoneme;
                    if (value != current && alias.Text != phoneme.phoneme) Commit(() => new ChangePhonemeAliasCommand(part, note, phoneme.index, value));
                }
                panel.Children.Add(FieldRow(DesktopUi.Label("Desktop.Alias"), alias));
                alias.LostFocus += (_, _) => { SaveAlias(); };
                alias.KeyDown += (_, e) =>
                {
                    if (EditorInputController.IsComposing(e.Source as Visual)) return;
                    if (e.Key == Key.Escape) { alias.Text = phoneme.phoneme; e.Handled = true; }
                    else if (e.Key == Key.Enter) { SaveAlias(); e.Handled = true; }
                };
                foreach ((string key, decimal value, Func<float, UCommand> command) in new (string, decimal, Func<float, UCommand>)[]
                {
                    ("Desktop.Offset", note.phonemeOverrides.FirstOrDefault(o => o.index == phoneme.index)?.offset ?? 0, v => new PhonemeOffsetCommand(part, note, phoneme.index, (int)v)),
                    ("Desktop.Preutter", (decimal)(phoneme.preutterDelta ?? 0), v => new PhonemePreutterCommand(part, note, phoneme.index, phoneme, v)),
                    ("Desktop.Overlap", (decimal)(phoneme.overlapDelta ?? 0), v => new PhonemeOverlapCommand(part, note, phoneme.index, phoneme, v)),
                    ("Desktop.Attack", (decimal)(phoneme.attackTimeDelta ?? 0), v => new PhonemeAttackTimeCommand(part, note, phoneme.index, phoneme, v)),
                    ("Desktop.Release", (decimal)(phoneme.releaseTimeDelta ?? 0), v => new PhonemeReleaseTimeCommand(part, note, phoneme.index, phoneme, v))
                })
                {
                    bool supported = key == "Desktop.Offset" || (!phoneme.Error && part.trackNo >= 0 && part.trackNo < DocManager.Inst.Project.tracks.Count
                        && DocManager.Inst.Project.tracks[part.trackNo].RendererSettings.Renderer?.SingerType == USingerType.Classic);
                    decimal accepted = value;
                    NumericUpDown number = new() { Value = value, Minimum = -10000, Maximum = 10000, Increment = 1, IsEnabled = supported };
                    panel.Children.Add(FieldRow(DesktopUi.Label(key), number));
                    void Save()
                    {
                        if (Valid() && number.Value is { } changed && changed != accepted && decimal.TryParse(number.Text, out decimal parsed) && parsed == changed)
                        {
                            accepted = changed;
                            Commit(() => command((float)changed));
                        }
                    }
                    number.LostFocus += (_, _) => Save();
                    number.KeyDown += (_, e) =>
                    {
                        if (EditorInputController.IsComposing(e.Source as Visual)) return;
                        if (e.Key == Key.Escape) { number.Value = accepted; e.Handled = true; }
                        else if (e.Key == Key.Enter) { Save(); e.Handled = true; }
                    };
                }
                panel.Children.Add(DesktopUi.Action("NoteProperties.Reset", () => Commit(() => new ClearPhonemeTimingCommand(part, note))));
                fields.Content = panel;
            }
            selector.SelectionChanged += (_, _) => Build(); Build();
            _phonemeHost.Content = body;
        }
        public void Dispose()
        {
            if (_disposed) return;
            CancelExpressionSliderPreviews();
            _disposed = true; Detach(); _draft?.Dispose(); _trackVm?.Dispose();
        }
    }
}
