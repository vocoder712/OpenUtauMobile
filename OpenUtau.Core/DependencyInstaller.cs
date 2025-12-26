using System;
using System.IO;
using System.Linq;
using System.Text;
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
        public static string FileExt = ".oudep";
        public static void Install(string archivePath) 
        {
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
            }
        }
    }
}
