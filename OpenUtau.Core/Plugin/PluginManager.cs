using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenUtau.Api;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Plugins {
    /// <summary>已安装插件的信息。</summary>
    public class InstalledPlugin {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime LastWriteTime { get; set; }
        public bool IsLoaded { get; set; }
        public string? LoadError { get; set; }
        public int PhonemizerCount { get; set; }
        public string? AssemblyName { get; set; }
    }

    /// <summary>
    /// 插件管理器：统一扫描、导入、卸载 PluginsPath 下的 DLL。
    /// 目前主要注册音素器；后续可扩展注册其它类型。
    /// </summary>
    public class PluginManager : SingletonBase<PluginManager> {
        private static readonly object loadLock = new();
        private readonly Dictionary<string, InstalledPlugin> loadedPlugins =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>启动时扫描 PluginsPath 下所有 DLL 并加载。</summary>
        public void LoadAll() {
            string pluginsPath;
            try {
                pluginsPath = PathManager.Inst.PluginsPath;
            } catch (Exception e) {
                Log.Warning(e, "Cannot resolve PluginsPath when loading plugins.");
                return;
            }
            if (!Directory.Exists(pluginsPath)) {
                return;
            }
            foreach (string dllPath in Directory.GetFiles(pluginsPath, "*.dll")) {
                TryLoad(dllPath);
            }
        }

        /// <summary>列出 PluginsPath 下所有 DLL 的状态。</summary>
        public List<InstalledPlugin> GetInstalledPlugins() {
            var list = new List<InstalledPlugin>();
            string pluginsPath;
            try {
                pluginsPath = PathManager.Inst.PluginsPath;
            } catch {
                return list;
            }
            if (!Directory.Exists(pluginsPath)) {
                return list;
            }

            foreach (string dllPath in Directory.GetFiles(pluginsPath, "*.dll")) {
                var fi = new FileInfo(dllPath);
                if (loadedPlugins.TryGetValue(dllPath, out var cached)) {
                    cached.FileSize = fi.Length;
                    cached.LastWriteTime = fi.LastWriteTime;
                    list.Add(cached);
                } else {
                    list.Add(new InstalledPlugin {
                        FileName = fi.Name,
                        FullPath = fi.FullName,
                        FileSize = fi.Length,
                        LastWriteTime = fi.LastWriteTime,
                        IsLoaded = false,
                        LoadError = null,
                    });
                }
            }
            return list.OrderBy(p => p.FileName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 从外部路径导入一个 DLL 到 PluginsPath 并加载。
        /// 会同时复制所选 DLL 同目录下的其它 DLL 作为依赖。
        /// </summary>
        public InstalledPlugin Import(string srcPath) {
            if (!File.Exists(srcPath)) {
                throw new FileNotFoundException("Plugin DLL not found.", srcPath);
            }

            string pluginsPath = PathManager.Inst.PluginsPath;
            Directory.CreateDirectory(pluginsPath);

            string fileName = Path.GetFileName(srcPath);
            string destPath = Path.Combine(pluginsPath, fileName);

            if (string.Equals(Path.GetFullPath(srcPath), Path.GetFullPath(destPath),
                    StringComparison.OrdinalIgnoreCase)) {
                return TryLoad(destPath) ?? throw new InvalidOperationException("Failed to load plugin.");
            }

            File.Copy(srcPath, destPath, true);

            string srcDir = Path.GetDirectoryName(srcPath) ?? string.Empty;
            if (Directory.Exists(srcDir)) {
                foreach (string dep in Directory.GetFiles(srcDir, "*.dll")) {
                    string depName = Path.GetFileName(dep);
                    if (string.Equals(depName, fileName, StringComparison.OrdinalIgnoreCase)) continue;
                    try {
                        File.Copy(dep, Path.Combine(pluginsPath, depName), true);
                    } catch (Exception e) {
                        Log.Warning(e, "Failed to copy dependency {Dep}", depName);
                    }
                }
            }

            return TryLoad(destPath) ?? throw new InvalidOperationException("Failed to load plugin.");
        }

        /// <summary>
        /// 删除一个已安装的插件。
        /// 注意：DLL 一旦被加载，程序集在进程结束前无法卸载；文件删除是立即生效的，重启 App 后彻底消失。
        /// </summary>
        public void Uninstall(InstalledPlugin plugin) {
            if (plugin == null) return;
            string fullPath = plugin.FullPath;

            lock (loadLock) {
                loadedPlugins.Remove(fullPath);
            }

            try {
                if (File.Exists(fullPath)) {
                    File.Delete(fullPath);
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to delete plugin file {Path}", fullPath);
                throw;
            }
        }

        private InstalledPlugin? TryLoad(string dllPath) {
            lock (loadLock) {
                if (loadedPlugins.TryGetValue(dllPath, out var existing) && existing.IsLoaded) {
                    return existing;
                }

                var fi = new FileInfo(dllPath);
                var info = new InstalledPlugin {
                    FileName = fi.Name,
                    FullPath = fi.FullName,
                    FileSize = fi.Length,
                    LastWriteTime = fi.LastWriteTime,
                };

                try {
                    Assembly assembly = Assembly.LoadFrom(dllPath);
                    info.AssemblyName = assembly.GetName().Name;

                    int count = PhonemizerFactory.RegisterFromAssembly(assembly);
                    info.PhonemizerCount = count;
                    info.IsLoaded = true;
                    info.LoadError = null;
                    Log.Information("Loaded plugin {File} with {Count} phonemizer(s).",
                        info.FileName, count);
                } catch (Exception e) {
                    info.IsLoaded = false;
                    info.LoadError = e.Message;
                    Log.Error(e, "Failed to load plugin {Path}", dllPath);
                }

                loadedPlugins[dllPath] = info;
                return info;
            }
        }
    }
}