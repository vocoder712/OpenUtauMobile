using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Util;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.ViewModels;

namespace OpenUtauMobile.Services;

/// <summary>重采样器目录中的一个文件，以及引擎当前是否已将其注册为可选重采样器。</summary>
public sealed record ResamplerFileEntry(string RelativePath, long Size, bool IsAvailable)
{
    /// <summary>不含目录的文件名。</summary>
    public string FileName => Path.GetFileName(RelativePath);

    /// <summary>以 KB 表示的文件大小。</summary>
    public string SizeText => $"{Size / 1024d:N1} KB";
}

/// <summary>
/// 管理自定义重采样器文件。安装和移除后重新扫描引擎注册表；
/// “可用”只表示引擎已识别该文件，不代表程序能在当前设备上实际运行。
/// </summary>
public static class ResamplerManagementService
{
    // 内置重采样器在 Core 中是 internal 类型，Mobile 层无法引用其常量，只能按名称保留。
    private static readonly string[] BuiltinNames = ["worldline", "hifisampler"];

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Root => Path.GetFullPath(PathManager.Inst.ResamplersPath);

    /// <summary>文件选择器的过滤器。内置选择器无法匹配无扩展名文件，非 Windows 平台因此不加过滤。</summary>
    public static string[] PickerFilters => OperatingSystem.IsWindows() ? ["*.exe", "*.bat"] : [];

    /// <summary>当前设备可添加的文件类型，供界面提示使用。</summary>
    public static string DescribeSupportedTypes()
    {
        List<string> types;
        if (OperatingSystem.IsWindows())
        {
            types = [".exe", ".bat"];
        }
        else
        {
            types = [".sh", L.S("ResamplerManager.NoExtension")];
            if (!string.IsNullOrEmpty(Preferences.Default.WinePath)) types.AddRange([".exe", ".bat"]);
        }
        return string.Join(", ", types);
    }

