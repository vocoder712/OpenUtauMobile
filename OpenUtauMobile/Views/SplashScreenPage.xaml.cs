using CommunityToolkit.Maui.Views;
using OpenUtau.Audio;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using OpenUtauMobile.Utils;
using OpenUtauMobile.Views.Controls;
using OpenUtauMobile.Views.Utils;
using OpenUtauMobile.Resources.Strings;
using Serilog;
using System.Diagnostics;
using System.Globalization;
using Preferences = OpenUtau.Core.Util.Preferences;

namespace OpenUtauMobile.Views;

public partial class SplashScreenPage : ContentPage, ICmdSubscriber
{
    public SplashScreenPage()
	{
		InitializeComponent();
        string version = AppInfo.VersionString;
        LabelDisplayVersion.Text = $"Version {version}";
        DocManager.Inst.AddSubscriber(this); // 订阅DocManager的命令
        //CheckPermission(); // 检查权限
        InitApp(); // 初始化OpenUtau后端
    }


    /// <summary>
    /// 初始化OpenUtau后端
    /// </summary>
    public void InitApp()
    {
        var mainThread = Thread.CurrentThread;
        var mainScheduler = TaskScheduler.FromCurrentSynchronizationContext();
        Task.Run(async () => // 异步初始化
        {
            Log.Information("==========开始初始化OpenUtau后端==========");
            try
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LabelInitDetail.Text = AppResources.InitializingObjectProvider;
                });
                // throw new Exception("测试异常");
                ObjectProvider.Initialize(); // 初始化对象提供器
                Log.Information("对象提供器初始化完成");

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LabelInitDetail.Text = AppResources.InitializingToolsManager;
                });
                ToolsManager.Inst.Initialize(); // 初始化ToolsManager
                Log.Information("ToolsManager初始化完成");

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LabelInitDetail.Text = AppResources.InitializingSingerManager;
                });
                SingerManager.Inst.Initialize(); // 初始化SingerManager
                Log.Information("SingerManager初始化完成");

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LabelInitDetail.Text = AppResources.InitializingDocManager;
                });
                DocManager.Inst.Initialize(mainThread, mainScheduler); // 初始化DocManager
                DocManager.Inst.PostOnUIThread = action => MainThread.BeginInvokeOnMainThread(action); // 设置DocManager的PostOnUIThread
                Log.Information("DocManager初始化完成");

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LabelInitDetail.Text = AppResources.InitializingPlaybackManager;
                });
                PlaybackManager.Inst.AudioOutput = ObjectProvider.AudioOutput?? new DummyAudioOutput(); // 设置PlaybackManager的AudioOutput
                Log.Information("PlaybackManager初始化完成");

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LabelInitDetail.Text = AppResources.InitializingPianoRoll;
                });
                ViewConstants.PianoKeys = [.. Enumerable.Range(0, ViewConstants.TotalPianoKeys).Reverse().Select(n => new PianoKey(n))];
                Log.Information("钢琴卷帘初始化完成");

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LabelInitDetail.Text = AppResources.InitializingLanguageLocalization;
                });
                string lang = Preferences.Default.Language;
                if (string.IsNullOrEmpty(lang))
                {
                    lang = CultureInfo.CurrentCulture.Name; // 获取系统语言
                }
                CultureInfo.DefaultThreadCurrentCulture = new CultureInfo(lang);
                CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo(lang);
                Log.Information($"语言偏好：{lang}");
                Log.Information("语言本地化初始化完成");

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    LabelInitDetail.Text = AppResources.InitializationComplete;
                });
            }
            catch (Exception e)
            {
                Log.Error($"OpenUtau后端初始化失败: {e}");
                // 初始化失败弹窗
                var popup = new ErrorPopup(e, AppResources.ErrorOpenUtauBackendInitializationFailed);
                object? result = await MainThread.InvokeOnMainThreadAsync(() => this.ShowPopupAsync(popup));

                if (result is string id)
                {
                    if (id == "exit")
                    {
                        Log.Information("退出应用");
                        Application.Current?.Quit();
                    }
                }
            }
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Navigation.PushModalAsync(new HomePage(), false); // 模式导航到主页
            });
            Log.Information("==========OpenUtau后端初始化完成==========");
        });
    }

    public async void CheckPermission()
    {
        if (DeviceInfo.Current.Platform == DevicePlatform.Android) // Android
        {
            PermissionStatus storage_read_status = await Permissions.CheckStatusAsync<Permissions.StorageRead>(); // 检查存储读取权限
            if (true)
            {
                storage_read_status = await Permissions.RequestAsync<Permissions.StorageRead>(); // 请求存储读取权限
                if (storage_read_status != PermissionStatus.Granted)
                {
                    Log.Error("没有存储权限");
                    throw new Exception("没有存储权限");
                }
            }
        }
    }

    public void OnNext(UCommand cmd, bool isUndo)
    {
        if (cmd is ErrorMessageNotification errorCmd)
        {
            Debug.WriteLine(errorCmd.e?.ToString());
            Popup popup = new ErrorPopup(errorCmd.e, errorCmd.message);
            MainThread.BeginInvokeOnMainThread(async () => await this.ShowPopupAsync(popup));
        }
    }
}