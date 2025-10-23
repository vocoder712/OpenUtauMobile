using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Views;
using OpenUtauMobile.Utils;
using OpenUtauMobile.Resources.Strings;

namespace OpenUtauMobile.Views.Controls;

public partial class ErrorPopup : Popup
{
    public ErrorPopup(Exception? exception, string message)
	{
		InitializeComponent();
        LabelErrorDetail.Text = string.Format(AppResources.ErrorPopupTemplate, message, exception?.ToString());
    }

    private async void ButtonCopy_Clicked(object sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(LabelErrorDetail.Text);
#if !ANDROID33_0_OR_GREATER
        await Toast.Make(AppResources.CopiedToClipboardToast, CommunityToolkit.Maui.Core.ToastDuration.Short).Show();
#endif
    }

    private async void ButtonClose_Clicked(object sender, EventArgs e)
    {
        await CloseAsync("close");
    }

    private void ButtonRelaunch_Clicked(object sender, EventArgs e)
    {
        ObjectProvider.AppLifeCycleHelper.Restart();
    }
}