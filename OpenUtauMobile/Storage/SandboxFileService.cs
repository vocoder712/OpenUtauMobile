using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtauMobile.Helpers;
using OpenUtauMobile.Services;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace OpenUtauMobile.Storage
{
    /// <summary>限制在应用工作区内的文件管理，导入不覆盖已有内容。</summary>
    public sealed class SandboxFileService
    {
        private const int MaxEntries = 50000;
        private const int MaxDepth = 64;
        private const string TemporaryPrefix = ".opum-transfer-";
        private readonly StringComparison _comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        private readonly Func<bool> _hasActiveEditor;

        /// <summary>当前宿主已注册的应用工作区根目录。</summary>
        public string Root { get; }

        /// <summary>建立工作区边界，并查询工程是否仍占用资源。</summary>
        public SandboxFileService(Func<bool> hasActiveEditor)
        {
            Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(PathManager.Inst.RootPath));
            _hasActiveEditor = hasActiveEditor;
        }

        /// <summary>校验完整路径及其每层父目录，拒绝工作区外路径和链接。</summary>
        public string Resolve(string path)
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (!IsWithin(full, Root)) throw new IOException(L.S("Sandbox.OutsideWorkspace"));
            CheckLinks(full, Root);
            return full;
        }

        /// <summary>枚举一层目录，保持文件夹优先和本地化名称排序。</summary>
        public IReadOnlyList<FileSystemInfo> List(string path)
        {
            DirectoryInfo directory = new(Resolve(path));
            return directory.EnumerateFileSystemInfos()
                .Where(item => !item.Name.StartsWith(TemporaryPrefix, StringComparison.Ordinal))
                .OrderBy(item => item is DirectoryInfo ? 0 : 1)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        }

        /// <summary>单项名称校验同时适用于新建和重命名。</summary>
        public static string? ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name is "." or ".." ||
                name.Length > 255 || name.EndsWith('.') || name.StartsWith(TemporaryPrefix, StringComparison.Ordinal) ||
                name.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)) ||
                Path.GetInvalidFileNameChars().Any(name.Contains))
                return L.S("Sandbox.InvalidName");
            string stem = name.Split('.')[0];
            if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '0' and <= '9'))
                return L.S("Sandbox.InvalidName");
            return null;
        }

        /// <summary>返回不可破坏性修改的原因，保护检查也覆盖将被递归处理的子树。</summary>
        public string? GetMutationRestriction(string path, bool? hasActiveEditor = null)
        {
            path = Resolve(path);
            if (hasActiveEditor ?? _hasActiveEditor()) return L.S("Sandbox.CloseEditor");
            PathManager manager = PathManager.Inst;
            string[] fixedPaths = [Root, manager.DataPath, manager.CachePath, manager.SingersPath,
                manager.DependencyPath, manager.ResamplersPath, manager.WavtoolsPath, manager.DictionariesPath,
                manager.TemplatesPath, manager.ThemesPath, manager.BackupsPath, Path.Combine(Root, "Projects")];
            if (fixedPaths.Any(fixedPath => IsWithin(Path.GetFullPath(fixedPath), path)))
                return L.S("Sandbox.Protected");
            return GetWriteRestriction(path);
        }

        private string? GetWriteRestriction(string path)
        {
            PathManager manager = PathManager.Inst;
            string[] protectedTrees = [manager.PluginsPath, manager.LogsPath];
            if (protectedTrees.Any(tree => IsWithin(path, Path.GetFullPath(tree)) || IsWithin(Path.GetFullPath(tree), path)))
                return L.S("Sandbox.Protected");
            string[] protectedFiles = [manager.PrefsFilePath, manager.NotePresetsFilePath];
            return protectedFiles.Any(file => IsWithin(Path.GetFullPath(file), path)) ? L.S("Sandbox.Protected") : null;
        }

        /// <summary>创建单级文件夹，不将同名目录当作创建成功。</summary>
        public string CreateDirectory(string parent, string name)
        {
            string destination = GetNewPath(parent, name);
            EnsureWritable(destination);
            if (Exists(destination)) throw new IOException(L.S("Sandbox.AlreadyExists"));
            Directory.CreateDirectory(destination);
            return destination;
        }

        /// <summary>在原父目录内重命名，禁止覆盖和操作活跃资源。</summary>
        public string Rename(string path, string name)
        {
            path = Resolve(path);
            ThrowIfRestricted(GetMutationRestriction(path));
            string destination = GetNewPath(Path.GetDirectoryName(path)!, name);
            if (path == destination) return path;
            EnsureWritable(destination);
            // 枚举实际目录项，允许大小写不敏感文件系统中的纯大小写改名。
            if (Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!)
                .Any(entry => !string.Equals(entry, path, StringComparison.Ordinal) &&
                    string.Equals(Path.GetFileName(entry), name, _comparison)))
                throw new IOException(L.S("Sandbox.AlreadyExists"));
            if (Directory.Exists(path)) Directory.Move(path, destination);
            else File.Move(path, destination);
            return destination;
        }

        /// <summary>删除前检查整棵目录树，避免递归操作进入链接。</summary>
        public async Task DeleteAsync(string path, CancellationToken cancellationToken)
        {
            path = Resolve(path);
            ThrowIfRestricted(GetMutationRestriction(path));
            ValidateTree(path, 0);
            await DeleteTreeAsync(path, cancellationToken);
        }

        private async Task DeleteTreeAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Resolve(path);
            if (Directory.Exists(path))
            {
                foreach (string child in Directory.GetFileSystemEntries(path))
                    await DeleteTreeAsync(child, cancellationToken);
                Directory.Delete(path);
            }
            else File.Delete(path);
            await Task.Yield();
        }

        /// <summary>先在传输暂存区接收完整条目，再按根对象解决冲突并提交。</summary>
        public async Task<int> ImportAsync(SandboxImportBatch batch, string parent, bool unzip,
            Func<string, Task<SandboxConflictAction>> resolveConflict, IProgress<string> progress,
            CancellationToken cancellationToken)
        {
            parent = Resolve(parent);
            ThrowIfRestricted(GetWriteRestrictionForDirectory(parent));
            // 与目标位于同一文件系统，提交只需改名，不复制完整文件。
            using SandboxTransferTemporary temporary = new(parent);
            string staging = Path.Combine(temporary.DirectoryPath, "items");
            Directory.CreateDirectory(staging);
            TransferBudget budget = new();
            await foreach (SandboxImportEntry entry in batch.GetEntriesAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                budget.AddEntry();
                string relative = ValidateRelativePath(entry.RelativePath);
                if (unzip && (entry.IsDirectory || !relative.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                    throw new IOException(L.S("Sandbox.ZipFilesOnly"));
                string destination = SafeChild(staging, relative);
                string workspaceDestination = Resolve(Path.Combine(parent, relative));
                EnsureWritable(workspaceDestination);
                progress.Report(entry.RelativePath);
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }
                await using Stream source = await entry.OpenReadAsync!();
                if (unzip)
                {
                    string archiveRoot = Path.Combine(staging, Path.GetFileNameWithoutExtension(relative));
                    ThrowIfRestricted(ValidateName(Path.GetFileName(archiveRoot)));
                    if (Exists(archiveRoot)) throw new IOException(L.S("Sandbox.AlreadyExists"));
                    Directory.CreateDirectory(archiveRoot);
                    if (source.CanSeek)
                        await ExtractAsync(source, archiveRoot, budget, progress, cancellationToken);
                    else
                    {
                        // 云文件提供方可能返回不可寻址流，按平台策略暂存后再读取 ZIP 索引。
                        await using SandboxTransferFile archiveFile = await ServiceHub.CreateSandboxTransferFileAsync();
                        await source.CopyToAsync(archiveFile.WriteStream, 128 * 1024, cancellationToken);
                        await archiveFile.CompleteAsync(cancellationToken);
                        await using Stream archiveInput = await archiveFile.OpenReadAsync(cancellationToken);
                        await ExtractAsync(archiveInput, archiveRoot, budget, progress, cancellationToken);
                    }
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using (FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write))
                        await CopyBoundedAsync(source, output, budget, cancellationToken);
                    await budget.CheckpointAsync(cancellationToken);
                }
            }

            int completed = 0;
            foreach (string source in Directory.GetFileSystemEntries(staging))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string destination = Resolve(Path.Combine(parent, Path.GetFileName(source)));
                if (Exists(destination))
                {
                    SandboxConflictAction action = await resolveConflict(Path.GetFileName(source));
                    if (action == SandboxConflictAction.Cancel) throw new OperationCanceledException(cancellationToken);
                    if (action == SandboxConflictAction.Skip) continue;
                    destination = GetUniquePath(parent, Path.GetFileName(source), Directory.Exists(source));
                }
                EnsureWritable(destination);
                if (Directory.Exists(source)) Directory.Move(source, destination);
                else File.Move(source, destination);
                completed++;
            }
            return completed;
        }

        /// <summary>制作稳定的导出快照，文件夹和多项导出为保留空目录的 ZIP。</summary>
        public async Task<SandboxPreparedExport> PrepareExportAsync(IReadOnlyList<string> paths,
            IProgress<string> progress, CancellationToken cancellationToken)
        {
            if (paths.Count == 0) throw new ArgumentException(nameof(paths));
            string[] sources = paths.Select(Resolve).ToArray();
            SandboxTransferFile transferFile = await ServiceHub.CreateSandboxTransferFileAsync();
            SandboxPreparedExport result = new(sources.Length == 1 && File.Exists(sources[0])
                ? Path.GetFileName(sources[0]) : (sources.Length == 1 ? Path.GetFileName(sources[0]) : "OpenUtauMobile-files") + ".zip", transferFile);
            try
            {
                TransferBudget budget = new();
                if (sources.Length == 1 && File.Exists(sources[0]))
                {
                    progress.Report(Path.GetFileName(sources[0]));
                    await using FileStream source = OpenSharedRead(sources[0]);
                    await CopyBoundedAsync(source, transferFile.WriteStream, budget, cancellationToken);
                }
                else
                {
                    BufferedArchiveWriteStream zipOutput = new(transferFile.WriteStream);
                    await using (ZipArchive archive = await ZipArchive.CreateAsync(zipOutput,
                        ZipArchiveMode.Create, true, null, cancellationToken))
                    {
                        foreach (string source in sources)
                            await AddToArchiveAsync(archive, zipOutput, source, Path.GetFileName(source), budget, progress, cancellationToken);
                    }
                    await zipOutput.FlushAsync(cancellationToken);
                }
                await transferFile.CompleteAsync(cancellationToken);
                return result;
            }
            catch
            {
                await result.DisposeAsync();
                throw;
            }
        }

        private async Task ExtractAsync(Stream input, string target, TransferBudget budget,
            IProgress<string> progress, CancellationToken cancellationToken)
        {
            // 使用现有 SharpCompress 的异步索引枚举，避免 .NET Entries 属性同步读取浏览器文件。
            await using IAsyncArchive archive = await SharpCompress.Archives.Zip.ZipArchive.OpenAsyncArchive(input,
                new ReaderOptions { LeaveStreamOpen = true }, cancellationToken);
            await foreach (IArchiveEntry entry in archive.EntriesAsync.WithCancellation(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                budget.AddEntry();
                string relative = ValidateRelativePath((entry.Key ?? "").TrimEnd('/'));
                // ZIP 中的 Unix 符号链接不能作为普通文件或目录导入。
                if ((((entry.Attrib ?? 0) >> 16) & 0xF000) == 0xA000)
                    throw new IOException(L.S("Sandbox.LinkNotAllowed"));
                string destination = SafeChild(target, relative);
                progress.Report(relative);
                if (entry.IsDirectory) Directory.CreateDirectory(destination);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await using Stream source = await entry.OpenEntryStreamAsync(cancellationToken);
                    await using (FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write))
                        await CopyBoundedAsync(source, output, budget, cancellationToken);
                    await budget.CheckpointAsync(cancellationToken);
                }
            }
        }

        private async Task AddToArchiveAsync(ZipArchive archive, Stream zipOutput, string source, string relative, TransferBudget budget,
            IProgress<string> progress, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Resolve(source);
            budget.AddEntry();
            if (relative.Split('/').Length > MaxDepth) throw new IOException(L.S("Sandbox.ImportLimit"));
            progress.Report(relative);
            if (Directory.Exists(source))
            {
                // 空目录也显式异步结束条目，避免创建下一项时触发 ZIP 的同步补写。
                await using (Stream directoryEntry = await archive.CreateEntry(relative + "/").OpenAsync(cancellationToken)) { }
                await zipOutput.FlushAsync(cancellationToken);
                foreach (string child in Directory.GetFileSystemEntries(source))
                {
                    if (Path.GetFileName(child).StartsWith(TemporaryPrefix, StringComparison.Ordinal)) continue;
                    await AddToArchiveAsync(archive, zipOutput, child, relative + "/" + Path.GetFileName(child), budget, progress, cancellationToken);
                }
            }
            else
            {
                ZipArchiveEntry entry = archive.CreateEntry(relative, CompressionLevel.Fastest);
                await using FileStream input = OpenSharedRead(source);
                await using (Stream output = await entry.OpenAsync(cancellationToken))
                    await CopyBoundedAsync(input, output, budget, cancellationToken);
                await zipOutput.FlushAsync(cancellationToken);
            }
            await Task.Yield();
        }

        /// <summary>固定缓冲分块复制，累计检查点流量但不限制传输总大小。</summary>
        private static async Task CopyBoundedAsync(Stream source, Stream destination, TransferBudget budget,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[128 * 1024];
            int count;
            while ((count = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                budget.AddBytes(count);
                await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                // Browser 的虚拟文件同步流也必须定期让出 UI 线程。
                await Task.Yield();
            }
        }

        private string GetNewPath(string parent, string name)
        {
            ThrowIfRestricted(ValidateName(name));
            return Resolve(Path.Combine(Resolve(parent), name));
        }

        private string GetUniquePath(string parent, string name, bool directory)
        {
            string extension = directory ? "" : Path.GetExtension(name);
            string stem = directory ? name : Path.GetFileNameWithoutExtension(name);
            for (int suffix = 2; suffix < 100000; suffix++)
            {
                string candidateName = $"{stem} ({suffix}){extension}";
                ThrowIfRestricted(ValidateName(candidateName));
                string candidate = Resolve(Path.Combine(parent, candidateName));
                if (!Exists(candidate)) return candidate;
            }
            throw new IOException(L.S("Sandbox.AlreadyExists"));
        }

        private string? GetWriteRestrictionForDirectory(string directory)
        {
            PathManager manager = PathManager.Inst;
            return IsWithin(directory, Path.GetFullPath(manager.PluginsPath)) ||
                IsWithin(directory, Path.GetFullPath(manager.LogsPath)) ? L.S("Sandbox.Protected") : null;
        }

        private void EnsureWritable(string path) => ThrowIfRestricted(GetWriteRestriction(path));

        private static void ThrowIfRestricted(string? reason)
        {
            if (reason != null) throw new IOException(reason);
        }

        private bool IsWithin(string path, string root) => string.Equals(path, root, _comparison) ||
            path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, _comparison);

        /// <summary>逐层检查链接，包括目标不存在的符号链接。</summary>
        private void CheckLinks(string path, string root)
        {
            string? current = path;
            while (current != null)
            {
                try
                {
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException(L.S("Sandbox.LinkNotAllowed"));
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                if (string.Equals(current, root, _comparison)) break;
                current = Path.GetDirectoryName(current);
            }
        }

        private void ValidateTree(string path, int depth)
        {
            Resolve(path);
            if (depth > MaxDepth) throw new IOException(L.S("Sandbox.ImportLimit"));
            if (Directory.Exists(path))
                foreach (string child in Directory.GetFileSystemEntries(path)) ValidateTree(child, depth + 1);
        }

        /// <summary>按路径段校验外部名称，拒绝目录穿越和平台特殊名称。</summary>
        private static string ValidateRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Contains('\\') || Path.IsPathRooted(path))
                throw new IOException(L.S("Sandbox.OutsideWorkspace"));
            string[] segments = path.Split('/');
            if (segments.Length > MaxDepth) throw new IOException(L.S("Sandbox.ImportLimit"));
            foreach (string segment in segments) ThrowIfRestricted(ValidateName(segment));
            return Path.Combine(segments);
        }

        private static string SafeChild(string parent, string relative)
        {
            string full = Path.GetFullPath(Path.Combine(parent, relative));
            if (!full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new IOException(L.S("Sandbox.OutsideWorkspace"));
            return full;
        }

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
        private static FileStream OpenSharedRead(string path) => new(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        internal static void DeleteTemporary(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>.NET ZIP 条目关闭时仍可能同步补写尾部，以固定缓冲接收并在条目结束后异步排出。</summary>
        private sealed class BufferedArchiveWriteStream(Stream destination) : Stream
        {
            private readonly byte[] _pending = new byte[128 * 1024];
            private int _buffered;
            private long _length;
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => _length;
            public override long Position { get => _length; set => throw new NotSupportedException(); }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Flush() => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                // 这里只接收条目收尾；正文始终走异步写入，缓冲不会随文件大小增长。
                if (buffer.Length > _pending.Length - _buffered) throw new IOException("ZIP metadata buffer exceeded.");
                buffer.CopyTo(_pending.AsSpan(_buffered));
                _buffered += buffer.Length;
                _length += buffer.Length;
            }
            public override async Task FlushAsync(CancellationToken cancellationToken)
            {
                await DrainAsync(cancellationToken);
                await destination.FlushAsync(cancellationToken);
            }
            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                await DrainAsync(cancellationToken);
                await destination.WriteAsync(buffer, cancellationToken);
                _length += buffer.Length;
            }
            private async Task DrainAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_buffered == 0) return;
                await destination.WriteAsync(_pending.AsMemory(0, _buffered), cancellationToken);
                _buffered = 0;
            }
        }

        /// <summary>仅限制条目数量，并在关闭输出文件后按流量或时间保存检查点。</summary>
        private sealed class TransferBudget
        {
            private long _bytes;
            private int _entries;
            private readonly Stopwatch _sinceFlush = Stopwatch.StartNew();
            public void AddEntry()
            {
                if (++_entries > MaxEntries) throw new IOException(L.S("Sandbox.ImportLimit"));
            }
            public void AddBytes(int bytes)
            {
                _bytes += bytes;
            }
            public async Task CheckpointAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_bytes < 32L * 1024 * 1024 && _sinceFlush.Elapsed < TimeSpan.FromSeconds(2)) return;
                await ServiceHub.FlushFileSystemAsync();
                _bytes = 0;
                _sinceFlush.Restart();
            }
        }
    }

    /// <summary>一次文件传输使用的临时目录，不保存历史版本或回滚副本。</summary>
    public class SandboxTransferTemporary : IDisposable
    {
        /// <summary>本次操作独占的暂存目录。</summary>
        public string DirectoryPath { get; }
        /// <summary>在指定父目录或系统临时区建立本次传输专用目录。</summary>
        public SandboxTransferTemporary(string? parent = null)
        {
            DirectoryPath = Path.Combine(parent ?? Path.GetTempPath(), ".opum-transfer-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
        }
        /// <summary>释放尚未交给设备的本次传输数据。</summary>
        public void Dispose() => SandboxFileService.DeleteTemporary(DirectoryPath);
    }

    /// <summary>等待用户保存到设备的稳定导出快照。</summary>
    public sealed class SandboxPreparedExport(string name, SandboxTransferFile file) : IDisposable, IAsyncDisposable
    {
        /// <summary>保存到设备时建议使用的文件名。</summary>
        public string Name { get; } = name;
        /// <summary>交给平台注册导出服务的暂存标识。</summary>
        public string FilePath => file.ExportPath;
        /// <summary>关闭页面时释放导出快照。</summary>
        public void Dispose() => file.Dispose();
        /// <summary>等待导出暂存清理完成。</summary>
        public ValueTask DisposeAsync() => file.DisposeAsync();
    }
}