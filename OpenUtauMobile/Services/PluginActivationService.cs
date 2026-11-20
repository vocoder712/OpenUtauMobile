using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtau.Core.Util;
using OpenUtauMobile.Services.Projects;
using Serilog;

namespace OpenUtauMobile.Services;

/// <summary>持久化插件启用名单，并在启动时冻结本进程使用的加载范围。</summary>
public static class PluginActivationService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static HashSet<string>? startupPaths;
    private static HashSet<Guid> disabledModules = [];
    internal static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>查询已提交的启用状态，未列入名单的插件默认禁用。</summary>
    public static bool IsEnabled(string relativePath) =>
        Preferences.Default.EnabledPlugins.Contains(relativePath, PathComparer);

    /// <summary>串行保存名单，成功后才发布状态，避免安装与页面操作覆盖彼此。</summary>
    public static async Task SetEnabledAsync(string relativePath, bool enabled)
    {
        string normalized = Path.GetRelativePath(PluginManagementService.Root,
            PluginManagementService.ResolvePluginPath(relativePath));
        await Gate.WaitAsync();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                HashSet<string> paths = [with(Preferences.Default.EnabledPlugins, PathComparer)];
                if (enabled) paths.Add(normalized);
                else paths.Remove(normalized);
                List<string> saved = [.. paths.Order(PathComparer)];
                await PreferencesWriter.Inst.SaveAsync(preferences => preferences.EnabledPlugins = [.. saved]);
            });
        }
        finally { Gate.Release(); }
    }

    /// <summary>固定启动名单并记录禁用模块，只有用户启用或安装操作能够加入名单。</summary>
    public static async Task PrepareStartupAsync()
    {
        IReadOnlyList<PluginFileEntry> files = await Task.Run(PluginManagementService.ListFiles);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            startupPaths = [with(Preferences.Default.EnabledPlugins, PathComparer)];
        });
        HashSet<Guid> disabled = [];
        foreach (PluginFileEntry file in files)
        {
            if (startupPaths!.Contains(file.RelativePath)) continue;
            try
            {
                using FileStream stream = File.OpenRead(PluginManagementService.ResolvePluginPath(file.RelativePath));
                using PEReader pe = new(stream);
                if (pe.HasMetadata)
                {
                    MetadataReader reader = pe.GetMetadataReader();
                    disabled.Add(reader.GetGuid(reader.GetModuleDefinition().Mvid));
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or BadImageFormatException)
            { Log.Debug(error, "Failed to inspect disabled plugin {Plugin}", file.RelativePath); }
        }
        disabledModules = disabled;
    }

    /// <summary>在加载文件前使用启动快照过滤，内置目录中的程序集不受外部名单限制。</summary>
    public static bool ShouldLoadFile(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string prefix = Path.TrimEndingDirectorySeparator(PluginManagementService.Root) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return true;
        return startupPaths?.Contains(Path.GetRelativePath(PluginManagementService.Root, fullPath)) == true;
    }

    /// <summary>禁止已因依赖解析进入进程的禁用模块执行插件入口，兼容移动端映射路径。</summary>
    public static bool ShouldInitialize(Assembly assembly) =>
        !assembly.IsDynamic && !disabledModules.Contains(assembly.ManifestModule.ModuleVersionId);
}
