using CommunityToolkit.Maui.Views;
using OpenUtauMobile.ViewModels.Controls;

namespace OpenUtauMobile.Views.Controls;

public partial class ExitPopup : Popup
{
    public ExitPopup()
	{
		InitializeComponent();
    }

    private void ButtonDiscard_Clicked(object sender, EventArgs e)
    {
        CloseAsync("discard");
    }

    private void ButtonSave_Clicked(object sender, EventArgs e)
    {
        CloseAsync("save");
    }

    private void ButtonCancel_Clicked(object sender, EventArgs e)
    {
        CloseAsync("cancel");
    }

}