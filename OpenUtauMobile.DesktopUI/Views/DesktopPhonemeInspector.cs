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
    internal sealed class DesktopPhonemeInspector : UserControl
    {
        private readonly EditorViewModel _editor;
        private UNote? _phonemeNote;
        private int _phonemeIndex;

        public DesktopPhonemeInspector(EditorViewModel editor) => _editor = editor;

        public bool HasInputFocus()
        {
            if (this.GetVisualDescendants().OfType<ComboBox>().Any(picker => picker.IsDropDownOpen)) return true;
            return TopLevel.GetTopLevel(this)?.FocusManager.GetFocusedElement() is Visual visual && visual.GetSelfAndVisualAncestors().Contains(this);
        }
        public void Refresh()
        {
            UVoicePart? part = _editor.PianoRollViewModel.EditingVoicePart;
            UPhoneme[] phonemes = part?.phonemes.Where(p => _editor.PianoRollViewModel.IsNoteSelected(p.Parent)).ToArray() ?? [];
            if (part == null || phonemes.Length == 0) { _phonemeNote = null; Content = DesktopUi.Label("Desktop.NoPhoneme"); return; }
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
                    Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
                }
                TextBox alias = new() { Text = phoneme.phoneme };
                void SaveAlias()
                {
                    string? value = string.IsNullOrWhiteSpace(alias.Text) ? null : alias.Text.Trim();
                    string? current = note.phonemeOverrides.FirstOrDefault(o => o.index == phoneme.index)?.phoneme;
                    if (value != current && alias.Text != phoneme.phoneme) Commit(() => new ChangePhonemeAliasCommand(part, note, phoneme.index, value));
                }
                panel.Children.Add(DesktopInspectorControls.FieldRow(DesktopUi.Label("Desktop.Alias"), alias));
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
                    panel.Children.Add(DesktopInspectorControls.FieldRow(DesktopUi.Label(key), number));
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
                panel.Children.Add(DesktopUi.Action("NoteProperties.Reset", () =>
                {
                    if (Valid()) PhonemeCanvasActions.ResetTiming(part, [phoneme]);
                }));
                fields.Content = panel;
            }
            selector.SelectionChanged += (_, _) => Build(); Build();
            Content = body;
        }
    }
}
