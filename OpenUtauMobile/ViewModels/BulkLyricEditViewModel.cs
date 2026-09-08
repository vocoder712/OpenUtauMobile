using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtauMobile.ViewModels;

/// <summary>
/// 歌词批量编辑弹窗 ViewModel。
/// </summary>
public sealed class BulkLyricEditViewModel : PopupViewModelBase, IDisposable
{
    private readonly UVoicePart _part;
    private readonly List<UNote> _partNotes;
    private readonly List<UNote> _selectedNotes;
    private readonly int _startNoteIndex;

    [Reactive]
    public string LyricsText { get; set; }

    [Reactive]
    public bool ApplyToSelectedNotesOnly { get; set; }

    public bool HasSelectedNotes => _selectedNotes.Count > 0;

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }

    public BulkLyricEditViewModel(UVoicePart part, IReadOnlyCollection<UNote> selectedNotes)
    {
        _part = part;
        _partNotes = [.. part.notes];
        _selectedNotes =
        [
            .. _partNotes
                .Where(selectedNotes.Contains)
        ];

        UNote? firstSelectedNote = _selectedNotes.FirstOrDefault();
        _startNoteIndex = firstSelectedNote == null ? 0 : _partNotes.IndexOf(firstSelectedNote);
        LyricsText = SplitLyrics.Join(_partNotes.Skip(_startNoteIndex).Select(note => note.lyric));

        CancelCommand = ReactiveCommand.Create(OnCancel);
        ApplyCommand = ReactiveCommand.Create(OnApply);
    }

    private void OnCancel()
    {
        RaiseClose(null);
    }

    private void OnApply()
    {
        List<string> lyrics = SplitLyrics.Split(LyricsText);
        if (lyrics.Count == 0)
        {
            RaiseClose(null);
            return;
        }

        IEnumerable<UNote> candidates = ApplyToSelectedNotesOnly
            ? _selectedNotes
            : _partNotes.Skip(_startNoteIndex);
        UNote[] notes = [.. candidates.Take(lyrics.Count)];
        if (notes.Length > 0)
        {
            string[] appliedLyrics = [.. lyrics.Take(notes.Length)];
            DocManager.Inst.StartUndoGroup();
            DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(_part, notes, appliedLyrics));
            DocManager.Inst.EndUndoGroup();
        }

        RaiseClose(null);
    }

    public override void RequestBack()
    {
        RaiseClose(null);
    }

    public void Dispose()
    {
        CancelCommand.Dispose();
        ApplyCommand.Dispose();
        GC.SuppressFinalize(this);
    }

}
