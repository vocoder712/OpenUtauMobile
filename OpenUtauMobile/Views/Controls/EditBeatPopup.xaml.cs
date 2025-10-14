using CommunityToolkit.Maui.Views;
using OpenUtauMobile.ViewModels.Controls;

namespace OpenUtauMobile.Views.Controls;

public partial class EditBeatPopup : Popup
{
    public EditBeatPopup(int beatPerBar, int beatUnit)
	{
		InitializeComponent();
		EntryBeatPerBar.Text = beatPerBar.ToString();
		EntryBeatUnit.Text = beatUnit.ToString();
		EntryBeatPerBar.Focus();
		EntryBeatPerBar.CursorPosition = 0;
		EntryBeatPerBar.SelectionLength = EntryBeatPerBar.Text.Length;
	}
	private void OnCancelClicked(object sender, EventArgs e)
	{
		CloseAsync(null);
    }
	private void OnConfirmClicked(object sender, EventArgs e)
	{
		if (!int.TryParse(EntryBeatPerBar.Text, out int beatPerBar) || beatPerBar <= 0)
        {
			return;
        }
		if (!int.TryParse(EntryBeatUnit.Text, out int beatUnit) || beatUnit <= 0)
        {
			return;
        }
        CloseAsync(new Tuple<int, int>(beatPerBar, beatUnit));
    }
}