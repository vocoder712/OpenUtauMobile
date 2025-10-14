using CommunityToolkit.Maui.Alerts;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.ViewModels;

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
        if (await this.DisplayAlert("卸载歌手", $"确认要卸载歌手 {ViewModel.Singer.LocalizedName} 吗？", "是", "否"))
        {
            await Toast.Make($"正在卸载歌手 {ViewModel.Singer.LocalizedName}...").Show();
            if (await SingerManager.Inst.UninstallSingerAsync(ViewModel.Singer))
            {
                await Toast.Make($"成功卸载歌手 {ViewModel.Singer.LocalizedName}").Show();
            }
            else
            {
                await Toast.Make($"卸载歌手 {ViewModel.Singer.LocalizedName} 失败").Show();
            }
            await Navigation.PopModalAsync(); // 返回上一页
        }
    }
}