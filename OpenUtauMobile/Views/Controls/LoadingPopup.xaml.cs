using CommunityToolkit.Maui.Views;
using OpenUtauMobile.ViewModels.Controls;

namespace OpenUtauMobile.Views.Controls;

public partial class LoadingPopup : Popup
{
    public LoadingPopup(bool showProgressBar)
	{
		InitializeComponent();
        ProgressBarLoading.IsVisible = showProgressBar;
        ActivityIndicatorLoading.IsVisible = !showProgressBar;
    }
    /// <summary>
    /// 更新加载进度和消息
    /// </summary>
    /// <param name="progress">0-100</param>
    /// <param name="message">消息</param>
    public void Update(double progress, string message)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ProgressBarLoading.Progress = progress / 100.0;
            LabelMessage.Text = message;
        });
    }

    public Task Finish()
    {
        return CloseAsync();
    }
}