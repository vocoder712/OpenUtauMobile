using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Views;
using OpenUtau.Core.Util;
using OpenUtauMobile.Utils;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.Views.Controls;
using OpenUtauMobile.Resources.Strings;

namespace OpenUtauMobile.Views;

public partial class DependencyManagePage : ContentPage
{
    private DependencyManageViewModel ViewModel { get; }
    public DependencyManagePage()
    {
        InitializeComponent();
        ViewModel = (DependencyManageViewModel)BindingContext;
    }

    /// <summary>
    /// 按钮事件-返回
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void ButtonBack_Clicked(object sender, EventArgs e)
    {
        await Navigation.PopModalAsync(); // 返回上一页
    }

    private async void ButtonRemove_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is DependencyInfo dependency)
        {
            if (!await DisplayAlert(AppResources.RemoveDependencyTitle, string.Format(AppResources.RemoveDependencyPrompt, dependency.Name), AppResources.Confirm, AppResources.CancelText))
            {
                return;
            }
            LoadingPopup popup = new(false);
            this.ShowPopup(popup);
            popup.Update(0, string.Format(AppResources.RemovingDependencyMessage, dependency.Name));
            bool result = await ViewModel.RemoveDependencyAsync(dependency);
            await popup.Finish();
            if (result)
            {
                await Toast.Make(AppResources.SuccessfullyDeleted, CommunityToolkit.Maui.Core.ToastDuration.Short).Show();
            }
            else
            {
                await Toast.Make($"依赖项 {dependency.Name} 删除失败", CommunityToolkit.Maui.Core.ToastDuration.Long).Show();
            }
            await ViewModel.RefreshInstalledDependencies();
        }
    }

    private async void ButtonInstall_Clicked(object sender, EventArgs e)
    {
        string archivePath = await ObjectProvider.PickFile([".oudep"], this);
        if (!string.IsNullOrWhiteSpace(archivePath))
        {
            LoadingPopup popup = new(true);
            this.ShowPopup(popup);
            popup.Update(0, "正在安装依赖项...");
            await ViewModel.InstallDependencyAsync(archivePath, popup.Update);
            await popup.Finish();
            await Toast.Make(AppResources.InstallationComplete, CommunityToolkit.Maui.Core.ToastDuration.Short).Show();
            await ViewModel.RefreshInstalledDependencies();
        }
    }
}
