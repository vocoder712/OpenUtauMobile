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
            try {
                AudioOutput = new OpenUtauMobile.Platforms.iOS.Utils.Audio.AVAudioEngineOutput();
            } catch (Exception ex) {
                Log.Error(ex, "Failed to initialize AVAudioEngineOutput, falling back to DummyAudioOutput");
                AudioOutput = new OpenUtau.Audio.DummyAudioOutput();
            }
            Log.Information("ObjectProvider initialized AudioOutput: {AudioOutput}", AudioOutput?.GetType().FullName);
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
#if IOS
            // iOS 使用原生文件选择器
            try
            {
                // iOS 需要使用 UTType，将扩展名转换为对应的 UTType
                var utTypes = new List<string>();
                foreach (var ext in types)
                {
                    var cleanExt = ext.TrimStart('.');
                    switch (cleanExt.ToLower())
                    {
                        case "zip":
                            utTypes.Add("public.zip-archive");
                            break;
                        case "rar":
                            utTypes.Add("com.rarlab.rar-archive");
                            break;
                        case "uar":
                        case "vogeon":
                        case "ustx":
                        case "vsqx":
                        case "ust":
                        case "ufdata":
                        case "musicxml":
                            // 自定义文件类型使用 public.data
                            utTypes.Add("public.data");
                            break;
                        case "mid":
                        case "midi":
                            utTypes.Add("public.midi-audio");
                            break;
                        case "wav":
                            utTypes.Add("com.microsoft.waveform-audio");
                            break;
                        case "mp3":
                            utTypes.Add("public.mp3");
                            break;
                        case "flac":
                            utTypes.Add("org.xiph.flac");
                            break;
                        case "ogg":
                            utTypes.Add("org.xiph.ogg");
                            break;
                        default:
                            utTypes.Add("public.data");
                            break;
                    }
                }
                // 去重
                utTypes = utTypes.Distinct().ToList();
                
                var customFileTypes = new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, utTypes }
                };
                var options = new PickOptions
                {
                    PickerTitle = AppResources.SelectFileToast,
                    FileTypes = new FilePickerFileType(customFileTypes)
                };
                var result = await FilePicker.Default.PickAsync(options);
                if (result != null)
                {
                    // 检查文件类型
                    bool typeMatched = false;
                    foreach (string type in types)
                    {
                        if (result.FullPath.EndsWith(type, StringComparison.OrdinalIgnoreCase))
                        {
                            typeMatched = true;
                            break;
                        }
                    }
                    if (!typeMatched)
                    {
                        // 文件类型不匹配
                        string stringBuilder = string.Format(AppResources.WrongFileTypeToast, string.Join("，*", types));
                        await Toast.Make(stringBuilder, CommunityToolkit.Maui.Core.ToastDuration.Short, 16).Show();
                        return string.Empty;
                    }
                    
                    // iOS 安全范围 URL 需要通过 Stream 读取，然后复制到应用可访问的目录
                    try
                    {
                        // 获取文件名
                        string fileName = Path.GetFileName(result.FullPath);
                        // 目标路径：Documents/Import/
                        string importDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Import");
                        if (!Directory.Exists(importDir))
                        {
                            Directory.CreateDirectory(importDir);
                        }
                        string destPath = Path.Combine(importDir, fileName);
                        
                        // 如果文件已存在，添加时间戳避免冲突
                        if (File.Exists(destPath))
                        {
                            string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                            string ext = Path.GetExtension(fileName);
                            destPath = Path.Combine(importDir, $"{nameWithoutExt}_{DateTime.Now:yyyyMMddHHmmss}{ext}");
                        }
                        
                        // 使用 FilePicker 返回的 Stream 读取文件（这是安全范围访问的正确方式）
                        using (var sourceStream = await result.OpenReadAsync())
                        using (var destStream = File.Create(destPath))
                        {
                            await sourceStream.CopyToAsync(destStream);
                        }
                        
                        Log.Information($"iOS: Copied file from picker to {destPath}");
                        return destPath;
                    }
                    catch (Exception copyEx)
                    {
                        Log.Error(copyEx, "iOS: Failed to copy file from picker");
                        // 如果复制失败，尝试直接返回路径（可能是应用内部的文件）
                        return result.FullPath;
                    }
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "iOS FilePicker failed");
                return string.Empty;
            }
#else
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
#endif
        }

        public static async Task<string> SaveFile(string[] types, ContentPage context, string initialDirectory = "", string initialFileName = "")
        {
            if (ExternalStorageService == null)
            {
                throw new InvalidOperationException("ExternalStorageService is not initialized. Call ObjectProvider.Initialize() first.");
            }
#if IOS
            // iOS 保存到应用的 Documents/Projects 目录
            try
            {
                string projectsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Projects");
                if (!Directory.Exists(projectsDir))
                {
                    Directory.CreateDirectory(projectsDir);
                }
                
                string fileName = string.IsNullOrEmpty(initialFileName) ? $"project_{DateTime.Now:yyyyMMddHHmmss}" : initialFileName;
                // 确保文件有正确的扩展名
                if (types.Length > 0 && !fileName.EndsWith(types[0], StringComparison.OrdinalIgnoreCase))
                {
                    fileName += types[0];
                }
                
                string filePath = Path.Combine(projectsDir, fileName);
                
                // 如果文件已存在，添加数字后缀
                int counter = 1;
                string baseName = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                while (File.Exists(filePath))
                {
                    filePath = Path.Combine(projectsDir, $"{baseName}_{counter}{ext}");
                    counter++;
                }
                
                Log.Information($"iOS: Save file path: {filePath}");
                return filePath;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "iOS SaveFile failed");
                return string.Empty;
            }
#else
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
#endif
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
