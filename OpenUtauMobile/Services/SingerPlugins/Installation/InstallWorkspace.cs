using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtauMobile.Plugin.Abstractions;

namespace OpenUtauMobile.Services.SingerPlugins.Installation
{
    /// <summary>会话路径边界，同时拒绝越界和已有符号链接。</summary>
    internal static class InstallPaths
    {
        internal static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>仅接受根目录下的相对路径，拒绝跨平台分隔符绕过。</summary>
        internal static string Resolve(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
                throw new IOException("无效的安装相对路径。");
            string normalized = relative.Replace('\\', '/');
            if (normalized.Split('/').Any(part => part is ".." or "." or ""))
                throw new IOException("安装路径不能包含空路径或上级目录。");
            string full = Path.GetFullPath(Path.Combine(root, normalized));
            RequireInside(root, full);
            RejectLinks(full);
            return full;
        }

        internal static void RequireInside(string root, string path)
        {
            string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(prefix, Comparison)) throw new IOException("安装路径超出允许范围。");
        }

        internal static void RejectLinks(string path)
        {
            for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"安装路径包含符号链接：{current}");
        }

        internal static void DeleteTemporary(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>宿主拥有的会话缓存，不删除任何正式发布内容。</summary>
    internal sealed class InstallWorkspace : IInstallWorkspace
    {
        public string RootPath { get; }
        internal InstallWorkspace(string cachePath, Guid sessionId)
        {
            RootPath = Path.GetFullPath(Path.Combine(cachePath, "SingerInstall", sessionId.ToString("N")));
            InstallPaths.RejectLinks(RootPath);
            Directory.CreateDirectory(RootPath);
        }
        public string GetPath(string relativePath) => InstallPaths.Resolve(RootPath, relativePath);
        public string CreateDirectory(string relativePath)
        {
            string path = GetPath(relativePath);
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>将所选包固定到会话缓存，之后分析、预览和提交共用同一来源。</summary>
    internal sealed class InstallSource(string originalPath, InstallWorkspace workspace) : IInstallSource
    {
        private string? localPath;
        private readonly SemaphoreSlim materialize = new(1, 1);
        public string DisplayName => Path.GetFileName(originalPath);
        public long? Length => new FileInfo(localPath ?? originalPath).Length;
        public async ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken) =>
            File.OpenRead(await GetLocalPathAsync(cancellationToken));
        public async ValueTask<string> GetLocalPathAsync(CancellationToken cancellationToken)
        {
            await materialize.WaitAsync(cancellationToken);
            try
            {
                if (localPath != null) return localPath;
                string path = Path.Combine(workspace.CreateDirectory("source"), DisplayName);
                bool created = false;
                try
                {
                    await using FileStream source = File.OpenRead(originalPath);
                    await using FileStream destination = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    created = true;
                    await source.CopyToAsync(destination, cancellationToken);
                    localPath = path;
                    return path;
                }
                catch
                {
                    if (created) File.Delete(path);
                    throw;
                }
            }
            finally { materialize.Release(); }
        }
    }
}
