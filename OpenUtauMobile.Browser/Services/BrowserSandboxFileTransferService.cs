using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenUtauMobile.Storage;

namespace OpenUtauMobile.Browser.Services
{
    /// <summary>通过浏览器选择对象和分块读取导入文件，下载由独立用户动作发起。</summary>
    internal sealed partial class BrowserSandboxFileTransferService : ISandboxFileTransferService
    {
        private const string ModuleName = "OpenUtauMobileFileTransfer";
        public bool CanImportFolder => CanPickFolder();
        public bool StartsDownload => true;

        public async Task<SandboxImportBatch?> PickAsync(bool folder)
        {
            string json = await Pick(folder);
            using JsonDocument document = JsonDocument.Parse(json);
            List<BrowserEntry> entries = [];
            foreach (JsonElement item in document.RootElement.EnumerateArray())
                entries.Add(new BrowserEntry(item.GetProperty("id").GetInt32(), item.GetProperty("path").GetString()!,
                    item.GetProperty("directory").GetBoolean(), item.GetProperty("size").GetInt64()));
            return entries.Count == 0 ? null : new BrowserBatch(entries);
        }

        public Task<bool> SaveAsync(string sourcePath, string suggestedName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Download(sourcePath, suggestedName);
            return Task.FromResult(true);
        }

        [JSImport("canPickFolder", ModuleName)]
        private static partial bool CanPickFolder();
        [JSImport("pick", ModuleName)]
        private static partial Task<string> Pick(bool folder);
        [JSImport("readChunk", ModuleName)]
        private static partial Task<string> ReadChunk(int id, double offset, int count);
        [JSImport("release", ModuleName)]
        private static partial void Release(string ids);
        [JSImport("download", ModuleName)]
        private static partial void Download(string path, string name);

        private sealed record BrowserEntry(int Id, string Path, bool Directory, long Size);

        private sealed class BrowserBatch(List<BrowserEntry> entries) : SandboxImportBatch
        {
            public override async IAsyncEnumerable<SandboxImportEntry> GetEntriesAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                foreach (BrowserEntry entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return new SandboxImportEntry(entry.Path, entry.Directory,
                        entry.Directory ? null : () => Task.FromResult<Stream>(new BrowserReadStream(entry.Id, entry.Size)));
                }
                await Task.CompletedTask;
            }

            public override ValueTask DisposeAsync()
            {
                Release(JsonSerializer.Serialize(entries.Where(entry => !entry.Directory).Select(entry => entry.Id).ToArray()));
                return ValueTask.CompletedTask;
            }
        }

        /// <summary>文件选择对象和暂存 Blob 均通过异步分块读取，寻址不触发同步文件读取。</summary>
        internal sealed class BrowserReadStream(int id, long length) : Stream
        {
            private long _position;
            public override bool CanRead => true;
            public override bool CanSeek => true;
            public override bool CanWrite => false;
            public override long Length => length;
            public override long Position
            {
                get => _position;
                set
                {
                    if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                    _position = value;
                }
            }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin)
            {
                Position = checked((origin switch
                {
                    SeekOrigin.Begin => 0,
                    SeekOrigin.Current => _position,
                    SeekOrigin.End => length,
                    _ => throw new ArgumentOutOfRangeException(nameof(origin)),
                }) + offset);
                return _position;
            }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (buffer.Length == 0) return 0;
                byte[] bytes = Convert.FromBase64String(await ReadChunk(id, _position, Math.Min(buffer.Length, 128 * 1024)));
                cancellationToken.ThrowIfCancellationRequested();
                bytes.AsMemory().CopyTo(buffer);
                _position += bytes.Length;
                return bytes.Length;
            }
        }
    }
}