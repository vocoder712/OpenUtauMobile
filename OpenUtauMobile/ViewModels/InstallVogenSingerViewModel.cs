using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using OpenUtau.Core.Vogen;
using OpenUtau.Core;
using System.Diagnostics;

namespace OpenUtauMobile.ViewModels
{
    public class InstallVogenSingerViewModel : ReactiveObject
    {
        [Reactive] public string InstallPackagePath { get; set; } = string.Empty;

        [Reactive] public string InstallProgressText { get; set; } = "进度："; // 安装进度
        [Reactive] public string InstallProgressDetail { get; set; } = ""; // 安装进度消息
        [Reactive] public double InstallProgress { get; set; } = 0.0; // 安装进度, 0.0-1.0

        /// <summary>
        /// 更新安装进度信息，作为回调委托给安装器
        /// </summary>
        /// <param name="progress"></param>
        /// <param name="detail"></param>
        public void UpdateInstallProgress(double progress, string detail)
        {
            Debug.WriteLine($"Install Progress: {progress}, Detail: {detail}");
            InstallProgress = progress / 100;
            InstallProgressText = $"进度：{progress:0.##}%";
            InstallProgressDetail = $"{detail}";
        }

        public void Install()
        {
            VogenSingerInstaller.Install(InstallPackagePath, 
                //产生进度条通知，将一个可以发送进度通知的命令执行器传递给安装器，以便安装器更新进度条
                UpdateInstallProgress);
        }
    }
}
