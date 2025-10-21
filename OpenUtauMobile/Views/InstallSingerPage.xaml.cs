using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.Messaging;
using OpenUtau.Core;
using OpenUtauMobile.Resources.Strings;
using OpenUtauMobile.ViewModels;
using OpenUtauMobile.ViewModels.Messages;
using OpenUtauMobile.Views.Controls;
using Serilog;

namespace OpenUtauMobile.Views;

public partial class InstallSingerPage : ContentPage
{
    private InstallSingerViewModel _viewModel;

    private bool _isExit = false; // 退出标志
    private List<View> StepViews { get; set; } = [];
    private int _currentStep = 0;

    public InstallSingerPage(string installPackagePath)
    {
        InitializeComponent();
        _viewModel = (InstallSingerViewModel)BindingContext;
        _viewModel.InstallPackagePath = installPackagePath;

        InitializeStepViews();
        UpdateStepViews();
        UpdateStepButton();

        //WeakReferenceMessenger.Default.Register<SimpleMessage>(this, async (sender, message) =>
        //{
        //    var popup = new SimplePopup("警告", message.Value, new Dictionary<string, string> { { "ok", "确定" } });
        //    object? result = await MainThread.InvokeOnMainThreadAsync(() => this.ShowPopupAsync(popup));

        //    if (result is string action && action == "ok")
        //    {
        //        await Navigation.PopModalAsync();
        //    }
        //});
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Task.Run(() => { _viewModel.Init(); }); // 待解决
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
    /// 上一步按钮点击事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ButtonBackStep_Clicked(object sender, EventArgs e)
    {
        if (_currentStep > 0)
        {
            _currentStep--;
            if (_currentStep == 2 && !_viewModel.MissingInfo)
            {
                _currentStep--;
            }
            UpdateStepViews();
            UpdateStepButton();
        }
    }

    /// <summary>
    /// 下一步按钮点击事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ButtonNextStep_Clicked(object sender, EventArgs e)
    {
        if (_currentStep < StepViews.Count - 1)
        {
            _currentStep++;
            if (_currentStep == 2 && !_viewModel.MissingInfo)
            {
                _currentStep++;
            }
            UpdateStepViews();
            UpdateStepButton();
        }
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

    private void UpdateStepButton()
    {
        // 上一步按钮可见性
        if (_currentStep >= 1 && _currentStep <= 3)
        {
            ButtonBackStep.IsVisible = true;
        }
        else
        {
            ButtonBackStep.IsVisible = false;
        }
        // 下一步按钮可见性
        if (_currentStep <= 2)
        {
            ButtonNextStep.IsVisible = true;
        }
        else
        {
            ButtonNextStep.IsVisible = false;
        }
    }

    /// <summary>
    /// 返回按钮按下事件
    /// </summary>
    /// <returns></returns>
    protected override bool OnBackButtonPressed()
    {
        if (_currentStep >= 5) // 安装完成后直接返回
        {
            return base.OnBackButtonPressed();
        }
        if (_isExit)
        {
            Navigation.PopModalAsync();
            Toast.Make(AppResources.InstallationCancelledToast, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
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

            Toast.Make(AppResources.ConfirmCancelSingerInstallToast, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show(); // toast显示提示
        }

        return true;
    }

    private void ArchiveEncodingListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null)
        {
            return;
        }
        Task.Run(() => { _viewModel.RefreshArchiveItems(); });
    }

    private void TextEncodingListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel == null)
        {
            return;
        }
        Task.Run(() => { _viewModel.RefreshTextItems(); });
    }

    /// <summary>
    /// 开始安装按钮点击事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ButtonBeginInstall_Clicked(object sender, EventArgs e)
    {
        _currentStep++;
        UpdateStepViews();
        UpdateStepButton();

        Task.Run(() =>
        {
            try
            {
                _viewModel.Install();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "声库安装失败！");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(ex));
            }
        }).ContinueWith(task => MainThread.InvokeOnMainThreadAsync(() =>
        {
            DocManager.Inst.ExecuteCmd(new SingersChangedNotification());
            Toast.Make(AppResources.InstallationComplete, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
            _currentStep++;
            UpdateStepViews();
            UpdateStepButton();
        }));
    }

    private void ButtonFinish_Clicked(object sender, EventArgs e)
    {
        Navigation.PopModalAsync();
    }
}