    /// <summary>按引擎扫描重采样器时相同的规则，判断当前设备能否加载该文件类型。</summary>
    public static bool IsLoadableFile(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is ".exe" or ".bat")
            return OperatingSystem.IsWindows() || !string.IsNullOrEmpty(Preferences.Default.WinePath);
        return !OperatingSystem.IsWindows() && (extension == ".sh" || extension.Length == 0);
    }

    /// <summary>目录中可能是重采样器的文件；不含配置文件和安装中的临时文件。</summary>
    private static bool IsCandidate(string path)
    {
        string fileName = Path.GetFileName(path);
        if (fileName.StartsWith('.')) return false;
        return Path.GetExtension(fileName).ToLowerInvariant() is ".exe" or ".bat" or ".sh" or "";
    }

    /// <summary>列出重采样器目录中的文件，并标出引擎已注册的项。</summary>
    public static IReadOnlyList<ResamplerFileEntry> ListFiles()
    {
        string root = Root;
        Directory.CreateDirectory(root);
        // 内置重采样器位于应用根目录，不会与此目录下的文件重合。
        HashSet<string> registered = new(
            ToolsManager.Inst.Resamplers
                .Select(resampler => resampler.FilePath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Select(Path.GetFullPath),
            PathComparer);
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        return
        [
            .. Directory.EnumerateFiles(root, "*", options)
                .Where(IsCandidate)
                .Select(path => new ResamplerFileEntry(
                    Path.GetRelativePath(root, path),
                    new FileInfo(path).Length,
                    registered.Contains(Path.GetFullPath(path))))
                .OrderBy(entry => entry.RelativePath, StringComparer.OrdinalIgnoreCase)
        ];
    }

    /// <summary>在询问信任前检查所选文件，不写入重采样器目录。</summary>
    public static void ValidateForInstall(string sourcePath)
    {
        if (!File.Exists(sourcePath) || !IsLoadableFile(sourcePath))
            throw new InvalidDataException(L.S("ResamplerManager.UnsupportedFile"));
        // 与内置重采样器同名的文件会在引擎扫描时被忽略，提前拒绝以免出现安装成功却不可用的文件。
        if (BuiltinNames.Contains(Path.GetFileName(sourcePath), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException(L.S("ResamplerManager.BuiltinNotAllowed"));
        if (PathComparer.Equals(Path.GetFullPath(sourcePath), GetDestination(sourcePath)))
            throw new InvalidDataException(L.S("ResamplerManager.AlreadyInstalled"));
    }

    /// <summary>安装该文件会覆盖已有的同名重采样器。</summary>
    public static bool WillReplace(string sourcePath) => File.Exists(GetDestination(sourcePath));

    private static string GetDestination(string sourcePath) => Path.Combine(Root, Path.GetFileName(sourcePath));

    /// <summary>
    /// 复制重采样器及其同名 YAML 清单到重采样器目录。
    /// 先复制到临时文件，全部成功后再发布；清单先于程序发布，失败时不会留下半个安装。
    /// </summary>
    public static async Task InstallAsync(string sourcePath, LoadingPopupViewModel loading)
    {
        // 确认期间文件或目录状态可能变化，复制前再次检查。
        ValidateForInstall(sourcePath);
        string root = Root;
        Directory.CreateDirectory(root);

        string destination = GetDestination(sourcePath);
        // 清单约定与引擎一致：与程序同名、扩展名为 .yaml。
        string manifestSource = Path.ChangeExtension(sourcePath, ".yaml");
        string manifestDestination = Path.ChangeExtension(destination, ".yaml");
        bool hasManifest = File.Exists(manifestSource);
        bool manifestExisted = File.Exists(manifestDestination);

        List<string> temporaryFiles = [];
        bool manifestPublished = false;
        try
        {
            loading.UpdateProgress(5, L.S("ResamplerManager.Installing"));
            string programTemporary = await CopyToTemporaryAsync(sourcePath, root, temporaryFiles);
            // 引擎不会调用 CheckPermissions，从选择器复制来的文件没有执行位，需在此补上。
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsBrowser())
            {
                File.SetUnixFileMode(programTemporary,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            loading.UpdateProgress(70);

            string? manifestTemporary = hasManifest
                ? await CopyToTemporaryAsync(manifestSource, root, temporaryFiles)
                : null;
            loading.UpdateProgress(85);

            if (manifestTemporary != null)
            {
                File.Move(manifestTemporary, manifestDestination, overwrite: true);
                manifestPublished = true;
            }
            File.Move(programTemporary, destination, overwrite: true);
            loading.UpdateProgress(95);
        }
        catch
        {
            // 程序未能发布时，撤销本次新增的清单，避免遗留无主文件。
            if (manifestPublished && !manifestExisted) DeleteFile(manifestDestination);
            throw;
        }
        finally
        {
            foreach (string temporary in temporaryFiles)
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    /// <summary>复制到目录内的临时文件；临时文件以点开头且无可识别扩展名，不会进入列表或被引擎加载。</summary>
    private static async Task<string> CopyToTemporaryAsync(string sourcePath, string root, List<string> temporaryFiles)
    {
        string temporary = Path.Combine(root, $".{Guid.NewGuid():N}.installing");
        temporaryFiles.Add(temporary);
        await using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using FileStream target = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await source.CopyToAsync(target);
        await target.FlushAsync();
        return temporary;
    }

    /// <summary>删除重采样器及其同名清单；相对路径必须位于重采样器目录内。</summary>
    public static void Uninstall(string relativePath)
    {
        string path = ResolvePath(relativePath);
        DeleteFile(path);
        DeleteFile(Path.ChangeExtension(path, ".yaml"));
    }

    /// <summary>重新扫描引擎注册表。Initialize 自带锁，渲染线程不会读到半更新的映射。</summary>
    public static Task RescanAsync() => Task.Run(() => ToolsManager.Inst.Initialize());

    private static void DeleteFile(string path)
    {
        if (!File.Exists(path)) return;
        // 用户已确认删除；只读属性不应阻止删除。
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        File.Delete(path);
    }

    private static string ResolvePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        string root = Root;
        string path = Path.GetFullPath(Path.Combine(root, relativePath));
        string prefix = Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar;
        if (Path.IsPathRooted(relativePath) || !path.StartsWith(prefix, PathComparison) || !IsCandidate(path))
            throw new InvalidDataException("Invalid resampler path.");
        return path;
    }
}
