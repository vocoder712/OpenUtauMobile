using CommunityToolkit.Maui.Alerts;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Resources.Strings;

namespace OpenUtauMobile.Views;

public partial class SingerDetailPage : ContentPage
{
    private SingerDetailViewModel ViewModel { get; }
    public SingerDetailPage(USinger singer)
	{
		InitializeComponent();
        ViewModel = (SingerDetailViewModel)BindingContext;
        ViewModel.Init(singer);
    }

    /// <summary>
    /// 按钮事件-返回
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ButtonBack_Clicked(object sender, EventArgs e)
    {
        Navigation.PopModalAsync(); // 返回上一页
    }

    private async void ButtonRemoveSinger_Clicked(object sender, EventArgs e)
    {
        if (await this.DisplayAlert(AppResources.RemoveSinger, string.Format(AppResources.RemoveSingerPrompt, 0, ViewModel.Singer.LocalizedName), AppResources.Confirm, AppResources.CancelText))
        {
            await Toast.Make(string.Format(AppResources.RemovingSingerToast, 0, ViewModel.Singer.LocalizedName)).Show();
            if (await SingerManager.Inst.UninstallSingerAsync(ViewModel.Singer))
            {
                await Toast.Make(string.Format(AppResources.RemoveSingerSuccessToast, 0, ViewModel.Singer.LocalizedName)).Show();
            }
            else
            {
                await Toast.Make(string.Format(AppResources.RemoveSingerFailureToast, 0, ViewModel.Singer.LocalizedName)).Show();
            }
            await Navigation.PopModalAsync(); // 返回上一页
        }
    }
}