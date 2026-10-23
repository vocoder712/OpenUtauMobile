using System;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;
using System.Threading.Tasks;
using OpenUtauMobile.Storage;

namespace OpenUtauMobile.Browser.Services
{
    /// <summary>小文件保留有限缓冲，大文件由浏览器按块溢写 IndexedDB。</summary>
    internal sealed partial class BrowserSandboxTransferFile : SandboxTransferFile
    {
        private const string ModuleName = "OpenUtauMobileFileTransfer";
        private readonly int _id = CreateTemporary();
        private readonly BufferedWriteStream _stream;
        private bool _completed;
        private bool _disposed;
        public override string ExportPath => "transfer:" + _id;
        public override Stream WriteStream => _stream;

        private BrowserSandboxTransferFile() => _stream = new BufferedWriteStream(_id);

        /// <summary>创建浏览器传输暂存，不占用工作区的 MEMFS 文件副本。</summary>
        internal static Task<SandboxTransferFile> CreateAsync() => Task.FromResult<SandboxTransferFile>(new BrowserSandboxTransferFile());

        /// <summary>排出最后一块缓冲，并组合可读取、可下载的 Blob 快照。</summary>
        public override async Task CompleteAsync(CancellationToken cancellationToken)
        {
            if (_completed) return;
            await _stream.FlushAsync(cancellationToken);
            await CompleteTemporary(_id);
            _completed = true;
        }

        /// <summary>打开暂存快照的异步寻址流。</summary>
        public override Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_completed) throw new InvalidOperationException();
            return Task.FromResult<Stream>(new BrowserSandboxFileTransferService.BrowserReadStream(_id, _stream.Length));
        }

        /// <summary>页面同步释放时启动受观察的异步清理。</summary>
        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stream.Dispose();
            _ = CleanupAsync();
        }

        private async Task CleanupAsync()
        {
            try { await DisposeTemporary(_id); }
            catch (Exception exception) { Console.Error.WriteLine("Failed to release temporary transfer storage: " + exception.Message); }
        }

        /// <summary>删除 IndexedDB 暂存并释放内存引用。</summary>
        public override async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _stream.Dispose();
            await DisposeTemporary(_id);
        }

        [JSImport("createTemporary", ModuleName)]
        private static partial int CreateTemporary();
        [JSImport("writeTemporary", ModuleName)]
        private static partial Task WriteTemporary(int id, string data);
        [JSImport("completeTemporary", ModuleName)]
        private static partial Task CompleteTemporary(int id);
        [JSImport("disposeTemporary", ModuleName)]
        private static partial Task DisposeTemporary(int id);

        /// <summary>以固定一兆字节缓冲合并 ZIP 的小写入，等待每次溢写后才继续接收。</summary>
        private sealed class BufferedWriteStream(int id) : Stream
        {
            private readonly byte[] _buffer = new byte[1024 * 1024];
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
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override async Task FlushAsync(CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_buffered == 0) return;
                await WriteTemporary(id, Convert.ToBase64String(_buffer, 0, _buffered));
                _buffered = 0;
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                while (!buffer.IsEmpty)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int count = Math.Min(buffer.Length, _buffer.Length - _buffered);
                    buffer[..count].CopyTo(_buffer.AsMemory(_buffered));
                    _buffered += count;
                    _length += count;
                    buffer = buffer[count..];
                    if (_buffered == _buffer.Length) await FlushAsync(cancellationToken);
                }
            }
        }
    }
}