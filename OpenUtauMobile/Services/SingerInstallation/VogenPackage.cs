using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace OpenUtauMobile.Services.SingerInstallation
{
    /// <summary>仅读取 Vogen 展示信息，与安装提交及模型加载无关。</summary>
    internal static class VogenPackage
    {
        /// <summary>无法取得可展示的名称时返回空；读取异常由页面转换为非阻断提示。</summary>
        internal static (string Name, string Version)? ReadMetadata(string source, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using FileStream input = File.OpenRead(source);
            using IArchive archive = ArchiveFactory.OpenArchive(input, new ReaderOptions { LeaveStreamOpen = false });
            IArchiveEntry? meta = archive.Entries.FirstOrDefault(entry => !entry.IsDirectory && entry.Key == "meta.json");
            if (meta == null) return null;
            using Stream stream = meta.OpenEntryStream();
            using JsonDocument document = JsonDocument.Parse(stream);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("name", out JsonElement name)
                || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString())) return null;
            token.ThrowIfCancellationRequested();
            return (name.GetString()!, root.TryGetProperty("version", out JsonElement version) && version.ValueKind == JsonValueKind.String
                ? version.GetString()! : "");
        }
    }
}
