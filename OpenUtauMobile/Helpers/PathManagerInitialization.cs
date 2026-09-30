using System;
using System.IO;
using System.Linq;
using OpenUtau.Core;

namespace OpenUtauMobile.Helpers
{
    /// <summary>在日志、偏好及其他服务初始化前注册路径并创建必要目录。</summary>
    public static class PathManagerInitialization
    {
        public static void Initialize(string rootPath, string dataPath, string cachePath, bool isInstalled = false)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(dataPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
            rootPath = Path.GetFullPath(rootPath);
            dataPath = Path.GetFullPath(dataPath);
            cachePath = Path.GetFullPath(cachePath);
            PathManager.RegisterPaths(new PathManager.PathConfiguration(
                rootPath, dataPath, cachePath, dataPath.All(character => character < 128), isInstalled));

            // 主动完成构造，确保后续服务始终读取同一份最终路径。
            PathManager manager = PathManager.Inst;
            Directory.CreateDirectory(manager.DataPath);
            Directory.CreateDirectory(manager.CachePath);
            Directory.CreateDirectory(manager.LogsPath);
        }
    }
}
