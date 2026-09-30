using System;
using System.IO;
using OpenUtau.Core;

namespace OpenUtauMobile.Storage;

/// <summary>浏览器选择器只暴露持久化的应用目录。</summary>
internal static class BrowserWorkspace
{
    public static string Root => Path.GetFullPath(PathManager.Inst.RootPath);

    public static string NormalizeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Root;
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (fullPath == Root || fullPath.StartsWith(Root + "/", StringComparison.Ordinal))
                return fullPath;
        }
        catch (ArgumentException) { }
        catch (NotSupportedException) { }
        return Root;
    }
}
