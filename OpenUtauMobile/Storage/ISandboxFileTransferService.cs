using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OpenUtauMobile.Storage
{
    /// <summary>在设备存储与应用工作区之间传输文件，不依赖外部文件的物理路径。</summary>
    public interface ISandboxFileTransferService
    {
        /// <summary>当前平台能否直接选择外部目录。</summary>
        bool CanImportFolder { get; }
        /// <summary>保存是否只能确认下载已发起，而不能确认设备端已保存。</summary>
        bool StartsDownload { get; }
        /// <summary>选择外部文件或目录，取消返回空批次。</summary>
        Task<SandboxImportBatch?> PickAsync(bool folder);
        /// <summary>将准备好的文件交给设备，返回是否完成保存或发起下载。</summary>
        Task<bool> SaveAsync(string sourcePath, string suggestedName, CancellationToken cancellationToken);
    }

    /// <summary>一次选择持有的外部文件和授权，在传输结束后统一释放。</summary>
    public abstract class SandboxImportBatch : IAsyncDisposable
    {
        /// <summary>按需枚举相对路径，目录先于其子项返回。</summary>
        public abstract IAsyncEnumerable<SandboxImportEntry> GetEntriesAsync(CancellationToken cancellationToken);
        /// <summary>释放本次选择持有的设备授权与文件对象。</summary>
        public abstract ValueTask DisposeAsync();
    }

    /// <summary>相对于本次选择的路径和可延迟打开的数据流，目录没有读取函数。</summary>
    public sealed record SandboxImportEntry(string RelativePath, bool IsDirectory, Func<Task<Stream>>? OpenReadAsync);

    /// <summary>导入冲突的处理方式，不覆盖已有数据。</summary>
    public enum SandboxConflictAction
    {
        KeepBoth,
        Skip,
        Cancel,
    }
}
