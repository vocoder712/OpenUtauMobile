using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OpenUtau.Core.Util;
using OpenUtauMobile.Plugin.Abstractions;
using OpenUtauMobile.Services.Projects;
using OpenUtauMobile.Services.SingerPlugins;
using Serilog;

namespace OpenUtauMobile.Services;

/// <summary>在插件启动前执行用户已确认的数据清除，避免与运行中的插件争用文件。</summary>
public static class PluginDataService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>查询插件是否已登记下次启动清除数据。</summary>
    public static bool IsPending(string pluginId) => Preferences.Default.PendingPluginDataClears.Contains(pluginId, StringComparer.Ordinal);

    /// <summary>等待清除或取消任务持久化，当前进程内不触碰插件数据。</summary>
    public static async Task SetPendingAsync(string pluginId, bool clear)
    {
        SingerPluginContract.ValidatePluginId(pluginId);
        await Gate.WaitAsync();
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                HashSet<string> pending = [with(Preferences.Default.PendingPluginDataClears, StringComparer.Ordinal)];
                if (clear) pending.Add(pluginId);
                else pending.Remove(pluginId);
                List<string> saved = [.. pending.Order(StringComparer.Ordinal)];
                await PreferencesWriter.Inst.SaveAsync(preferences => preferences.PendingPluginDataClears = [.. saved]);
            });
        }
        finally { Gate.Release(); }
    }

    /// <summary>启动时一次性消费清除请求；持久化失败不删除，执行失败不会静默重试。</summary>
    public static async Task<int> ApplyPendingAsync()
    {
        await Gate.WaitAsync();
        try
        {
            string[] pending = await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                string[] ids = [.. Preferences.Default.PendingPluginDataClears];
                if (ids.Length > 0)
                    await PreferencesWriter.Inst.SaveAsync(preferences => preferences.PendingPluginDataClears = []);
                return ids;
            });
            return await Task.Run(() =>
            {
                int failures = 0;
                foreach (string id in pending)
                {
                    try { Clear(id); }
                    catch (Exception error)
                    {
                        failures++;
                        Log.Warning(error, "Failed to clear plugin data {PluginId}", id);
                    }
                }
                return failures;
            });
        }
        catch (Exception error) { Log.Error(error, "Failed to process pending plugin data clears"); return 1; }
        finally { Gate.Release(); }
    }

    /// <summary>目录由验证后的插件 ID 唯一确定，先检查整个子树及父级，拒绝跨目录链接。</summary>
    private static void Clear(string pluginId)
    {
        string directory = Path.GetFullPath(PluginWebSettingsService.Storage(pluginId).DataDirectory);
        if (!Directory.Exists(directory)) return;
        for (DirectoryInfo? parent = new(directory); parent != null; parent = parent.Parent)
            RejectLink(parent);
        Inspect(new DirectoryInfo(directory));
        Directory.Delete(directory, recursive: true);
    }

    private static void Inspect(DirectoryInfo directory)
    {
        RejectLink(directory);
        foreach (FileSystemInfo item in directory.EnumerateFileSystemInfos())
        {
            RejectLink(item);
            if (item is DirectoryInfo child) Inspect(child);
        }
    }

    private static void RejectLink(FileSystemInfo item)
    {
        if ((item.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("插件数据目录包含链接，无法自动清除。");
    }
}
