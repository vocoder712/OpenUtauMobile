using System;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Services.Editor;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.DesktopUI.Views
{
    /// <summary>输入直接覆盖编辑对象；捕获对象身份，避免失焦后写入新选择。</summary>
    internal sealed class DesktopInlineEditor : IDisposable
    {
        private readonly PianoRollViewModel _vm;
        private readonly PianoRollSurface _surface;
        private readonly Canvas _layer = new() { ClipToBounds = true };
        private TextBox? _input;
        private UVoicePart? _part;
        private UNote? _note;
        private int? _phonemeIndex;
        private string _original = string.Empty;
        private bool _closing;

        public DesktopInlineEditor(PianoRollViewModel vm, PianoRollSurface surface)
        {
            _vm = vm; _surface = surface;
            Grid.SetRowSpan(_layer, 4); Grid.SetColumnSpan(_layer, 2);
            surface.LayoutGrid.Children.Add(_layer);
            vm.RequestEditLyric += EditLyric;
            vm.RequestEditPhoneme += EditPhoneme;
            vm.PropertyChanged += OnChanged;
            vm.SelectedNotes.CollectionChanged += OnSelectionChanged;
        }
        private void OnSelectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (_note != null && !_vm.SelectedNotes.Contains(_note)) Close(false);
        }
        private void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PianoRollViewModel.EditingVoicePart)) Close(false);
            else if (e.PropertyName is nameof(PianoRollViewModel.TickOffset) or nameof(PianoRollViewModel.TickWidth) or nameof(PianoRollViewModel.KeyOffset) or nameof(PianoRollViewModel.KeyHeight) or nameof(PianoRollViewModel.PhonemePanelHeight) or nameof(PianoRollViewModel.PhonemePanelMode)) Position();
        }
        private void EditLyric(UVoicePart part, int index)
        {
            UNote? note = part.notes.ElementAtOrDefault(index);
            if (note != null) Open(part, note, null, note.lyric);
        }
        private void EditPhoneme(UVoicePart part, UNote note, int index)
        {
            UPhoneme? phoneme = part.phonemes.FirstOrDefault(p => p.Parent == note && p.index == index);
            if (phoneme != null) Open(part, note, index, phoneme.phoneme);
        }
        private void Open(UVoicePart part, UNote note, int? index, string value)
        {
            Close(true);
            if (_vm.EditingVoicePart != part || !part.notes.Contains(note)) return;
            _vm.SelectedNotes.Clear(); _vm.SelectedNotes.Add(note);
            _part = part; _note = note; _phonemeIndex = index; _original = value;
            TextBox input = new() { Name = "DesktopInlineText", Text = value, MinHeight = 26, Padding = new Thickness(4, 2) };
            _input = input;
            _layer.Children.Add(input); Position();
            input.LostFocus += (_, _) => { if (_input == input && !_closing) Close(true); };
            input.KeyDown += (_, e) =>
            {
                if (EditorInputController.IsComposing(e.Source as Visual)) return;
                if (e.Key == Key.Escape) { e.Handled = true; Close(false); _surface.NotesCanvas.Focus(); }
                else if (e.Key is Key.Enter or Key.Tab)
                {
                    e.Handled = true;
                    UNote? current = _note; UVoicePart? currentPart = _part; int? phonemeIndex = _phonemeIndex;
                    Close(true);
                    if (e.Key == Key.Tab && currentPart != null && current != null)
                    {
                        int step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
                        if (phonemeIndex == null)
                        {
                            UNote? next = currentPart.notes.ElementAtOrDefault(Array.IndexOf(currentPart.notes.ToArray(), current) + step);
                            if (next != null) { Open(currentPart, next, null, next.lyric); return; }
                        }
                        else
                        {
                            UPhoneme[] phonemes = currentPart.phonemes.ToArray();
                            int at = Array.FindIndex(phonemes, p => p.Parent == current && p.index == phonemeIndex);
                            if (at + step >= 0 && at + step < phonemes.Length)
                            { UPhoneme next = phonemes[at + step]; Open(currentPart, next.Parent, next.index, next.phoneme); return; }
                        }
                    }
                    _surface.NotesCanvas.Focus();
                }
            };
            Dispatcher.UIThread.Post(() => { if (_input == input) { input.Focus(); input.SelectAll(); } }, DispatcherPriority.Loaded);
        }
        private void Position()
        {
            if (_input == null || _part == null || _note == null) return;
            Point noteAt = _vm.TickPitchToPoint(_part.position + _note.position, _note.AdjustedTone);
            Point at = _surface.NotesCanvas.TranslatePoint(noteAt, _layer) ?? default;
            if (_phonemeIndex is int index && _part.phonemes.FirstOrDefault(p => p.Parent == _note && p.index == index) is { } phoneme)
            {
                if (_vm.IsPhonemeAdvancedMode)
                {
                    PhonemeAdvancedCanvas canvas = _surface.ParameterPanel.FindControl<PhonemeAdvancedCanvas>("AdvancedPhonemes")!;
                    at = canvas.TranslatePoint(canvas.GetAliasLabelBounds(phoneme).TopLeft - new Vector(0, 4), _layer) ?? at;
                }
                else if (_vm.IsPhonemeSimpleMode)
                {
                    PhonemeSimpleCanvas canvas = _surface.ParameterPanel.FindControl<PhonemeSimpleCanvas>("SimplePhonemes")!;
                    at = canvas.TranslatePoint(canvas.GetAliasLabelBounds(phoneme).TopLeft - new Vector(0, 4), _layer) ?? at;
                }
                else { Close(false); return; }
            }
            _input.Width = Math.Min(240, Math.Max(100, _note.duration * _vm.TickWidth));
            Canvas.SetLeft(_input, Math.Clamp(at.X, 0, Math.Max(0, _layer.Bounds.Width - _input.Width)));
            Canvas.SetTop(_input, Math.Clamp(at.Y, 0, Math.Max(0, _layer.Bounds.Height - 28)));
        }
        public void Close(bool commit)
        {
            if (_closing || _input == null) return;
            _closing = true;
            try
            {
                string value = _input.Text?.Trim() ?? string.Empty;
                if (commit && value != _original && _part != null && _note != null && _vm.EditingVoicePart == _part &&
                    DocManager.Inst.Project.parts.Contains(_part) && _part.notes.Contains(_note) &&
                    (_phonemeIndex == null || _part.phonemes.Any(p => p.Parent == _note && p.index == _phonemeIndex)) && !DocManager.Inst.HasOpenUndoGroup)
                {
                    UCommand command = _phonemeIndex is int index
                        ? new ChangePhonemeAliasCommand(_part, _note, index, value.Length == 0 ? null : value)
                        : new ChangeNoteLyricCommand(_part, new[] { _note }, new[] { value });
                    DocManager.Inst.StartUndoGroup();
                    try { DocManager.Inst.ExecuteCmd(command); }
                    finally { DocManager.Inst.EndUndoGroup(); }
                }
                _input = null; _note = null; _part = null;
                _layer.Children.Clear();
            }
            finally { _closing = false; }
        }
        public void Dispose()
        {
            Close(false);
            _vm.RequestEditLyric -= EditLyric; _vm.RequestEditPhoneme -= EditPhoneme;
            _vm.PropertyChanged -= OnChanged; _vm.SelectedNotes.CollectionChanged -= OnSelectionChanged;
            _surface.LayoutGrid.Children.Remove(_layer);
        }
    }
}
