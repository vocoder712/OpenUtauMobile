using System;
using System.IO;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Services.SingerPlugins.Installation;

namespace OpenUtauMobile.Services.SingerPlugins
{
    /// <summary>保留内置卸载入口，重扫统一经过 Mobile 协调器。</summary>
    internal static class BuiltinSingerMaintenance
    {
        /// <summary>类型和实例均由 Core 提供时才允许内置目录卸载。</summary>
        internal static bool CanUninstall(USinger singer) => singer.GetType().Assembly == typeof(USinger).Assembly &&
            SingerPluginService.Registry.Snapshot.TypesByBridge.TryGetValue(singer.SingerType, out RegisteredSingerType? type) &&
            type.PluginId == "opum.builtin";

        internal static async Task UninstallAsync(USinger singer)
        {
            if (!CanUninstall(singer)) throw new InvalidOperationException("此歌手的维护操作由提供方管理。");
            string path = Path.GetFullPath(singer.Location);
            bool inside = false;
            foreach (string root in PathManager.Inst.SingersPaths)
                if (path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar,
                    InstallPaths.Comparison)) inside = true;
            if (!inside) throw new IOException("不能卸载歌手根目录或目录外的内容。");
            InstallPaths.RejectLinks(path);
            await SingerDiscoveryService.Instance.MaintainAsync(() => Task.Run(() =>
                {
                    singer.FreeMemory();
                    if (Directory.Exists(path)) Directory.Delete(path, true);
                    else if (File.Exists(path)) File.Delete(path);
                    else throw new FileNotFoundException("歌手位置不存在。", path);
                }));
        }
    }
}