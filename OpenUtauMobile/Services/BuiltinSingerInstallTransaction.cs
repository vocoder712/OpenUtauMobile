using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services.SingerPlugins;

namespace OpenUtauMobile.Services
{
    /// <summary>内置安装的临时解包和可撤销发布；不改变上游归档布局与配置生成规则。</summary>
    internal sealed class BuiltinSingerInstallTransaction
    {
        private readonly string id = Guid.NewGuid().ToString("N");
        private readonly string workspace;
        private readonly string payload;
        private readonly string targetRoot;
        private readonly List<FileChange> changes = [];
        private readonly List<string> createdDirectories = [];
        private readonly List<string> temporaryFiles = [];
        private readonly IProgress<(double Percent, string Message)> progress;
        private bool preserveWorkspace;

        /// <summary>仅记录本会话已经替换的文件，原件只用于安装取消或失败时恢复。</summary>
        private sealed record FileChange(string Path, string? Original, FileAttributes Attributes, DateTime LastWriteTime)
        {
            public bool Applied { get; set; }
        }

        internal BuiltinSingerInstallTransaction(IProgress<(double Percent, string Message)> progress)
        {
            this.progress = progress;
            workspace = Path.GetFullPath(Path.Combine(PathManager.Inst.CachePath, "BuiltinSingerInstall", id));
            payload = Path.Combine(workspace, "payload");
            targetRoot = Path.GetFullPath(PathManager.Inst.SingersInstallPath);
        }

        /// <summary>解包期间不写歌手目录；发布与回滚均阻止并发扫描看到中间结果。</summary>
        internal async Task RunAsync(BuiltinSingerInstaller installer, string source, string singerType,
            Encoding archiveEncoding, Encoding textEncoding, CancellationToken token)
        {
            try
            {
                Directory.CreateDirectory(payload);
                if (installer.IsArchive)
                {
                    await Task.Run(() =>
                    {
                        bool first = true;
                        new VoicebankInstaller(payload, (percent, message) =>
                        {
                            token.ThrowIfCancellationRequested();
                            if (!first) Resolve(payload, message.Replace('\\', '/'));
                            first = false;
                            progress.Report((percent * 0.7, message));
                        }, archiveEncoding, textEncoding).Install(source, singerType);
                        token.ThrowIfCancellationRequested();
                    }, token);
                }
                else if (installer.Id == "builtin:vogen")
                {
                    await CopyAsync(source, Path.Combine(payload, Path.GetFileName(source)), token);
                }
                else
                {
                    throw new InvalidOperationException("未知的内置安装器。");
                }
                token.ThrowIfCancellationRequested();
                await SingerDiscoveryService.Instance.WithCatalogLockAsync(async () =>
                {
                    try
                    {
                        await PublishAsync(installer.IsArchive, token);
                        token.ThrowIfCancellationRequested();
                        await ServiceHub.FlushFileSystemAsync();
                        token.ThrowIfCancellationRequested();
                    }
                    catch (Exception failure)
                    {
                        try
                        {
                            await RollbackAsync();
                        }
                        catch (Exception rollbackFailure)
                        {
                            // 保留仍可能含原件的会话，避免回滚失败后销毁恢复资料。
                            preserveWorkspace = true;
                            throw new AggregateException($"{L.S("SingerSetup.RollbackFailed")}\n{workspace}", failure, rollbackFailure);
                        }
                        throw;
                    }
                    return true;
                }, token);
            }
            finally
            {
                foreach (string path in temporaryFiles)
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                string cacheRoot = Path.GetFullPath(Path.Combine(PathManager.Inst.CachePath, "BuiltinSingerInstall"));
                if (!preserveWorkspace && Directory.Exists(workspace))
                {
                    if (!string.Equals(Resolve(cacheRoot, id), workspace, PathComparison)) throw new IOException("临时目录超出会话范围。");
                    List<string> cachedFiles = [];
                    List<string> cachedDirectories = [];
                    Collect(workspace, cachedFiles, cachedDirectories, CancellationToken.None);
                    foreach (string file in cachedFiles) File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                    Directory.Delete(workspace, true);
                }
            }
        }

