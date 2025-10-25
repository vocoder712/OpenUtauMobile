using DynamicData.Binding;
using OpenUtau.Core.Util;
using OpenUtauMobile.Resources.Strings;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace OpenUtauMobile.ViewModels.Controls
{
    public partial class FileSaverPopupViewModel : ReactiveObject
    {
        [Reactive] public string CurrentDirectory { get; set; } = "";
        [Reactive] public string FileName { get; set; } = "";
        [Reactive] public ObservableCollectionExtended<FileSystemInfo> Items { get; set; } = new ObservableCollectionExtended<FileSystemInfo>();
        [Reactive] public FileSystemInfo? SelectedItem { get; set; }
        [Reactive] public string ErrorMessage { get; set; } = "";
        [Reactive] public string WarningMessage { get; set; } = "";
        public string[] Types { get; set; } = ["*"];

        public FileSaverPopupViewModel()
        {
            this.WhenAnyValue(x => x.CurrentDirectory)
                .Subscribe(path =>
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        LoadDirectory(path);
                    }
                });
        }
        
        public void Initialize()
        {
            if (!string.IsNullOrEmpty(CurrentDirectory))
            {
                LoadDirectory(CurrentDirectory);
                return;
            }
            string defaultPath = "";
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                defaultPath = "/storage/emulated/0";
            }
            else if (DeviceInfo.Platform == DevicePlatform.iOS)
            {
                defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            else if (DeviceInfo.Platform == DevicePlatform.WinUI)
            {
                defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            CurrentDirectory = defaultPath;
            if (string.IsNullOrEmpty(FileName))
            {
                FileName = AppResources.NewFile;
            }
            FileName = FileName + (Types.Length > 0 && Types[0] != "*" ? Types[0].TrimStart('*') : "");
        }

        public void LoadDirectory(string path)
        {
            Items.Clear();
            ErrorMessage = "";
            WarningMessage = "";
            try
            {
                DirectoryInfo directoryInfo = new(path);
                List<FileSystemInfo> allItems = [];

                // 添加目录，按名称排序
                var directories = directoryInfo.GetDirectories().LocalizedOrderBy(dir => dir.Name);
                allItems.AddRange(directories);

                // 添加文件，按名称排序
                if (Types.Length == 0)
                {
                    Types = ["*"];
                }
                if (Types.Contains("*"))
                {
                    // 如果包含通配符，则获取所有文件
                    var files = directoryInfo.GetFiles().LocalizedOrderBy(file => file.Name);
                    allItems.AddRange(files);
                }
                else
                {
                    List<FileInfo> allFiles = [];
                    foreach (string type in Types)
                    {
                        allFiles.AddRange(directoryInfo.GetFiles($"*{type}"));
                    }
                    IEnumerable<FileInfo> sortedFiles = allFiles.LocalizedOrderBy(file => file.Name);
                    allItems.AddRange(sortedFiles);
                }

                Items.AddRange(allItems);

                if (Items.Count == 0)
                {
                    WarningMessage = AppResources.WarningNoDirOrFilesMatchingCriteria;
                }
            }
            catch (Exception ex)
            {
                if (ex is UnauthorizedAccessException)
                {
                    ErrorMessage = AppResources.WarningNoDirectoryPermission;
                }
                else
                {
                    ErrorMessage = string.Format(AppResources.Error, ex.Message);
                }
            }
        }
    }
}
