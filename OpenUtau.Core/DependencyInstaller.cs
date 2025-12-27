using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpCompress.Archives;

namespace OpenUtau.Core 
{
    //Installation of dependencies (primarilly for diffsinger), including vocoder and phoneme timing model
    [Serializable]
    public class DependencyConfig 
    {
        public string name;
    }

    public class DependencyInstaller 
    {
        public static void Install(string archivePath, Action<double, string> progress) 
        {
            progress?.Invoke(0, "准备安装依赖项...");
            int counter = 0;
            DependencyConfig dependencyConfig;
            using var archive = ArchiveFactory.Open(archivePath);
            var configEntry = archive.Entries.First(e => e.Key == "oudep.yaml") ?? throw new ArgumentException("missing oudep.yaml");
            using (var stream = configEntry.OpenEntryStream())
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                dependencyConfig = Core.Yaml.DefaultDeserializer.Deserialize<DependencyConfig>(reader);
            }
            string name = dependencyConfig.name;
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("missing name in oudep.yaml");
            }
            var basePath = Path.Combine(PathManager.Inst.DependencyPath, name);
            foreach (var entry in archive.Entries)
            {
                counter++;
                if (string.IsNullOrEmpty(entry.Key) || entry.Key.Contains(".."))
                {
                    // Prevent zipSlip attack
                    continue;
                }
                var filePath = Path.Combine(basePath, entry.Key);
                var directoryPath = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(directoryPath))
                {
                    throw new ArgumentException($"Invalid entry path '{entry.Key}' in archive.");
                }
                Directory.CreateDirectory(directoryPath);
                if (!entry.IsDirectory)
                {
                    entry.WriteToFile(Path.Combine(basePath, entry.Key));
                }
                double progressValue = (double)counter / archive.Entries.Count() * 100;
                progress?.Invoke(progressValue, $"正在安装依赖项 {name} ({counter}/{archive.Entries.Count()})");
            }
        }
    }
}
