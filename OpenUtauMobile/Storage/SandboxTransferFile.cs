using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OpenUtauMobile.Storage
{
    /// <summary>传输暂存文件；原生端使用磁盘文件，浏览器可按需溢写持久存储。</summary>
    public abstract class SandboxTransferFile : IDisposable, IAsyncDisposable
    {
        /// <summary>仅交给平台导出服务的标识，不要求是本地物理路径。</summary>
        public abstract string ExportPath { get; }
        /// <summary>顺序写入暂存文件，调用完成方法后不再写入。</summary>
        public abstract Stream WriteStream { get; }
        /// <summary>完成写入并准备可读取、可导出的稳定快照。</summary>
        public abstract Task CompleteAsync(CancellationToken cancellationToken);
        /// <summary>打开可寻址读取流，供异步 ZIP 读取使用。</summary>
        public abstract Task<Stream> OpenReadAsync(CancellationToken cancellationToken);
        /// <summary>释放暂存文件及其写入资源。</summary>
        public abstract void Dispose();
        /// <summary>异步释放磁盘或浏览器持久化暂存。</summary>
        public abstract ValueTask DisposeAsync();

        /// <summary>原生平台的默认暂存直接写入系统临时目录。</summary>
        public static Task<SandboxTransferFile> CreateLocalAsync() => Task.FromResult<SandboxTransferFile>(new LocalTransferFile());

        private sealed class LocalTransferFile : SandboxTransferFile
        {
            private readonly SandboxTransferTemporary _directory = new();
            private readonly FileStream _stream;
            private bool _completed;
            private bool _disposed;
            public override string ExportPath => Path.Combine(_directory.DirectoryPath, "payload");
            public override Stream WriteStream => _stream;

            public LocalTransferFile() => _stream = new FileStream(ExportPath, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous);

            public override async Task CompleteAsync(CancellationToken cancellationToken)
            {
                if (_completed) return;
                await _stream.FlushAsync(cancellationToken);
                await _stream.DisposeAsync();
                _completed = true;
            }

            public override Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_completed) throw new InvalidOperationException();
                return Task.FromResult<Stream>(new FileStream(ExportPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 128 * 1024, FileOptions.Asynchronous));
            }

            public override void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _stream.Dispose();
                _directory.Dispose();
            }

            public override async ValueTask DisposeAsync()
            {
                if (_disposed) return;
                _disposed = true;
                await _stream.DisposeAsync();
                _directory.Dispose();
            }
        }
    }
}