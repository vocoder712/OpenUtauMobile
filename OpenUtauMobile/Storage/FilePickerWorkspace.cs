using System;
using System.IO;
using OpenUtau.Core;

namespace OpenUtauMobile.Storage;

/// <summary>沙箱平台的内置选择器只暴露应用工作区目录。</summary>
internal static class FilePickerWorkspace
{
    /// <summary>浏览器与 iOS 的普通文件操作均限定在应用工作区内。</summary>
    public static bool IsSandboxed => OperatingSystem.IsBrowser() || OperatingSystem.IsIOS();

    /// <summary>与沙箱文件管理器共用的工作区根目录。</summary>
    public static string Root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(PathManager.Inst.RootPath));

    /// <summary>规范化浏览目录，越界、链接或无效路径回到工作区根目录。</summary>
    public static string NormalizeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Root;
        try
        {
            SandboxFileService files = new(() => false);
            return files.Resolve(path);
        }
        catch (ArgumentException) { }
        catch (NotSupportedException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return Root;
    }
}
