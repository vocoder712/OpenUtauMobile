using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Serilog;

namespace OpenUtau.Core.Util
{
    /// <summary>
    /// 用于描述依赖项的数据结构
    /// </summary>
    public class DependencyInfo 
    {
        /// <summary>
        /// 名称 同目录名
        /// </summary>
        public string Name { get; init; } = string.Empty;
        /// <summary>
        /// 绝对路径
        /// </summary>
        public string FullPath { get; init; } = string.Empty;
        /// <summary>
        /// 占用空间大小（字节）
        /// </summary>
        public long Size { get; init; }
        public string DisplaySize => FileTools.FormatSize(Size);
        public override string ToString()
        {
            return $"{Name} ({Size} bytes) at {FullPath}";
        }
    }
    /// <summary>
    /// 单例类，管理已安装的依赖项
    /// </summary>
    public class DependencyManager : SingletonBase<DependencyManager>
    {
        private readonly Lock locker = new();
        /// <summary>
        /// 获取已安装的依赖项列表
        /// </summary>
        /// <returns></returns>
        public IReadOnlyList<DependencyInfo> ListInstalled()
        {
            lock (locker)
            {
                try
                {
                    string basePath = PathManager.Inst.DependencyPath;
                    Directory.CreateDirectory(basePath);
                    return [.. Directory.EnumerateDirectories(basePath, "*", SearchOption.TopDirectoryOnly)
                        .Select(dir =>
                        {
                            // 将每个文件夹视作一个依赖项
                            DirectoryInfo info = new(dir);
                            return new DependencyInfo {
                                Name = info.Name,
                                FullPath = info.FullName,
                                Size = FileTools.GetDirectorySize(info),
                            };
                        })
                        .OrderBy(info => info.Size, Comparer<long>.Create((x, y) => y.CompareTo(x)))]; // 按大小排序返回,降序
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "在获取已安装的依赖项列表时发生错误");
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification("未能获取已安装的依赖项列表", ex));
                    return [];
                }
            }
        }
        /// <summary>
        /// 删除指定名称的依赖项
        /// </summary>
        /// <param name="name"></param>
        /// <returns>是否成功</returns>
        public bool Delete(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }
            lock (locker)
            {
                try
                {
                    string basePath = PathManager.Inst.DependencyPath;
                    string targetPath = Path.Combine(basePath, name);
                    string fullBase = Path.GetFullPath(basePath);
                    string fullTarget = Path.GetFullPath(targetPath);

                    if (!fullTarget.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
                    {
                        Log.Warning("发现危险的删除请求，已阻止");
                        return false;
                    }
                    if (!Directory.Exists(fullTarget))
                    {
                        return false;
                    }

                    Directory.Delete(fullTarget, recursive: true); // 递归删除
                    Log.Information($"已删除位于 {fullTarget} 的依赖项 {name}");
                    return true;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"未能删除依赖项 {name}.");
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification($"未能删除依赖项 {name}", ex));
                    return false;
                }
            }
        }
    }
}
