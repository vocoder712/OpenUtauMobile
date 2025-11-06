using DynamicData.Binding;
using OpenUtau.Core;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Android.OS;
using Environment = Android.OS.Environment;

namespace OpenUtauMobile.ViewModels
{
    /// <summary>
    /// 日志文件信息
    /// </summary>
    public class LogFileInfo
    {
        public string FileName { get; set; } = "";
        public string FullPath { get; set; } = "";
        public DateTime LastWriteTime { get; set; }
        public long Size { get; set; }
        public bool IsSelected { get; set; }

        public string SizeFormatted => Utils.FormatTools.FormatSize(Size);
        public string LastWriteTimeFormatted => LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public partial class LogExportViewModel : ReactiveObject
    {
        [Reactive] public ObservableCollectionExtended<LogFileInfo> LogFiles { get; set; } = new();
        [Reactive] public string ExportDirectory { get; set; } = "";
        [Reactive] public string StatusMessage { get; set; } = "";
        [Reactive] public bool IsExporting { get; set; } = false;
        [Reactive] public int SelectedCount { get; set; } = 0;

        public LogExportViewModel()
        {
            LoadLogFiles();
        }

        /// <summary>
        /// 加载日志文件列表
        /// </summary>
        public void LoadLogFiles()
        {
            try
            {
                LogFiles.Clear();
                StatusMessage = "";

                string logsPath = PathManager.Inst.LogsPath;
                if (!Directory.Exists(logsPath))
                {
                    StatusMessage = "日志目录不存在";
                    return;
                }

                var logFiles = Directory.GetFiles(logsPath, "*.txt", SearchOption.AllDirectories)
                    .Select(filePath => new FileInfo(filePath))
                    .OrderByDescending(file => file.LastWriteTime)
                    .Select(file => new LogFileInfo
                    {
                        FileName = file.Name,
                        FullPath = file.FullName,
                        LastWriteTime = file.LastWriteTime,
                        Size = file.Length,
                        IsSelected = false
                    })
                    .ToList();

                LogFiles.AddRange(logFiles);

                if (!LogFiles.Any())
                {
                    StatusMessage = "未找到日志文件";
                }
                else
                {
                    StatusMessage = $"找到 {LogFiles.Count} 个日志文件";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "加载日志文件失败");
                StatusMessage = $"加载失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 切换文件选择状态
        /// </summary>
        /// <param name="logFile"></param>
        public void ToggleFileSelection(LogFileInfo logFile)
        {
            logFile.IsSelected = !logFile.IsSelected;
            UpdateSelectedCount();
        }

        /// <summary>
        /// 全选/全不选
        /// </summary>
        /// <param name="selectAll"></param>
        public void SelectAll(bool selectAll)
        {
            foreach (var logFile in LogFiles)
            {
                logFile.IsSelected = selectAll;
            }
            UpdateSelectedCount();
        }

        /// <summary>
        /// 更新选中数量
        /// </summary>
        private void UpdateSelectedCount()
        {
            SelectedCount = LogFiles.Count(f => f.IsSelected);
        }

        /// <summary>
        /// 导出选中的日志文件
        /// </summary>
        /// <returns></returns>
        public async Task<bool> ExportSelectedFiles()
        {
            var selectedFiles = LogFiles.Where(f => f.IsSelected).ToList();
            if (!selectedFiles.Any())
            {
                StatusMessage = "请选择要导出的日志文件";
                return false;
            }

            if (string.IsNullOrEmpty(ExportDirectory))
            {
                StatusMessage = "请选择导出目录";
                return false;
            }

            if (!Directory.Exists(ExportDirectory))
            {
                StatusMessage = "导出目录不存在";
                return false;
            }

            IsExporting = true;
            StatusMessage = "正在导出...";

            try
            {
                // 创建以时间戳命名的子目录
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string exportSubDir = Path.Combine(ExportDirectory, $"OpenUtau_Logs_{timestamp}");
                Directory.CreateDirectory(exportSubDir);

                int successCount = 0;
                int totalCount = selectedFiles.Count;

                foreach (var logFile in selectedFiles)
                {
                    try
                    {
                        string destPath = Path.Combine(exportSubDir, logFile.FileName);

                        // 如果目标文件已存在，添加序号
                        int counter = 1;
                        string originalDestPath = destPath;
                        while (File.Exists(destPath))
                        {
                            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalDestPath);
                            string extension = Path.GetExtension(originalDestPath);
                            destPath = Path.Combine(exportSubDir, $"{fileNameWithoutExt}_{counter}{extension}");
                            counter++;
                        }

                        File.Copy(logFile.FullPath, destPath);
                        successCount++;

                        StatusMessage = $"正在导出... ({successCount}/{totalCount})";
                        await Task.Delay(50); // 给UI更新的时间
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"导出文件失败: {logFile.FileName}");
                    }
                }

                StatusMessage = $"导出完成: {successCount}/{totalCount} 个文件成功导出到 {exportSubDir}";
                Log.Information($"日志导出完成: {successCount}/{totalCount} 个文件导出到 {exportSubDir}");

                return successCount > 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "导出日志文件失败");
                StatusMessage = $"导出失败: {ex.Message}";
                return false;
            }
            finally
            {
                IsExporting = false;
            }
        }

        /// <summary>
        /// 初始化默认导出目录
        /// </summary>
        public void InitializeDefaultExportDirectory()
        {
            try
            {
                if (DeviceInfo.Platform == DevicePlatform.Android)
                {
                    ExportDirectory = Environment.GetExternalStoragePublicDirectory(Environment.DirectoryDownloads).AbsolutePath;
                }
                else if (DeviceInfo.Platform == DevicePlatform.iOS)
                {
                    ExportDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
                }
                else if (DeviceInfo.Platform == DevicePlatform.WinUI)
                {
                    ExportDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Desktop);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "设置默认导出目录失败");
                ExportDirectory = "";
            }
        }

        /// <summary>
        /// 刷新日志文件列表
        /// </summary>
        public void RefreshLogFiles()
        {
            LoadLogFiles();
        }
    }
}