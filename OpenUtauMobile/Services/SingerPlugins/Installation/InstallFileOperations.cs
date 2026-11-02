using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OpenUtauMobile.Plugin.Abstractions;
using Serilog;

namespace OpenUtauMobile.Services.SingerPlugins.Installation
{
    /// <summary>发布已校验文件，记录提交回执并清理本会话独占的临时目录。</summary>
    internal sealed class InstallFileOperations(InstallWorkspace workspace, ImmutableArray<InstallTarget> targets,
        Guid sessionId) : IInstallFileOperations
    {
        private readonly List<string> staging = [];
        private readonly List<PublishedLocation> published = [];
        internal ImmutableArray<PublishedLocation> Published => published.ToImmutableArray();
        internal bool CanPublish { get; set; }
        internal ImmutableArray<string> PlannedPaths { get; set; } = [];

        public string ResolveEntryPath(string workspaceDirectory, string entryPath)
        {
            string directory = workspace.GetPath(workspaceDirectory);
            return InstallPaths.Resolve(directory, entryPath.TrimEnd('/', '\\'));
        }

        public async ValueTask<PublishedLocation> PublishAsync(PublishRequest request,
            IProgress<InstallProgress> progress, CancellationToken cancellationToken)
        {
            if (!CanPublish) throw new InvalidOperationException("当前阶段不允许发布文件。");
            InstallTarget target = targets.FirstOrDefault(item => item.Id == request.TargetId)
                ?? throw new IOException("安装目标不可用。");
            string source = workspace.GetPath(request.SourceRelativePath);
            string destination = InstallPaths.Resolve(target.Path, request.DestinationRelativePath);
            RequirePlanned(destination);
            RequireAbsent(destination);
            bool directory = request.Kind == InstallArtifactKind.Directory;
            if (directory ? !Directory.Exists(source) : !File.Exists(source)) throw new IOException("已校验的安装源不存在。");
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            progress.Report(new(InstallPhase.Publishing, request.DestinationRelativePath));
            try
            {
                Move(source, destination, directory);
            }
            catch (IOException) when (!File.Exists(destination) && !Directory.Exists(destination) &&
                (directory ? Directory.Exists(source) : File.Exists(source)))
            {
                // 不同挂载点的 Move 可能失败；将同一份内容复制到目标卷后校验再移动。
                string temporary = InstallPaths.Resolve(target.Path, $".singer-install-{sessionId:N}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(temporary);
                staging.Add(temporary);
                string copy = Path.Combine(temporary, "payload");
                await CopyCheckedAsync(source, copy, directory, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                RequireAbsent(destination);
                Move(copy, destination, directory);
            }
            PublishedLocation location = new(destination, request.Kind);
            // Move 已完成后即记录回执，后续取消不能将它回退成未安装。
            published.Add(location);
            return location;
        }

        private static void Move(string source, string destination, bool directory)
        {
            if (directory) Directory.Move(source, destination);
            else File.Move(source, destination, false);
        }

        internal static void RequireAbsent(string path)
        {
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException($"安装目标已存在：{path}");
        }

        private static async Task CopyCheckedAsync(string source, string destination, bool directory, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (directory)
            {
                Directory.CreateDirectory(destination);
                foreach (string child in Directory.EnumerateFileSystemEntries(source))
                    await CopyCheckedAsync(child, Path.Combine(destination, Path.GetFileName(child)), Directory.Exists(child), token);
                return;
            }
            await using FileStream input = File.OpenRead(source);
            await using (FileStream output = new(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output, token);
            input.Position = 0;
            byte[] originalHash = await SHA256.HashDataAsync(input, token);
            await using FileStream copied = File.OpenRead(destination);
            byte[] copiedHash = await SHA256.HashDataAsync(copied, token);
            if (!originalHash.AsSpan().SequenceEqual(copiedHash)) throw new IOException("跨卷复制校验失败。");
        }

        public void RegisterTemporaryPath(string path)
        {
            InstallPaths.RequireInside(workspace.RootPath, path);
            // Workspace 统一清理，无需再保存可与正式目录混淆的额外列表。
        }

        public void RecordPublishedLocation(PublishedLocation location)
        {
            if (!CanPublish) throw new InvalidOperationException("当前阶段不允许登记发布结果。");
            string path = Path.GetFullPath(location.Path);
            RequirePlanned(path);
            if (!targets.Any(target => path.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.Path)) + Path.DirectorySeparatorChar,
                InstallPaths.Comparison))) throw new IOException("发布位置不属于配置的安装目标。");
            if (location.Kind == InstallArtifactKind.Directory ? !Directory.Exists(path) : !File.Exists(path))
                throw new IOException("正式发布内容不存在。");
            if (published.All(item => !string.Equals(item.Path, path, InstallPaths.Comparison))) published.Add(location with { Path = path });
        }

        private void RequirePlanned(string path)
        {
            if (!PlannedPaths.Any(item => string.Equals(Path.GetFullPath(item), path, InstallPaths.Comparison)))
                throw new IOException("发布位置不在当前安装计划中。");
        }

        /// <summary>仅清理宿主独占的缓存和目标卷暂存目录。</summary>
        internal void Cleanup()
        {
            foreach (string path in staging.Append(workspace.RootPath))
                try
                {
                    InstallPaths.DeleteTemporary(path);
                }
                catch (Exception exception) { Log.Warning(exception, "Failed to clean singer installation workspace {Path}", path); }
        }
    }
}
