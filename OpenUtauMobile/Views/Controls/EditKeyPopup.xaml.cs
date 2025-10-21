using CommunityToolkit.Maui.Views;
using OpenUtauMobile.ViewModels.Controls;

namespace OpenUtauMobile.Views.Controls;

public partial class EditKeyPopup : Popup
{
	private EditKeyPopupViewModel ViewModel { get; }
    public EditKeyPopup(int key)
	{
		InitializeComponent();
		ViewModel = (EditKeyPopupViewModel)BindingContext;
		ViewModel.Initialize(key);
	}
	private void OnCancelClicked(object sender, EventArgs e)
	{
		CloseAsync(null);
    }

    private void ButtonSelect_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is int key)
        {
            CloseAsync(key);
        }
    }
}