        /// <summary>逐项合并到原目录，保留上游覆盖已有文件而不删除无关文件的行为。</summary>
        private async Task PublishAsync(bool overwrite, CancellationToken token)
        {
            List<string> files = [];
            List<string> directories = [];
            Collect(payload, files, directories, token);
            foreach (string directory in directories)
            {
                token.ThrowIfCancellationRequested();
                EnsureDirectory(Resolve(targetRoot, Path.GetRelativePath(payload, directory)));
            }
            for (int index = 0; index < files.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                string source = files[index];
                string relative = Path.GetRelativePath(payload, source);
                string destination = Resolve(targetRoot, relative);
                EnsureDirectory(Path.GetDirectoryName(destination)!);
                if (Directory.Exists(destination) || !overwrite && File.Exists(destination))
                    throw new IOException($"{L.S("SingerSetup.TargetExists")} {destination}");
                string? original = null;
                FileAttributes attributes = FileAttributes.Normal;
                DateTime lastWriteTime = default;
                if (File.Exists(destination))
                {
                    original = Path.Combine(workspace, "originals", index.ToString());
                    Directory.CreateDirectory(Path.GetDirectoryName(original)!);
                    attributes = File.GetAttributes(destination);
                    lastWriteTime = File.GetLastWriteTimeUtc(destination);
                    await CopyAsync(destination, original, token);
                }
                FileChange change = new(destination, original, attributes, lastWriteTime);
                changes.Add(change);
                string temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".opum-install-{id}-{index}.tmp");
                temporaryFiles.Add(temporary);
                await CopyAsync(source, temporary, token);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, destination, overwrite);
                change.Applied = true;
                File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
                File.SetAttributes(destination, File.GetAttributes(source));
                progress.Report((70 + ((index + 1) * 30d / Math.Max(1, files.Count)), relative));
            }
        }

        /// <summary>回滚不使用已经取消的令牌，并继续尝试恢复其余文件；全部恢复后才能报告已取消。</summary>
        private async Task RollbackAsync()
        {
            progress.Report((-1, L.S("SingerSetup.RollingBack")));
            List<Exception> failures = [];
            foreach (FileChange change in changes.AsEnumerable().Reverse())
            {
                if (!change.Applied) continue;
                try
                {
                    if (File.Exists(change.Path)) File.SetAttributes(change.Path, File.GetAttributes(change.Path) & ~FileAttributes.ReadOnly);
                    if (change.Original == null)
                    {
                        File.Delete(change.Path);
                    }
                    else
                    {
                        string temporary = Path.Combine(Path.GetDirectoryName(change.Path)!, $".opum-restore-{id}.tmp");
                        temporaryFiles.Add(temporary);
                        await CopyAsync(change.Original, temporary, CancellationToken.None);
                        File.Move(temporary, change.Path, true);
                        File.SetLastWriteTimeUtc(change.Path, change.LastWriteTime);
                        File.SetAttributes(change.Path, change.Attributes);
                    }
                }
                catch (Exception exception) { failures.Add(exception); }
            }
            // 临时发布文件也必须先移除，之后才能移除本会话创建的空目录。
            foreach (string temporary in temporaryFiles)
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception exception) { failures.Add(exception); }
            }
            foreach (string directory in createdDirectories.AsEnumerable().Reverse())
            {
                try
                {
                    if (Directory.Exists(directory)) Directory.Delete(directory, false);
                }
                catch (Exception exception) { failures.Add(exception); }
            }
            if (failures.Count > 0) throw new AggregateException(failures);
            await ServiceHub.FlushFileSystemAsync();
        }

        /// <summary>按父子顺序登记新目录，回滚仅删除本会话创建的空目录。</summary>
        private void EnsureDirectory(string path)
        {
            if (Directory.Exists(path)) return;
            if (!string.Equals(path, targetRoot, PathComparison)) EnsureDirectory(Path.GetDirectoryName(path)!);
            Directory.CreateDirectory(path);
            createdDirectories.Add(path);
        }

        /// <summary>递归收集会话文件和目录，支持取消。</summary>
        private static void Collect(string root, List<string> files, List<string> directories, CancellationToken token)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(root))
            {
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(path))
                {
                    directories.Add(path);
                    Collect(path, files, directories, token);
                }
                else files.Add(path);
            }
        }

        /// <summary>限定所有发布和归档条目在对应根目录内。</summary>
        private static string Resolve(string root, string relative)
        {
            if (Path.IsPathRooted(relative) || relative.Contains(':')) throw new IOException("安装路径超出允许范围。");
            string path = Path.GetFullPath(Path.Combine(root, relative));
            string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(prefix, PathComparison)) throw new IOException("安装路径超出允许范围。");
            return path;
        }

        private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>异步复制支持在大文件中途取消，失败的临时文件由会话收尾清理。</summary>
        private static async Task CopyAsync(string source, string destination, CancellationToken token)
        {
            await using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            await using FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, token);
        }
    }
}
