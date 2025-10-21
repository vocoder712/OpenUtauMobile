using CommunityToolkit.Maui.Views;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.ViewModels.Controls;

namespace OpenUtauMobile.Views.Controls;

public partial class ChooseSingerPopup : Popup
{
    public ChooseSingerPopup(UTrack track)
	{
		InitializeComponent();
    }
	private void OnCancelClicked(object sender, EventArgs e)
	{
		CloseAsync(null);
    }

    private void ButtonSinger_Clicked(object sender, EventArgs e)
    {
		if (sender is Button button && button.BindingContext is USinger singer)
		{
			CloseAsync(singer);
		}
    }
}