using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Views;
using OpenUtauMobile.Utils;

namespace OpenUtauMobile.Views.Controls;

public partial class ErrorPopup : Popup
{
    public ErrorPopup(string errorDetail)
	{
		InitializeComponent();
        LabelErrorDetail.Text = "很抱歉， OpenUtau Mobile 捕获到了异常，部分功能可能无法正常使用。\n";
        LabelErrorDetail.Text += "建议您将以下错误信息复制并反馈给开发者，并重启应用程序。\n\n";
        LabelErrorDetail.Text += errorDetail;
    }

    private async void ButtonCopy_Clicked(object sender, EventArgs e)
    {
        await Clipboard.Default.SetTextAsync(LabelErrorDetail.Text);
#if !ANDROID33_0_OR_GREATER
        await Toast.Make("已复制到剪贴板", CommunityToolkit.Maui.Core.ToastDuration.Short).Show();
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