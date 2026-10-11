using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using OpenUtauMobile.Helpers;
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
    private readonly IDisposable _summarySubscription;

    [Reactive]
    public string TargetSummary { get; private set; } = string.Empty;

    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    public ReactiveCommand<Unit, Unit> ApplyCommand { get; }

    public BulkLyricEditViewModel(UVoicePart part, IReadOnlyCollection<UNote> selectedNotes, bool selectedOnly = false)
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
        ApplyToSelectedNotesOnly = selectedOnly && HasSelectedNotes;
        LyricsText = SplitLyrics.Join((ApplyToSelectedNotesOnly ? _selectedNotes : _partNotes.Skip(_startNoteIndex)).Select(note => note.lyric));
        _summarySubscription = this.WhenAnyValue(x => x.ApplyToSelectedNotesOnly, x => x.LyricsText)
            .Subscribe(_ => TargetSummary = string.Format(L.S("BulkLyricEdit.TargetCount"),
                Math.Min(SplitLyrics.Split(LyricsText).Count, GetCandidates().Count(_part.notes.Contains))));

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

        if (!DocManager.Inst.Project.parts.Contains(_part) || DocManager.Inst.HasOpenUndoGroup) { RaiseClose(null); return; }
        UNote[] notes = [.. GetCandidates().Where(_part.notes.Contains).Take(lyrics.Count)];
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

    private IEnumerable<UNote> GetCandidates() => ApplyToSelectedNotesOnly
        ? _selectedNotes : _partNotes.Skip(_startNoteIndex);

    public void Dispose()
    {
        _summarySubscription.Dispose();
        CancelCommand.Dispose();
        ApplyCommand.Dispose();
        GC.SuppressFinalize(this);
    }

}
