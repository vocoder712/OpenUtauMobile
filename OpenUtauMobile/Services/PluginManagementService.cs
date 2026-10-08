using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.ViewModels;
using Serilog;

namespace OpenUtauMobile.Services;

public sealed record PluginFileEntry(string RelativePath, long Size, bool IsPendingDeletion)
{
    public string FileName => Path.GetFileName(RelativePath);
    public string SizeText => $"{Size / 1024d:N1} KB";
}

/// <summary>管理插件文件，不在安装或刷新时加载程序集。</summary>
public static class PluginManagementService
{
    private const string BuiltinFileName = "OpenUtau.Plugin.Builtin.dll";
    private static string Root => Path.GetFullPath(PathManager.Inst.PluginsPath);
    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static IReadOnlyList<PluginFileEntry> ListFiles()
    {
        Directory.CreateDirectory(Root);
        HashSet<string> pending = ReadPendingDeletions();
        // 不跟随目录链接，避免将插件目录之外的文件纳入管理。
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        return Directory.EnumerateFiles(Root, "*", options)
            .Where(path => string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
            .Select(path => new PluginFileEntry(
                Path.GetRelativePath(Root, path), new FileInfo(path).Length,
                pending.Contains(Path.GetRelativePath(Root, path))))
            .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static async Task InstallAsync(string sourcePath, LoadingPopupViewModel loading)
    {
        // TODO：补充 iOS 外部程序集执行能力和 Web 导入、加载链路的专门适配。
        string fileName = Path.GetFileName(sourcePath);
        if (!string.Equals(Path.GetExtension(fileName), ".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.S("PluginManager.InvalidAssembly"));
        if (string.Equals(fileName, BuiltinFileName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.S("PluginManager.BuiltinNotAllowed"));

        await using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        loading.UpdateProgress(0, L.S("PluginManager.Checking"));
        ValidateAssembly(source);
        source.Position = 0;

        Directory.CreateDirectory(Root);
        string destination = ResolvePluginPath(fileName);
        if (File.Exists(destination) || ReadPendingDeletions().Contains(fileName))
            throw new IOException(L.S("PluginManager.AlreadyExists"));

        // 临时文件不使用 DLL 扩展名，只有完整复制后才进入插件列表。
        string temporaryPath = Path.Combine(Root, $".{Guid.NewGuid():N}.installing");
        try
        {
            await using (FileStream target = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                long copied = 0;
                int count;
                loading.UpdateProgress(10, L.S("PluginManager.Copying"));
                while ((count = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, count));
                    copied += count;
                    loading.UpdateProgress(10 + 85d * copied / source.Length);
                }
                await target.FlushAsync();
            }
            File.Move(temporaryPath, destination);
            loading.UpdateProgress(95, L.S("PluginManager.Completed"));
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static void ValidateAssembly(Stream source)
    {
        try
        {
            using PEReader reader = new(source, PEStreamOptions.LeaveOpen);
            if (!reader.HasMetadata || reader.PEHeaders.CorHeader == null)
                throw new BadImageFormatException();
            MetadataReader metadata = reader.GetMetadataReader();
            if (!metadata.IsAssembly) throw new BadImageFormatException();
            _ = metadata.GetAssemblyDefinition();
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException(L.S("PluginManager.InvalidAssembly"), exception);
        }
    }

    /// <summary>仅登记删除任务，插件文件统一在下次启动加载前删除。</summary>
    public static void ScheduleDeletion(string relativePath)
    {
        string path = ResolvePluginPath(relativePath);
        HashSet<string> pending = ReadPendingDeletions();
        pending.Add(Path.GetRelativePath(Root, path));
        WritePendingDeletions(pending);
    }

    /// <summary>取消待删除标记，不修改插件文件。</summary>
    public static void CancelDeletion(string relativePath)
    {
        string path = ResolvePluginPath(relativePath);
        HashSet<string> pending = ReadPendingDeletions();
        pending.Remove(Path.GetRelativePath(Root, path));
        List<string> previous = Preferences.Default.PendingPluginDeletions;
        try
        {
            WritePendingDeletions(pending);
        }
        catch
        {
            // 取消未能保存时仍显示待删除标记，避免误认为下次启动不会删除。
            Preferences.Default.PendingPluginDeletions = previous;
            throw;
        }
    }

    private static void DeletePluginFile(string path)
    {
        // 用户已经确认删除；只读属性不应阻止删除，也不修改文件的访问权限。
        if (File.Exists(path))
        {
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }
        File.Delete(path);
    }

    /// <summary>必须在启动时的插件扫描之前调用，任务只消费一次，失败也不保留。</summary>
    public static int ApplyPendingDeletions()
    {
        try
        {
            HashSet<string> pending = ReadPendingDeletions();
            if (pending.Count == 0) return 0;
            int failures = 0;
            // 先清空并保存队列，再执行删除，避免失败或中途退出后反复重试。
            try
            {
                WritePendingDeletions([with(PathComparer)]);
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Failed to consume pending plugin deletions.");
                failures++;
            }
            foreach (string relativePath in pending)
            {
                try
                {
                    DeletePluginFile(ResolvePluginPath(relativePath));
                }
                catch (Exception exception)
                {
                    failures++;
                    Log.Warning(exception, "Failed to delete pending plugin {Plugin}", relativePath);
                }
            }
            return failures;
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to process pending plugin deletions");
            return 1;
        }
    }

    private static HashSet<string> ReadPendingDeletions()
    {
        return [with(Preferences.Default.PendingPluginDeletions ?? [], PathComparer)];
    }

    private static void WritePendingDeletions(HashSet<string> pending)
    {
        Preferences.Default.PendingPluginDeletions = pending.OrderBy(path => path, PathComparer).ToList();
        Preferences.Save();

        // 保存方法会自行捕获异常，回读字段以免将未持久化的任务提示为成功。
        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(PathManager.Inst.PrefsFilePath));
        string[] persisted = saved.RootElement.GetProperty(nameof(Preferences.SerializablePreferences.PendingPluginDeletions))
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        if (!pending.SetEquals(persisted))
        {
            throw new IOException("Failed to save pending plugin deletions in preferences.");
        }
    }

    private static string ResolvePluginPath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string path = Path.GetFullPath(Path.Combine(Root, relativePath));
        string prefix = Path.TrimEndingDirectorySeparator(Root) + Path.DirectorySeparatorChar;
        if (Path.IsPathRooted(relativePath) || !path.StartsWith(prefix,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || !string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid plugin path.");
        return path;
    }
}
