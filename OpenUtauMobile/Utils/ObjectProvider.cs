using CommunityToolkit.Maui.Alerts;
using OpenUtauMobile.Views.Controls;
using Microsoft.Maui.Controls;
using OpenUtau.Audio;
using OpenUtauMobile.Utils.Permission;
using CommunityToolkit.Maui.Views;
using OpenUtauMobile.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OpenUtauMobile.Resources.Strings;
using SkiaSharp;
using Serilog;
using OpenUtau.Core;

namespace OpenUtauMobile.Utils
{
    public static class ObjectProvider
    {
        public static IExternalStorageService? ExternalStorageService { get; private set; }
        public static IAudioOutput? AudioOutput { get; private set; }
        public static Random Random { get; } = new Random();
        public static IAppLifeCycleHelper AppLifeCycleHelper { get; set; } = null!;
        public static SKTypeface NotoSansCJKscRegularTypeface { get; set; } = null!;
        public static void Initialize()
        {
#if ANDROID
            ExternalStorageService = new OpenUtauMobile.Platforms.Android.Utils.Permission.ExternalStorageService();
            AudioOutput = new OpenUtauMobile.Platforms.Android.Utils.Audio.AudioTrackOutput();
            AppLifeCycleHelper = new OpenUtauMobile.Platforms.Android.Utils.AndroidAppLifeCycleHelper();
#elif IOS
            ExternalStorageService = new OpenUtauMobile.Platforms.iOS.Utils.Permission.ExternalStorageService();
            AudioOutput = new OpenUtau.Audio.DummyAudioOutput(); // iOS平台使用DummyAudioOutput作为占位符
            AppLifeCycleHelper = new OpenUtauMobile.Platforms.iOS.Utils.iOSAppLifeCycleHelper();
#elif WINDOWS
            ExternalStorageService = new OpenUtauMobile.Platforms.Windows.Utils.Permission.ExternalStorageService();
            AudioOutput = new OpenUtau.Audio.NAudioOutput();
            AppLifeCycleHelper = new OpenUtauMobile.Platforms.Windows.Utils.WindowsAppLifeCycleHelper();
#else
            throw new NotSupportedException("Unsupported platform");
#endif
            try
            {
                using var stream = FileSystem.OpenAppPackageFileAsync("NotoSansCJKsc-Regular.otf").Result;
                NotoSansCJKscRegularTypeface = SKTypeface.FromStream(stream);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "无法加载NotoSansCJKsc字体，使用默认字体代替。");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("字体加载失败，使用默认字体代替。", ex));
                NotoSansCJKscRegularTypeface = SKTypeface.Default;
            }
        }
        public static async Task<string> PickFile(string[] types, ContentPage context)
        {
            if (ExternalStorageService == null)
            {
                throw new InvalidOperationException("ExternalStorageService is not initialized. Call ObjectProvider.Initialize() first.");
            }
            if (await RequestStoragePermissionAsync())
            {
#if !WINDOWS
            await Toast.Make(AppResources.SelectFileToast, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
#endif
                // 选择路径
                var filePickPopup = new FilePickerPopup(types);
                object? result = await context.ShowPopupAsync(filePickPopup);
                if (result is string selectedPath)
                {
                    if (string.IsNullOrEmpty(selectedPath))
                    {
                        return string.Empty;
                    }
                    foreach (string type in types)
                    {
                        if (selectedPath.EndsWith(type, StringComparison.OrdinalIgnoreCase)) // 忽略大小写比较
                        {
                            return selectedPath;
                        }
                    }
                    string stringBuilder = string.Format(AppResources.WrongFileTypeToast, string.Join("，*", types));
                    await Toast.Make(stringBuilder, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
                    return string.Empty;
                }
                return string.Empty;
            }
            else
            {
                await Toast.Make(AppResources.StoragePermissionDeniedToast, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
                return string.Empty;
            }
        }

        public static async Task<string> SaveFile(string[] types, ContentPage context, string initialDirectory = "", string initialFileName = "")
        {
            if (ExternalStorageService == null)
            {
                throw new InvalidOperationException("ExternalStorageService is not initialized. Call ObjectProvider.Initialize() first.");
            }
            if (await RequestStoragePermissionAsync())
            {
#if !WINDOWS
            await Toast.Make(AppResources.SelectSaveLocationToast, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
#endif
                var fileSaverPopup = new FileSaverPopup(types, initialDirectory, initialFileName);
                object? result = await context.ShowPopupAsync(fileSaverPopup);
                if (result is string filePath)
                {
                    if (string.IsNullOrEmpty(filePath))
                    {
                        return string.Empty;
                    }
                    return filePath;
                }
                return string.Empty;
            }
            else
            {
                await Toast.Make(AppResources.StoragePermissionDeniedToast, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
                return string.Empty;
            }
        }

        private static async Task<bool> RequestStoragePermissionAsync()
        {
            if (ExternalStorageService == null)
            {
                throw new InvalidOperationException("ExternalStorageService is not initialized. Call ObjectProvider.Initialize() first.");
            }
            if (!await ExternalStorageService.HasManageExternalStoragePermissionAsync())
            {
                ExternalStorageService.RequestManageExternalStoragePermission();
            }
            return true;
        }
    }
}
