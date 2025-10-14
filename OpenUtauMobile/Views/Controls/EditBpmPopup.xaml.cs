using CommunityToolkit.Maui.Views;
using OpenUtauMobile.ViewModels.Controls;

namespace OpenUtauMobile.Views.Controls;

public partial class EditBpmPopup : Popup
{
    public EditBpmPopup(string initialContent)
	{
		InitializeComponent();
		EntryName.Text = initialContent;
		EntryName.Focus();
		EntryName.CursorPosition = 0;
		EntryName.SelectionLength = initialContent.Length;
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