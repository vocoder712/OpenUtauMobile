using CommunityToolkit.Maui.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Utils.Messages;
using OpenUtauMobile.ViewModels.Controls;
using System.Diagnostics;

namespace OpenUtauMobile.Views.Controls;

public partial class EditLyricsPopup : Popup
{
    public UVoicePart Part { get; set; }
    public UNote Note { get; set; }
    public EditLyricsPopup(UVoicePart part, UNote targetNote)
	{
		InitializeComponent();
		LabelTitle.Text = "±à¼­¸è´Ê";
        EntryLyrics.Text = targetNote.lyric;
		EntryLyrics.Focus();
		EntryLyrics.CursorPosition = 0;
		EntryLyrics.SelectionLength = targetNote.lyric.Length;
        Part = part;
        Note = targetNote;
    }

    private void ButtonCancel_Clicked(object sender, EventArgs e)
    {
        CloseAsync(null);
    }

    private void ButtonConfirm_Clicked(object sender, EventArgs e)
    {
        DocManager.Inst.StartUndoGroup();
        DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(Part, Note, EntryLyrics.Text));
        DocManager.Inst.EndUndoGroup();
        CloseAsync(null);
    }

    private void ButtonNext_Clicked(object sender, EventArgs e)
    {
        DocManager.Inst.StartUndoGroup();
        DocManager.Inst.ExecuteCmd(new ChangeNoteLyricCommand(Part, Note, EntryLyrics.Text));
        DocManager.Inst.EndUndoGroup();
        UNote? nextNote = SeekNextNote(Part, Note);
        if (nextNote == null)
        {
            CloseAsync(null);
            return;
        }
        SetNextNote(nextNote);
    }

    public void SetNextNote(UNote nextNote)
    {
        Note = nextNote;
        EntryLyrics.Text = nextNote.lyric;
        EntryLyrics.Focus();
        EntryLyrics.CursorPosition = 0;
        EntryLyrics.SelectionLength = nextNote.lyric.Length;
    }

    public static UNote? SeekNextNote(UVoicePart part, UNote note)
    {
        bool sought = false;
        foreach (UNote n in part.notes)
        {
            if (n == note)
            {
                sought = true;
            }
            else if (sought)
            {
                return n;
            }
        }
        return null;
    }
}