using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtauMobile.Plugin.Abstractions;
using Serilog;

namespace OpenUtauMobile.Services.SingerPlugins;

/// <summary>按插件隔离存储；只在用户打开页面时创建插件网页会话。</summary>
public static class PluginWebSettingsService
{
    /// <summary>插件 ID 直接作为独占目录名，不迁移或读取旧哈希目录。</summary>
    public static PluginStorageContext Storage(string pluginId)
    {
        SingerPluginContract.ValidatePluginId(pluginId);
        return new(Path.Combine(PathManager.Inst.DataPath, "PluginData", pluginId));
    }

    /// <summary>按模块标识映射已登记的插件，支持移动端映射路径及内存加载，不加载未激活的 DLL。</summary>
    public static RegisteredSingerPlugin? FindPlugin(string relativePath)
    {
        string path = Path.GetFullPath(Path.Combine(PathManager.Inst.PluginsPath, relativePath));
        try
        {
            using FileStream stream = File.OpenRead(path);
            using PEReader pe = new(stream);
            if (!pe.HasMetadata) return null;
            MetadataReader metadata = pe.GetMetadataReader();
            Guid moduleId = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            return SingerPluginService.Registry.Snapshot.Instances.Values.FirstOrDefault(plugin =>
                !plugin.Assembly.IsDynamic && plugin.Assembly.ManifestModule.ModuleVersionId == moduleId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            Log.Debug(exception, "Failed to identify loaded plugin {Plugin}", relativePath);
            return null;
        }
    }

    private static PluginWebSettingsContext Context(string pluginId) => new(Storage(pluginId), new SingerPluginRuntime.PluginLog(pluginId));

    /// <summary>查询当前声明，不缓存登记时的能力状态。</summary>
    public static ValueTask<PluginWebSettingsDescriptor?> QueryAsync(RegisteredSingerPlugin plugin, CancellationToken token) =>
        plugin.Instance.GetWebSettingsAsync(Context(plugin.PluginId), token);

    /// <summary>再次查询后打开，处理查询与点击之间能力改变的情况。</summary>
    public static async Task<IPluginWebSettingsSession> OpenAsync(RegisteredSingerPlugin plugin, CancellationToken token)
    {
        PluginWebSettingsContext context = Context(plugin.PluginId);
        if (await plugin.Instance.GetWebSettingsAsync(context, token) == null)
            throw new InvalidOperationException(Helpers.L.S("PluginWebSettings.Unavailable"));
        Directory.CreateDirectory(context.Storage.DataDirectory);
        IPluginWebSettingsSession session = await plugin.Instance.OpenWebSettingsAsync(context, token);
        try
        {
            token.ThrowIfCancellationRequested();
            if (session.Address is not { IsAbsoluteUri: true } address ||
                (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException(Helpers.L.S("PluginWebSettings.InvalidAddress"));
            return session;
        }
        catch { await session.DisposeAsync(); throw; }
    }
}
