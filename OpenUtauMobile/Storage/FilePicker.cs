using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OpenUtauMobile.Controls;
using OpenUtauMobile.Services;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Storage;

/// <summary>
/// 完全封装的文件选择器
/// </summary>
public static class FilePicker
{
    private static readonly bool UseInternalPicker = OperatingSystem.IsAndroid()
#if DEBUG
                                                     || OperatingSystem.IsWindows()
#endif
        ;
    /// <summary>
    /// 权限检查
    /// </summary>
    /// <returns></returns>
    private static bool CheckAndRequestStoragePermission()
    {
        IExternalStorageService? service = ServiceHub.ExternalStorageService;
        if (service == null) return false;
        if (!service.HasManageExternalStoragePermissionAsync())
        {
            service.RequestManageExternalStoragePermission();
            return false;
        }

        return true;
    }

    /// <summary>
    /// 使用内置文件选择器
    /// </summary>
    private static async Task<string> PickSingleFileInternalAsync(string title, string[] filters)
    {
        if (!CheckAndRequestStoragePermission()) return string.Empty;
        string? result = await RunOnUiThreadAsync(() =>
            PopupService.Show<string>(new FilePickerPopup(), new FilePickerPopupViewModel(title, filters)));
        return result ?? string.Empty;
    }

    /// <summary>
    /// 引导用户选取一个文件
    /// </summary>
    /// <param name="title">弹窗标题</param>
    /// <param name="filters">过滤器，形如["*.wav"]</param>
    /// <returns>选中的文件路径，如果取消或失败则返回 <see cref="string.Empty"/></returns>
    public static async Task<string> PickSingleFileAsync(string title, string[] filters)
    {
        // Android 需要特殊处理
        if (UseInternalPicker)
        {
            return await PickSingleFileInternalAsync(title, filters);
        }

        IStorageProvider? storageProvider = StorageProviderFactory.GetStorageProvider();
        if (storageProvider is null || !storageProvider.CanOpen)
            return string.Empty;

        FilePickerFileType[]? fileTypes = filters.Length > 0
            ? [new FilePickerFileType("Files") { Patterns = filters }]
            : null;

        FilePickerOpenOptions options = new()
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = fileTypes
        };

        IReadOnlyList<IStorageFile> files = await storageProvider.OpenFilePickerAsync(options);
        if (files.Count == 0) return string.Empty;
        return files[0].TryGetLocalPath() ?? string.Empty;
    }

    /// <summary>
    /// 使用内置文件夹选择器（Android 使用自定义 UI）。
    /// 取消或失败时返回 <see cref="string.Empty"/>。
    /// </summary>
    private static async Task<string> PickFolderInternalAsync(string title)
    {
        if (!CheckAndRequestStoragePermission()) return string.Empty;
        string? result = await RunOnUiThreadAsync(() =>
            PopupService.Show<string>(new FilePickerPopup(), new FolderPickerViewModel(title)));
        return result ?? string.Empty;
    }

    /// <summary>
    /// 使用系统文件夹选择器选择一个目录，全平台统一入口。
    /// Android 回退至内置 UI。
    /// 取消或失败时返回 <see cref="string.Empty"/>。
    /// </summary>
    public static async Task<string> PickFolderAsync(string title)
    {
        if (UseInternalPicker)
            return await PickFolderInternalAsync(title);

        IStorageProvider? storageProvider = StorageProviderFactory.GetStorageProvider();
        if (storageProvider is null || !storageProvider.CanPickFolder)
            return string.Empty;

        FolderPickerOpenOptions options = new()
        {
            Title = title,
            AllowMultiple = false,
        };

        IReadOnlyList<IStorageFolder> folders = await storageProvider.OpenFolderPickerAsync(options);
        if (folders.Count == 0) return string.Empty;
        return folders[0].TryGetLocalPath() ?? string.Empty;
    }

    /// <summary>
    /// 文件保存对话框，全平台统一入口。
    /// Android / Windows 使用内置 UI；其他平台使用系统 <see cref="IStorageProvider.SaveFilePickerAsync"/>。
    /// </summary>
    /// <param name="title">对话框标题</param>
    /// <param name="extension">强制后缀（含点或不含均可，如 ".ustx" 或 "ustx"）</param>
    /// <param name="defaultFileName">预填文件名（不含后缀）</param>
    /// <param name="initialPath">初始目录</param>
    /// <returns>完整目标路径；取消时返回 <see cref="string.Empty"/>。</returns>
    public static async Task<string> SaveFileAsync(
        string title, string extension,
        string defaultFileName = "", string initialPath = "")
    {
        if (UseInternalPicker)
        {
            if (!CheckAndRequestStoragePermission()) return string.Empty;
            string? result = await RunOnUiThreadAsync(() =>
                PopupService.Show<string?>(new FilePickerPopup(),
                    new FileSavePickerViewModel(title, extension, initialPath, defaultFileName)));
            return result ?? string.Empty;
        }

        // 其他平台：使用系统 SaveFilePicker
        IStorageProvider? storageProvider = StorageProviderFactory.GetStorageProvider();
        if (storageProvider is null || !storageProvider.CanSave) return string.Empty;

        string ext = extension.StartsWith('.') ? extension : "." + extension;
        IStorageFile? file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            DefaultExtension = ext,
            FileTypeChoices = [new FilePickerFileType("Files") { Patterns = [$"*{ext}"] }],
        });
        return file?.TryGetLocalPath() ?? string.Empty;
    }

    private static async Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return await action();

        TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                tcs.SetResult(await action());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, DispatcherPriority.Send);

        return await tcs.Task;
    }
}