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
    internal sealed class DesktopNoteInspector : UserControl, IDisposable
    {
        private readonly EditorViewModel _editor;
        private readonly DesktopLayoutStore _layout;
        private readonly Action _queueRefresh;
        private NotePropertyEditor? _draft;
        private UVoicePart? _part;
        private UNote[] _notes = [];
        private bool _notesChangedExternally;
        private bool _refreshingNotes;
        private StackPanel? _noteControls;
        private string? _noteSchema;
        private readonly List<Action<NotePropertyEditor>> _noteRebind = [];
        private readonly List<DesktopExpressionSlider> _expressionSliders = [];
        private bool _attached;
        private bool _disposed;
        private bool _committing;
        private CancellationTokenSource? _notePhonemizerPicker;
        private bool _ownsExpressionUndoGroup;
        private NotePropertyEditor? _liveExpressionDraft;
        private NotePropertyField? _liveExpressionField;
        private decimal? _liveExpressionValue;
        private bool _initialized;

        public event Action? RequestShowParameters;

        public DesktopNoteInspector(EditorViewModel editor, DesktopLayoutStore layout, Action queueRefresh)
        {
            _editor = editor;
            _layout = layout;
            _queueRefresh = queueRefresh;
        }

        private Expander Section(string key, object header, Control content, bool expanded)
        {
            Expander section = new() { Header = header, Content = content, HorizontalAlignment = HorizontalAlignment.Stretch };
            section.Tag = key;
            section.IsExpanded = _layout.State.InspectorSections.GetValueOrDefault(key, expanded);
            section.PropertyChanged += (_, e) =>
            {
                if (e.Property != Expander.IsExpandedProperty) return;
                _layout.State.InspectorSections[key] = section.IsExpanded;
                _layout.Save();
            };
            return section;
        }

        public UVoicePart? CurrentPart => _part;
        public bool IsCommitting => _committing;
        private bool SelectionMatches() => _part == _editor.PianoRollViewModel.EditingVoicePart && _notes.SequenceEqual(_editor.PianoRollViewModel.SelectedNotes.OrderBy(n => n.position).ThenBy(n => n.tone));
        private bool HasPendingDraftInput() => _draft != null && _draft.Groups.SelectMany(group => group.Fields.Concat(group.Vibrato == null ? [] : new[] { group.Vibrato.Duration }))
            .Any(field => field.IsEdited || field.HasInvalidInput || field.IsNumber && field.NumberText != field.Number?.ToString(CultureInfo.CurrentCulture));
        private static bool HasOpenPicker(Control host) => host.GetVisualDescendants().OfType<ComboBox>().Any(picker => picker.IsDropDownOpen);

        public void SetAttached(bool attached)
        {
            if (!attached)
            {
                _notePhonemizerPicker?.Cancel();
                if (_attached) CancelExpressionSliderPreviews();
            }
            _attached = attached;
        }

        public void MarkExternallyChanged(bool cancelPreviews)
        {
            _notesChangedExternally = true;
            if (cancelPreviews) CancelExpressionSliderPreviews();
        }

        public void OnSelectionChanged()
        {
            _notePhonemizerPicker?.Cancel();
            CancelExpressionSliderPreviews();
        }

        public bool RefreshIfNeeded()
        {
            bool same = SelectionMatches();
            if (!same) _notePhonemizerPicker?.Cancel();
            if (!_initialized || !same || _notesChangedExternally && _notePhonemizerPicker == null && !HasOpenPicker(this) && !HasPendingDraftInput()) Refresh();
            return same;
        }

        public bool CommitPendingInput()
        {
            foreach (DesktopExpressionSlider slider in _expressionSliders.ToArray())
                if (!slider.CommitPendingEdit()) return false;
            if (_draft != null && !CommitCurrentDraft()) return false;
            return string.IsNullOrEmpty(_draft?.Error) && !this.GetVisualDescendants().OfType<Control>().Any(DataValidationErrors.GetHasErrors);
        }

        private void CancelExpressionSliderPreviews()
        {
            foreach (DesktopExpressionSlider slider in _expressionSliders) slider.CancelPendingEdit();
        }
        public void CancelPreviews() => CancelExpressionSliderPreviews();
        public void Refresh()
        {
            foreach (DesktopExpressionSlider slider in _expressionSliders) slider.CancelPendingEdit();
            _notesChangedExternally = false;
            _initialized = true;
            _refreshingNotes = true;
            NotePropertyEditor? old = _draft;
            bool preservePresetSession = SelectionMatches();
            try
            {
                _part = _editor.PianoRollViewModel.EditingVoicePart;
                _notes = _editor.PianoRollViewModel.SelectedNotes.OrderBy(n => n.position).ThenBy(n => n.tone).ToArray();
                _draft = _part == null || _notes.Length == 0 ? null : new NotePropertyEditor(_part, _notes);
                if (_draft == null) { _expressionSliders.Clear(); _noteControls = null; _noteSchema = null; Content = DesktopUi.Label("Desktop.NoNotes"); return; }
                NotePropertyEditor draft = _draft;
                if (preservePresetSession && old != null)
                {
                    foreach (NotePropertyGroup group in draft.Groups)
                    {
                        if (group.Presets != null && old.Groups.FirstOrDefault(previous => previous.Key == group.Key)?.Presets is { } previousPresets)
                            group.Presets.RestoreSessionState(previousPresets);
                    }
                }
                string schema = string.Join("|", draft.Groups.Select(group => $"{group.Key}:{group.Title}:{group.EmptyMessage}:{group.Vibrato != null}:{group.Presets != null}:" +
                    string.Join(",", group.Fields.Select(field => $"{field.Label}:{field.IsNumber}:{field.IsChoice}:{field.IsPhonemizerPicker}:{field.CanReset}:{group.Key == "Expressions" && field.IsNumber}"))));
                // 选择改变时复用控件和模板，只替换草稿绑定，避免大量分配打断音频回调。
                if (_noteControls != null && _noteSchema == schema)
                {
                    foreach (Action<NotePropertyEditor> rebind in _noteRebind) rebind(draft);
                    Content = _noteControls;
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
                Content = sections;
            }
            finally { old?.Dispose(); _refreshingNotes = false; }
        }
        private async Task PickNotePhonemizerAsync(Button anchor, NotePropertyField field)
        {
            if (_notePhonemizerPicker != null || _refreshingNotes || _disposed || !_attached ||
                !SelectionMatches() || _draft is not { } draft || !field.IsEnabled || DocManager.Inst.HasOpenUndoGroup) return;
            using CancellationTokenSource cancellation = new();
            _notePhonemizerPicker = cancellation;
            try
            {
                PhonemizerPickerResult? result = await PhonemizerPickerService.PickAsync(new(
                    AllowTrackDefault: true, CurrentName: field.PhonemizerValue, TrackDefaultLabel: field.TrackDefaultLabel,
                    Anchor: anchor, CancellationToken: cancellation.Token));
                if (result == null || cancellation.IsCancellationRequested || _disposed || !_attached || draft != _draft ||
                    !SelectionMatches() || _part == null || !draft.MatchesCurrentSelection(DocManager.Inst.Project, _part, _notes)) return;
                field.SetPhonemizer(result);
                if (field.IsEdited) CommitDraft(draft);
            }
            catch (Exception exception)
            {
                Serilog.Log.Error(exception, "Failed to pick note phonemizer");
                OpenUtauMobile.Services.Dialogs.ErrorDialogService.Show(new ErrorDialogViewModel(new ErrorMessageNotification(exception)));
            }
            finally
            {
                _notePhonemizerPicker = null;
                _queueRefresh();
            }
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
            if (field.IsPhonemizerPicker)
            {
                Button picker = DesktopTrackPickers.CreatePhonemizerButton("NotePhonemizerPicker", "PhonemizerButtonLabel", "PhonemizerDisplay");
                picker.Bind(AutomationProperties.NameProperty, new Binding("Label"));
                picker.Click += async (_, _) => await PickNotePhonemizerAsync(picker, field);
                input = picker;
            }
            else if (field.IsChoice)
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
                if (e.Key == Key.Escape) { e.Handled = true; Refresh(); }
                else if (e.Key == Key.Enter && !field.IsPhonemizerPicker) { e.Handled = true; CommitCurrentDraft(); }
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
                        if (CommitDraft(resetDraft)) Refresh();
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
            if (completed && apply) Refresh();
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
                    _queueRefresh();
                }
                return committed;
            }
            finally { _committing = false; }
        }
        public void Dispose()
        {
            if (_disposed) return;
            SetAttached(false);
            _disposed = true;
            _draft?.Dispose();
        }
    }
}
