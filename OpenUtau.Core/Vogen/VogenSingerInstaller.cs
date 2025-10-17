using System;
using System.IO;
using System.Threading.Tasks;

namespace OpenUtau.Core.Vogen {
    public class VogenSingerInstaller {
        public const string FileExt = ".vogeon";
        public static void Install(string filePath, Action<double, string> progress) {
            progress.Invoke(0, "准备安装……");
            string fileName = Path.GetFileName(filePath);
            string destName = Path.Combine(PathManager.Inst.SingersInstallPath, fileName);
            if (File.Exists(destName)) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification($"{destName} already exist!"));
                return;
            }
            progress.Invoke(50, $"复制文件{fileName}……");
            File.Copy(filePath, destName);
            new Task(() => {
                DocManager.Inst.ExecuteCmd(new SingersChangedNotification());
                progress.Invoke(100, "安装完成！");
            }).Start(DocManager.Inst.MainScheduler);
        }
    }
}
