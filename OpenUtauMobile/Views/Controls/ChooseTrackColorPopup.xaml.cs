using CommunityToolkit.Maui.Views;
using OpenUtauMobile.ViewModels.Controls;

namespace OpenUtauMobile.Views.Controls;

public partial class ChooseTrackColorPopup : Popup
{
	private ChooseTrackColorPopupViewModel ViewModel { get; }
    public ChooseTrackColorPopup()
	{
		InitializeComponent();
		ViewModel = (ChooseTrackColorPopupViewModel)BindingContext;
    }
	private void OnCancelClicked(object sender, EventArgs e)
	{
		CloseAsync(null);
    }

    private void ButtonSelectColor_Clicked(object sender, EventArgs e)
    {
		if (sender is Button button && button.BindingContext is KeyValuePair<string, Color> color)
        {
            CloseAsync(color.Key);
        }
    }
}