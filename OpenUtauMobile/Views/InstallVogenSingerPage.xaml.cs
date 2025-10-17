using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Messaging;
using OpenUtau.Core;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.ViewModels.Messages;
using OpenUtauMobile.Views.Controls;
using Serilog;

namespace OpenUtauMobile.Views;

public partial class InstallVogenSingerPage : ContentPage
{
    private InstallVogenSingerViewModel ViewModel { get; }

    private bool _isExit = false; // 退出标志
    private List<View> StepViews { get; set; } = [];
    private int _currentStep = 0;

    public InstallVogenSingerPage(string installPackagePath)
    {
        InitializeComponent();
        ViewModel = (InstallVogenSingerViewModel)BindingContext;
        ViewModel.InstallPackagePath = installPackagePath;

        InitializeStepViews();
        UpdateStepViews();

    }

    /// <summary>
    /// 返回按钮点击事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ButtonBack_Clicked(object sender, EventArgs e)
    {
        Navigation.PopModalAsync();
    }


    /// <summary>
    /// 步骤视图添加到列表
    /// </summary>
    private void InitializeStepViews()
    {
        foreach (var child in MainContentLayout.Children)
        {
            if (child is View view)
            {
                StepViews.Add(view);
            }
        }
    }

    /// <summary>
    /// 更新步骤视图的可见性
    /// </summary>
    private void UpdateStepViews()
    {
        for (int i = 0; i < StepViews.Count; i++)
        {
            StepViews[i].IsVisible = (i == _currentStep);
            StepViews[i].InputTransparent = (i != _currentStep);
        }
    }

    /// <summary>
    /// 返回按钮按下事件
    /// </summary>
    /// <returns></returns>
    protected override bool OnBackButtonPressed()
    {
        if (_currentStep >= 2) // 安装完成后直接返回
        {
            return base.OnBackButtonPressed();
        }
        if (_isExit)
        {
            Navigation.PopModalAsync();
            Toast.Make("安装已取消！", CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
            return false;
        }
        else
        {
            _isExit = true;

            Task.Run(async () =>
            {
                await Task.Delay(2000); // 2秒
                _isExit = false;
            });

            Toast.Make("再次操作以取消歌手安装", CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show(); // toast显示提示
        }

        return true;
    }

    private void ButtonFinish_Clicked(object sender, EventArgs e)
    {
        Navigation.PopModalAsync();
    }

    private void ButtonInstall_Clicked(object sender, EventArgs e)
    {
        _currentStep++;
        UpdateStepViews();

        // 待解决：断点到达这里时，没有更新页面可见性，安装进度无法展示

        Task.Run(() =>
        {
            try
            {
                ViewModel.Install();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "声库安装失败！");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }).ContinueWith(task => MainThread.InvokeOnMainThreadAsync(() =>
        {
            DocManager.Inst.ExecuteCmd(new SingersChangedNotification());
            Toast.Make("安装完成！", CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
            _currentStep++;
            UpdateStepViews();
        }));
    }
}
