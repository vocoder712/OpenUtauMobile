using CommunityToolkit.Maui.Views;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Views;

public partial class LogExportPage : ContentPage
{
    private LogExportViewModel ViewModel { get; }

    public LogExportPage()
    {
        InitializeComponent();
        ViewModel = (LogExportViewModel)BindingContext;
        BindingContext = ViewModel;
        ViewModel.InitializeDefaultExportDirectory();
    }

    /// <summary>
    /// 文件项点击事件
    /// </summary>
    private void OnFileItemTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is LogFileInfo logFile)
        {
            ViewModel.ToggleFileSelection(logFile);
        }
    }

    /// <summary>
    /// 全选按钮点击
    /// </summary>
    private void OnSelectAllClicked(object sender, EventArgs e)
    {
        ViewModel.SelectAll(true);
    }

    /// <summary>
    /// 全不选按钮点击
    /// </summary>
    private void OnDeselectAllClicked(object sender, EventArgs e)
    {
        ViewModel.SelectAll(false);
    }

    /// <summary>
    /// 刷新按钮点击
    /// </summary>
    private void OnRefreshClicked(object sender, EventArgs e)
    {
        ViewModel.RefreshLogFiles();
    }

    /// <summary>
    /// 浏览目录按钮点击
    /// </summary>
    private async void OnBrowseDirectoryClicked(object sender, EventArgs e)
    {
        try
        {
            // 使用文件夹选择器
            var folderPickerPopup = new FolderPickerPopup(ViewModel.ExportDirectory);
            var result = await this.ShowPopupAsync(folderPickerPopup);

            if (!string.IsNullOrEmpty(result?.ToString()))
            {
                ViewModel.ExportDirectory = result.ToString();
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("错误", $"选择目录失败: {ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 导出按钮点击
    /// </summary>
    private async void OnExportClicked(object sender, EventArgs e)
    {
        try
        {
            var result = await ViewModel.ExportSelectedFiles();

            if (result)
            {
                await DisplayAlert("成功", "日志文件导出成功!", "确定");
                CloseAsync(true);
            }
            else
            {
                await DisplayAlert("失败", ViewModel.StatusMessage, "确定");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("错误", $"导出失败: {ex.Message}", "确定");
        }
    }

    private async Task<bool> DisplayAlert(string title, string message, string accept)
    {
        // 在实际应用中，您可能需要通过依赖注入或其他方式获取当前页面来显示Alert
        // 这里是一个简化的实现
        if (Application.Current?.MainPage != null)
        {
            await Application.Current.MainPage.DisplayAlert(title, message, accept);
        }
        return true;
    }
}