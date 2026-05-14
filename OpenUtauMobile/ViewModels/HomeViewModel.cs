using System;
using System.Diagnostics;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using DynamicData.Binding;
using OpenUtau.Core.Util;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtauMobile.ViewModels;

public class HomeViewModel : NavigateViewModelBase
{
    private static readonly TimeSpan backPressInterval = TimeSpan.FromMilliseconds(2000);
    private DateTime? _lastBackPressedTime;

    public ReactiveCommand<Unit, Unit> NewCommand { get; set; }
    public ReactiveCommand<Unit, Unit> OpenCommand { get; set; }
    public ReactiveCommand<Unit, Unit> SingersCommand { get; set; }
    public ReactiveCommand<Unit, Unit> OptionsCommand { get; set; }
    public ReactiveCommand<string, Unit> OpenRecentCommand { get; set; }
    public ReactiveCommand<string, Unit> RemoveRecentCommand { get; set; }
    public ReactiveCommand<Unit, Unit> OpenRecoveryCommand { get; set; }
    public ReactiveCommand<Unit, Unit> DismissRecoveryCommand { get; set; }

    /// <summary>
    /// 最近打开的项目列表，字符串为项目文件的路径。
    /// </summary>
    [Reactive]
    public ObservableCollectionExtended<string> RecentProjects { get; set; } = [];

    /// <summary>
    /// 是否有异常退出且恢复文件
    /// </summary>
    [Reactive]
    public bool HasRecovery { get; set; }

    /// <summary>
    /// 恢复横幅是否被手动关闭
    /// </summary>
    [Reactive]
    public bool RecoveryBannerDismissed { get; set; }

    /// <summary>
    /// 恢复横幅高度，用于收起动画
    /// </summary>
    /// <remarks>
    /// TODO: 动画很麻烦
    /// </remarks>
    [Reactive]
    public double RecoveryBannerHeight { get; set; } = double.NaN;

    /// <summary>
    /// 恢复文件路径
    /// </summary>
    [Reactive]
    public string RecoveryPath { get; set; } = string.Empty;

    public HomeViewModel(MainViewModel navigator) : base(navigator)
    {
        NewCommand = ReactiveCommand.Create(New);
        OpenCommand = ReactiveCommand.Create(Open);
        SingersCommand = ReactiveCommand.Create(Singers);
        OptionsCommand = ReactiveCommand.Create(Options);
        OpenRecentCommand = ReactiveCommand.Create<string>(OpenRecent);
        RemoveRecentCommand = ReactiveCommand.Create<string>(RemoveRecent);
        OpenRecoveryCommand = ReactiveCommand.Create(OpenRecovery);
        DismissRecoveryCommand = ReactiveCommand.Create(DismissRecovery);
    }

    public override void OnNavigatedTo()
    {
        _ = Initialize();
    }

    private void OpenRecent(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        Navigator.Navigate(new EditorViewModel(Navigator, path));
    }

    private void RemoveRecent(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        Preferences.Default.RecentFiles.Remove(path);
        Preferences.Save();
        RecentProjects.Remove(path);
    }

    private void New()
    {
        Navigator.Navigate(new EditorViewModel(Navigator));
    }

    private async void Open()
    {
        try
        {
            string path = await SelectProjectFile();
            Debug.WriteLine($"选取了：{path}");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Navigator.Navigate(new EditorViewModel(Navigator, path));
        }
        catch (Exception e)
        {
            Log.Error(e, "An error occured");
        }
    }

    private void Singers()
    {
        Navigator.Navigate(new SingerManagementViewModel(Navigator));
    }

    private void Options()
    {
        Navigator.Navigate(new OptionsViewModel(Navigator));
    }

    private void OpenRecovery()
    {
        if (string.IsNullOrEmpty(RecoveryPath)) return;
        DismissRecovery();
        Navigator.Navigate(new EditorViewModel(Navigator, RecoveryPath));
    }

    private void DismissRecovery()
    {
        RecoveryBannerDismissed = true;
        RecoveryBannerHeight = 0;
        Preferences.Default.RecoveryPath = string.Empty;
        Preferences.Save();
    }

    private static async Task<string> SelectProjectFile()
    {
        return await Storage.FilePicker.PickSingleFileAsync(L.S("FilePicker.OpenProject"),
            ["*.ustx", "*.vsqx", "*.ust", "*.mid", "*.midi", "*.ufdata", "*.musicxml"]);
    }

    private async Task Initialize()
    {
        RecoveryBannerDismissed = false;

        string recPath = Preferences.Default.RecoveryPath;
        if (!string.IsNullOrWhiteSpace(recPath) && File.Exists(recPath))
        {
            RecoveryPath = recPath;
            HasRecovery = true;
        }
        else
        {
            HasRecovery = false;
            RecoveryPath = string.Empty;
        }

        await RefreshRecentProjectsAsync();

        if (!Preferences.Default.SetupWizardCompleted)
        {
            await ShowSetupWizardAsync();
        }
    }

    private async Task RefreshRecentProjectsAsync()
    {
        RecentProjects.Load(await Task.Run(() => Preferences.Default.RecentFiles));
    }

    private async Task ShowSetupWizardAsync()
    {
        var vm = new SetupWizardViewModel();
        await PopupService.Show<object>(new SetupWizardPopup(), vm);
    }

    public override void OnBackRequested()
    {
        DateTime now = DateTime.UtcNow;
        if (_lastBackPressedTime.HasValue && (now - _lastBackPressedTime.Value) <= backPressInterval)
        {
            // 第二次按下且在有效窗口内，退出应用
            AppService.ExitApplication();
        }
        else
        {
            // 第一次按下（或超时后再按），显示提示并记录时间
            _lastBackPressedTime = now;
            ToastService.Enqueue(L.S("Home.Toast.BackToExit"), backPressInterval.TotalMilliseconds);
        }
    }
}