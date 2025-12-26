using CommunityToolkit.Maui.Alerts;
using OpenUtau.Core.Util;
using OpenUtauMobile.Utils;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Views;

public partial class DependencyManagePage : ContentPage
{
    private DependencyManageViewModel ViewModel { get; }
    public DependencyManagePage()
    {
        InitializeComponent();
        ViewModel = (DependencyManageViewModel)BindingContext;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ViewModel.RefreshInstalledDependencies(); // 刷新
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

    private async void ButtonRemove_Clicked(object sender, EventArgs e)
    {
        if (sender is Button button && button.BindingContext is DependencyInfo dependency)
        {
            if (!await DisplayAlert("删除依赖项", $"确定要删除依赖项 {dependency.Name} 吗？", "确定", "取消"))
            {
                return;
            }
            bool result = await ViewModel.RemoveDependencyAsync(dependency);
            if (result)
            {
                await Toast.Make("删除成功", CommunityToolkit.Maui.Core.ToastDuration.Short).Show();
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
            await ViewModel.InstallDependencyAsync(archivePath);
            await Toast.Make("安装完成", CommunityToolkit.Maui.Core.ToastDuration.Short).Show();
            await ViewModel.RefreshInstalledDependencies();
        }
    }
}
