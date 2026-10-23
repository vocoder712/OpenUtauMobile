using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using OpenUtauMobile.Helpers;

namespace OpenUtauMobile.Storage
{
    /// <summary>通过系统文件选择器和存储项流完成原生沙箱的数据交换。</summary>
    public sealed class StorageProviderSandboxTransferService : ISandboxFileTransferService
    {
        public bool CanImportFolder => StorageProviderFactory.GetStorageProvider()?.CanPickFolder == true;
        public bool StartsDownload => false;

        /// <summary>保持所选存储项有效，直到整个导入批次释放。</summary>
        public async Task<SandboxImportBatch?> PickAsync(bool folder)
        {
            IStorageProvider provider = StorageProviderFactory.GetStorageProvider()
                ?? throw new IOException(L.S("Sandbox.TransferUnavailable"));
            List<IStorageItem> items = [];
            if (folder)
            {
                if (!provider.CanPickFolder) throw new IOException(L.S("Sandbox.FolderUnavailable"));
                items.AddRange(await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
                {
                    Title = L.S("Sandbox.ImportFolder"),
                    AllowMultiple = false,
                }));
            }
            else
            {
                if (!provider.CanOpen) throw new IOException(L.S("Sandbox.TransferUnavailable"));
                items.AddRange(await provider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = L.S("Sandbox.ImportFiles"),
                    AllowMultiple = true,
                }));
            }
            return items.Count == 0 ? null : new StorageBatch(items);
        }

        /// <summary>将准备好的文件复制到系统保存位置，取消不产生成功提示。</summary>
        public async Task<bool> SaveAsync(string sourcePath, string suggestedName, CancellationToken cancellationToken)
        {
            IStorageProvider provider = StorageProviderFactory.GetStorageProvider()
                ?? throw new IOException(L.S("Sandbox.TransferUnavailable"));
            if (!provider.CanSave) throw new IOException(L.S("Sandbox.TransferUnavailable"));
            using IStorageFile? destination = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = L.S("Sandbox.SaveToDevice"),
                SuggestedFileName = suggestedName,
                ShowOverwritePrompt = true,
            });
            if (destination == null) return false;
            cancellationToken.ThrowIfCancellationRequested();
            await using FileStream source = File.OpenRead(sourcePath);
            await using Stream output = await destination.OpenWriteAsync();
            if (output.CanSeek) output.SetLength(0);
            await source.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            return true;
        }

        private sealed class StorageBatch(List<IStorageItem> roots) : SandboxImportBatch
        {
            private readonly List<IStorageItem> _owned = [.. roots];

            public override async IAsyncEnumerable<SandboxImportEntry> GetEntriesAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                foreach (IStorageItem root in roots)
                {
                    await foreach (SandboxImportEntry entry in EnumerateAsync(root, root.Name, 0, cancellationToken))
                        yield return entry;
                }
            }

            private async IAsyncEnumerable<SandboxImportEntry> EnumerateAsync(IStorageItem item, string relativePath,
                int depth, [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (depth > 64) throw new IOException(L.S("Sandbox.ImportLimit"));
                if (item is IStorageFile file)
                    yield return new SandboxImportEntry(relativePath, false, file.OpenReadAsync);
                else if (item is IStorageFolder folder)
                {
                    yield return new SandboxImportEntry(relativePath, true, null);
                    await foreach (IStorageItem child in folder.GetItemsAsync().WithCancellation(cancellationToken))
                    {
                        _owned.Add(child);
                        await foreach (SandboxImportEntry entry in EnumerateAsync(child,
                            relativePath + "/" + child.Name, depth + 1, cancellationToken))
                            yield return entry;
                    }
                }
            }

            public override ValueTask DisposeAsync()
            {
                for (int index = _owned.Count - 1; index >= 0; index--) _owned[index].Dispose();
                _owned.Clear();
                return ValueTask.CompletedTask;
            }
        }
    }
}
