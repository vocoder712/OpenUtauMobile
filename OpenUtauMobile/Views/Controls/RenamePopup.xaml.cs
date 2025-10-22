using CommunityToolkit.Maui.Views;
using OpenUtauMobile.ViewModels.Controls;

namespace OpenUtauMobile.Views.Controls;

public partial class RenamePopup : Popup
{
    public RenamePopup(string oldName, string title)
	{
		InitializeComponent();
		LabelTitle.Text = title;
        EntryName.Text = oldName;
		EntryName.Focus();
		EntryName.CursorPosition = 0;
		EntryName.SelectionLength = oldName.Length;
    }
	private void OnCancelClicked(object sender, EventArgs e)
	{
		CloseAsync(null);
    }
	private void OnConfirmClicked(object sender, EventArgs e)
	{
		CloseAsync(EntryName.Text.Trim());
    }
}