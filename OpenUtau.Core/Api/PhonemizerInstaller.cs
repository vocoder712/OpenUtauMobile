using System;
using System.IO;
using System.Threading.Tasks;
using OpenUtau.Api;
using Serilog;

namespace OpenUtau.Core.Api
{
    public class PhonemizerInstaller
    {
        public static void Install(string filePath) {
            if (!File.Exists(filePath)) {
                throw new FileNotFoundException("Phonemizer DLL not found.", filePath);
            }

            string pluginsPath = PathManager.Inst.PluginsPath;
            Directory.CreateDirectory(pluginsPath);

            string fileName = Path.GetFileName(filePath);
            string destPath = Path.Combine(pluginsPath, fileName);
            File.Copy(filePath, destPath, true);

            string srcDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(srcDir) && Directory.Exists(srcDir)) {
                foreach (string dep in Directory.GetFiles(srcDir, "*.dll")) {
                    string depName = Path.GetFileName(dep);
                    if (string.Equals(depName, fileName, StringComparison.OrdinalIgnoreCase)) {
                        continue;
                    }
                    try {
                        File.Copy(dep, Path.Combine(pluginsPath, depName), true);
                    } catch (Exception e) {
                        Log.Warning(e, "Failed to copy dependency {Dep}", depName);
                    }
                }
            }

            try {
                PhonemizerFactory.LoadPluginDll(destPath);
            } catch (Exception e) {
                Log.Error(e, "Failed to load installed phonemizer {Path}", destPath);
                throw;
            }

            new Task(() => {
                DocManager.Inst.ExecuteCmd(new SingersChangedNotification());
                DocManager.Inst.ExecuteCmd(new ProgressBarNotification(0, $"Installed {fileName}"));
            }).Start(DocManager.Inst.MainScheduler);
        }
    }
}
