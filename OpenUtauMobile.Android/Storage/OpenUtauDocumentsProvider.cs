using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Android.App;
using Android.Content;
using Android.Database;
using Android.OS;
using Android.Provider;
using Android.Webkit;
using Java.Lang;
using File = System.IO.File;
using FileNotFoundException = Java.IO.FileNotFoundException;
using IOException = System.IO.IOException;
using Object = Java.Lang.Object;

namespace OpenUtauMobile.Android.Storage;

/// <summary>
/// 通过 Android SAF 暴露当前应用的完整凭据加密数据目录。
/// </summary>
[ContentProvider(
    ["${applicationId}.documents"],
    Exported = true,
    GrantUriPermissions = true,
    Permission = global::Android.Manifest.Permission.ManageDocuments)]
[IntentFilter([DocumentsContract.ProviderInterface])]
public sealed class OpenUtauDocumentsProvider : DocumentsProvider
{
    private const string RootId = "app-data";
    private const string RootDocumentId = "root";
    private const string DocumentIdPrefix = "path:";

    private static readonly string[] DefaultRootProjection =
    [
        DocumentsContract.Root.ColumnRootId,
        DocumentsContract.Root.ColumnDocumentId,
        DocumentsContract.Root.ColumnTitle,
        DocumentsContract.Root.ColumnSummary,
        DocumentsContract.Root.ColumnFlags,
        DocumentsContract.Root.ColumnMimeTypes,
        DocumentsContract.Root.ColumnIcon,
        DocumentsContract.Root.ColumnAvailableBytes,
    ];

    private static readonly string[] DefaultDocumentProjection =
    [
        DocumentsContract.Document.ColumnDocumentId,
        DocumentsContract.Document.ColumnDisplayName,
        DocumentsContract.Document.ColumnMimeType,
        DocumentsContract.Document.ColumnFlags,
        DocumentsContract.Document.ColumnSize,
        DocumentsContract.Document.ColumnLastModified,
    ];

    private string? dataRoot;

    private string Authority => $"{Context?.PackageName}.documents";

    private string DataRoot
    {
        get
        {
            if (!string.IsNullOrEmpty(dataRoot))
            {
                return dataRoot;
            }

            Context context = Context
                ?? throw new InvalidOperationException("DocumentsProvider 尚未附加 Android Context。");
            string? path = context.DataDir?.AbsolutePath ?? context.ApplicationInfo?.DataDir;
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("无法确定 Android 应用 dataDir。");
            }

            dataRoot = GetCanonicalPath(path);
            return dataRoot;
        }
    }

    public override bool OnCreate() => true;

    public override ICursor QueryRoots(string[]? projection)
    {
        string[] columns = projection is { Length: > 0 } ? projection : DefaultRootProjection;
        MatrixCursor cursor = new(columns);
        DocumentRootFlags flags = DocumentRootFlags.LocalOnly |
                                  DocumentRootFlags.SupportsCreate |
                                  DocumentRootFlags.SupportsIsChild;
        int iconResource = Context?.ApplicationInfo?.Icon ?? 0;
        long availableBytes = 0;
        try
        {
            availableBytes = new StatFs(DataRoot).AvailableBytes;
        }
        catch
        {
            // 可用空间不是根查询的必要字段。
        }

        AddRow(cursor, columns, column => column switch
        {
            DocumentsContract.Root.ColumnRootId => RootId,
            DocumentsContract.Root.ColumnDocumentId => RootDocumentId,
            DocumentsContract.Root.ColumnTitle => "OpenUtau Mobile",
            DocumentsContract.Root.ColumnSummary => "OpenUtau Mobile 私有数据目录",
            DocumentsContract.Root.ColumnFlags => (int)flags,
            DocumentsContract.Root.ColumnMimeTypes => "*/*",
            DocumentsContract.Root.ColumnIcon => iconResource == 0 ? null : iconResource,
            DocumentsContract.Root.ColumnAvailableBytes => availableBytes,
            _ => null,
        });
        return cursor;
    }

    public override ICursor QueryDocument(string? documentId, string[]? projection)
    {
        documentId = RequireValue(documentId, nameof(documentId));
        string[] columns = projection is { Length: > 0 } ? projection : DefaultDocumentProjection;
        MatrixCursor cursor = new(columns);
        AddDocumentRow(cursor, columns, ResolveDocument(documentId, requireExists: true));
        return cursor;
    }

    public override ICursor QueryChildDocuments(
        string? parentDocumentId,
        string[]? projection,
        string? sortOrder)
    {
        parentDocumentId = RequireValue(parentDocumentId, nameof(parentDocumentId));
        string[] columns = projection is { Length: > 0 } ? projection : DefaultDocumentProjection;
        MatrixCursor cursor = new(columns);
        ResolvedDocument parent = ResolveDocument(parentDocumentId, requireExists: true);
        if (!Directory.Exists(parent.LexicalPath))
        {
            throw new FileNotFoundException($"文档不是目录：{parentDocumentId}");
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.EnumerateFileSystemEntries(parent.LexicalPath)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (System.Exception exception)
        {
            throw AsFileNotFound("无法读取目录。", exception);
        }

        foreach (string entry in entries)
        {
            try
            {
                string childRelativePath = CombineRelativePath(parent.RelativePath, Path.GetFileName(entry));
                AddDocumentRow(cursor, columns, ResolveRelativePath(childRelativePath, requireExists: true));
            }
            catch
            {
                // 目录可能并发变化；越界符号链接也不会出现在文档树中。
            }
        }
        return cursor;
    }

    public override ParcelFileDescriptor OpenDocument(
        string? documentId,
        string? mode,
        CancellationSignal? signal)
    {
        documentId = RequireValue(documentId, nameof(documentId));
        mode = RequireValue(mode, nameof(mode));
        ResolvedDocument document = ResolveMutableDocument(documentId);
        if (Directory.Exists(document.LexicalPath))
        {
            throw new FileNotFoundException("目录不能作为普通文件打开。");
        }

        try
        {
            return ParcelFileDescriptor.Open(
                new Java.IO.File(document.LexicalPath),
                ParcelFileDescriptor.ParseMode(mode))
                ?? throw new FileNotFoundException("Android 未返回文件描述符。");
        }
        catch (System.Exception exception)
        {
            throw AsFileNotFound("无法打开文档。", exception);
        }
    }

    public override string CreateDocument(string? parentDocumentId, string? mimeType, string? displayName)
    {
        parentDocumentId = RequireValue(parentDocumentId, nameof(parentDocumentId));
        mimeType = RequireValue(mimeType, nameof(mimeType));
        displayName = RequireValue(displayName, nameof(displayName));
        ValidateDisplayName(displayName);
        ResolvedDocument parent = ResolveDocument(parentDocumentId, requireExists: true);
        if (!Directory.Exists(parent.LexicalPath))
        {
            throw new FileNotFoundException("父文档不是目录。");
        }

        bool directory = string.Equals(
            mimeType,
            DocumentsContract.Document.MimeTypeDir,
            StringComparison.Ordinal);
        string targetRelativePath = GetUniqueRelativePath(parent.RelativePath, displayName, directory);
        ResolvedDocument target = ResolveRelativePath(targetRelativePath, requireExists: false);
        try
        {
            if (directory)
            {
                Directory.CreateDirectory(target.LexicalPath);
            }
            else
            {
                using FileStream stream = new(
                    target.LexicalPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read);
            }
        }
        catch (System.Exception exception)
        {
            throw AsFileNotFound("无法创建文档。", exception);
        }

        string targetId = EncodeDocumentId(targetRelativePath);
        NotifyChanged(parentDocumentId, targetId);
        return targetId;
    }

    public override void DeleteDocument(string? documentId)
    {
        documentId = RequireValue(documentId, nameof(documentId));
        ResolvedDocument document = ResolveMutableDocument(documentId);
        string parentId = GetParentDocumentId(document.RelativePath);
        try
        {
            if (IsSymbolicLink(document.LexicalPath))
            {
                File.Delete(document.LexicalPath);
            }
            else if (Directory.Exists(document.LexicalPath))
            {
                Directory.Delete(document.LexicalPath, recursive: true);
            }
            else
            {
                File.Delete(document.LexicalPath);
            }
        }
        catch (System.Exception exception)
        {
            throw AsFileNotFound("无法删除文档。", exception);
        }

        RevokeDocumentPermission(documentId);
        NotifyChanged(parentId, documentId);
    }

    public override string? RenameDocument(string? documentId, string? displayName)
    {
        documentId = RequireValue(documentId, nameof(documentId));
        displayName = RequireValue(displayName, nameof(displayName));
        ValidateDisplayName(displayName);
        ResolvedDocument source = ResolveMutableDocument(documentId);
        if (string.Equals(displayName, GetDisplayName(source), StringComparison.Ordinal))
        {
            return null;
        }
        string parentRelativePath = GetParentRelativePath(source.RelativePath);
        bool directory = Directory.Exists(source.LexicalPath) && !IsSymbolicLink(source.LexicalPath);
        string targetRelativePath = GetUniqueRelativePath(parentRelativePath, displayName, directory);
        ResolvedDocument target = ResolveRelativePath(targetRelativePath, requireExists: false);
        MovePath(source.LexicalPath, target.LexicalPath);

        string targetId = EncodeDocumentId(targetRelativePath);
        NotifyChanged(ToDocumentId(parentRelativePath), documentId, targetId);
        return targetId;
    }

    public override string CopyDocument(string? sourceDocumentId, string? targetParentDocumentId)
    {
        sourceDocumentId = RequireValue(sourceDocumentId, nameof(sourceDocumentId));
        targetParentDocumentId = RequireValue(targetParentDocumentId, nameof(targetParentDocumentId));
        ResolvedDocument source = ResolveMutableDocument(sourceDocumentId);
        ResolvedDocument targetParent = ResolveDocument(targetParentDocumentId, requireExists: true);
        EnsureDirectory(targetParent);
        EnsureTargetIsNotDescendant(source, targetParent);

        bool directory = Directory.Exists(source.LexicalPath) && !IsSymbolicLink(source.LexicalPath);
        string targetRelativePath = GetUniqueRelativePath(
            targetParent.RelativePath,
            GetDisplayName(source),
            directory);
        ResolvedDocument target = ResolveRelativePath(targetRelativePath, requireExists: false);
        CopyPath(source.LexicalPath, target.LexicalPath);

        string targetId = EncodeDocumentId(targetRelativePath);
        NotifyChanged(targetParentDocumentId, targetId);
        return targetId;
    }

    public override string MoveDocument(
        string? sourceDocumentId,
        string? sourceParentDocumentId,
        string? targetParentDocumentId)
    {
        sourceDocumentId = RequireValue(sourceDocumentId, nameof(sourceDocumentId));
        sourceParentDocumentId = RequireValue(sourceParentDocumentId, nameof(sourceParentDocumentId));
        targetParentDocumentId = RequireValue(targetParentDocumentId, nameof(targetParentDocumentId));
        ResolvedDocument source = ResolveMutableDocument(sourceDocumentId);
        ResolvedDocument sourceParent = ResolveDocument(sourceParentDocumentId, requireExists: true);
        ResolvedDocument targetParent = ResolveDocument(targetParentDocumentId, requireExists: true);
        EnsureDirectory(sourceParent);
        EnsureDirectory(targetParent);
        if (!string.Equals(
                GetParentRelativePath(source.RelativePath),
                sourceParent.RelativePath,
                StringComparison.Ordinal))
        {
            throw new FileNotFoundException("源文档不属于指定父目录。");
        }
        if (string.Equals(
                sourceParent.RelativePath,
                targetParent.RelativePath,
                StringComparison.Ordinal))
        {
            return sourceDocumentId;
        }
        EnsureTargetIsNotDescendant(source, targetParent);

        bool directory = Directory.Exists(source.LexicalPath) && !IsSymbolicLink(source.LexicalPath);
        string targetRelativePath = GetUniqueRelativePath(
            targetParent.RelativePath,
            GetDisplayName(source),
            directory);
        ResolvedDocument target = ResolveRelativePath(targetRelativePath, requireExists: false);
        MovePath(source.LexicalPath, target.LexicalPath);

        string targetId = EncodeDocumentId(targetRelativePath);
        NotifyChanged(sourceParentDocumentId, targetParentDocumentId, sourceDocumentId, targetId);
        return targetId;
    }

    public override bool IsChildDocument(string? parentDocumentId, string? documentId)
    {
        try
        {
            parentDocumentId = RequireValue(parentDocumentId, nameof(parentDocumentId));
            documentId = RequireValue(documentId, nameof(documentId));
            ResolvedDocument parent = ResolveDocument(parentDocumentId, requireExists: true);
            ResolvedDocument document = ResolveDocument(documentId, requireExists: true);
            if (string.Equals(parent.CanonicalPath, document.CanonicalPath, StringComparison.Ordinal))
            {
                return false;
            }
            return IsWithinRoot(document.CanonicalPath, parent.CanonicalPath);
        }
        catch
        {
            return false;
        }
    }

    private void AddDocumentRow(MatrixCursor cursor, string[] columns, ResolvedDocument document)
    {
        bool directory = Directory.Exists(document.LexicalPath);
        bool symbolicLink = IsSymbolicLink(document.LexicalPath);
        string mimeType = directory
            ? DocumentsContract.Document.MimeTypeDir
            : GetMimeType(document.LexicalPath);
        DocumentContractFlags flags;
        if (document.IsRoot)
        {
            flags = DocumentContractFlags.DirSupportsCreate;
        }
        else
        {
            flags = DocumentContractFlags.SupportsDelete |
                    DocumentContractFlags.SupportsMove |
                    DocumentContractFlags.SupportsRename;
            if (!symbolicLink)
            {
                flags |= DocumentContractFlags.SupportsCopy;
            }
            flags |= directory
                ? DocumentContractFlags.DirSupportsCreate
                : DocumentContractFlags.SupportsWrite;
        }

        long? size = null;
        long? lastModified = null;
        try
        {
            if (!directory)
            {
                size = new FileInfo(document.LexicalPath).Length;
            }
            DateTime modified = File.GetLastWriteTimeUtc(document.LexicalPath);
            if (modified > DateTime.UnixEpoch)
            {
                lastModified = new DateTimeOffset(modified).ToUnixTimeMilliseconds();
            }
        }
        catch
        {
            // 元数据读取失败不影响项目显示。
        }

        AddRow(cursor, columns, column => column switch
        {
            DocumentsContract.Document.ColumnDocumentId => document.DocumentId,
            DocumentsContract.Document.ColumnDisplayName => GetDisplayName(document),
            DocumentsContract.Document.ColumnMimeType => mimeType,
            DocumentsContract.Document.ColumnFlags => (int)flags,
            DocumentsContract.Document.ColumnSize => size,
            DocumentsContract.Document.ColumnLastModified => lastModified,
            _ => null,
        });
    }

    private static void AddRow(
        MatrixCursor cursor,
        IEnumerable<string> columns,
        Func<string, object?> getValue)
    {
        MatrixCursor.RowBuilder row = cursor.NewRow()
            ?? throw new InvalidOperationException("无法创建文档查询结果行。");
        foreach (string column in columns)
        {
            row.Add(ToJavaObject(getValue(column)));
        }
    }

    private static Object? ToJavaObject(object? value) => value switch
    {
        null => null,
        string text => new Java.Lang.String(text),
        int number => Integer.ValueOf(number),
        long number => Long.ValueOf(number),
        _ => new Java.Lang.String(value.ToString() ?? string.Empty),
    };

    private ResolvedDocument ResolveMutableDocument(string documentId)
    {
        ResolvedDocument document = ResolveDocument(documentId, requireExists: true);
        if (document.IsRoot)
        {
            throw new FileNotFoundException("不能修改文档提供器根节点。");
        }
        return document;
    }

    private ResolvedDocument ResolveDocument(string documentId, bool requireExists)
    {
        if (string.Equals(documentId, RootDocumentId, StringComparison.Ordinal))
        {
            return ResolveRelativePath(string.Empty, requireExists);
        }
        if (!documentId.StartsWith(DocumentIdPrefix, StringComparison.Ordinal))
        {
            throw new FileNotFoundException("无效的文档 ID。");
        }

        string relativePath;
        try
        {
            relativePath = DecodeBase64Url(documentId[DocumentIdPrefix.Length..]);
        }
        catch (System.Exception exception)
        {
            throw AsFileNotFound("无效的文档 ID。", exception);
        }
        return ResolveRelativePath(relativePath, requireExists);
    }

    private ResolvedDocument ResolveRelativePath(string relativePath, bool requireExists)
    {
        string normalized = NormalizeRelativePath(relativePath);
        string lexicalPath = normalized.Length == 0
            ? DataRoot
            : Path.Combine(DataRoot, normalized.Replace('/', Path.DirectorySeparatorChar));
        string fullPath = Path.GetFullPath(lexicalPath);
        if (!IsWithinRoot(fullPath, DataRoot, allowEqual: true))
        {
            throw new FileNotFoundException("文档路径超出应用 dataDir。");
        }

        string canonicalPath;
        try
        {
            canonicalPath = GetCanonicalPath(fullPath);
        }
        catch (System.Exception exception)
        {
            throw AsFileNotFound("无法解析文档路径。", exception);
        }
        if (!IsWithinRoot(canonicalPath, DataRoot, allowEqual: true))
        {
            throw new FileNotFoundException("符号链接目标超出应用 dataDir。");
        }
        if (requireExists && !File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            throw new FileNotFoundException("文档不存在。");
        }

        string documentId = normalized.Length == 0 ? RootDocumentId : EncodeDocumentId(normalized);
        return new ResolvedDocument(documentId, normalized, fullPath, canonicalPath, normalized.Length == 0);
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (relativePath.Length == 0)
        {
            return string.Empty;
        }
        if (relativePath.IndexOf('\0') >= 0 ||
            relativePath.Contains('\\') ||
            relativePath.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            throw new FileNotFoundException("无效的相对路径。");
        }

        string[] segments = relativePath.Split('/');
        if (segments.Any(segment =>
                segment.Length == 0 || segment is "." or ".."))
        {
            throw new FileNotFoundException("无效的相对路径段。");
        }
        return string.Join('/', segments);
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) ||
            displayName is "." or ".." ||
            displayName.IndexOf('\0') >= 0 ||
            displayName.Contains('/') ||
            displayName.Contains('\\'))
        {
            throw new FileNotFoundException("无效的文件名。");
        }
    }

    private static string RequireValue(string? value, string name)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new FileNotFoundException($"缺少必要参数：{name}。");
        }
        return value;
    }

    private string GetUniqueRelativePath(string parentRelativePath, string displayName, bool directory)
    {
        string candidate = CombineRelativePath(parentRelativePath, displayName);
        if (!PathExists(ResolveRelativePath(candidate, requireExists: false).LexicalPath))
        {
            return candidate;
        }

        string extension = directory ? string.Empty : Path.GetExtension(displayName);
        string baseName = directory ? displayName : Path.GetFileNameWithoutExtension(displayName);
        for (int suffix = 1; suffix < 10000; suffix++)
        {
            candidate = CombineRelativePath(parentRelativePath, $"{baseName} ({suffix}){extension}");
            if (!PathExists(ResolveRelativePath(candidate, requireExists: false).LexicalPath))
            {
                return candidate;
            }
        }
        throw new IOException("无法生成不冲突的文件名。");
    }

    private static bool PathExists(string path) => File.Exists(path) || Directory.Exists(path);

    private static string CombineRelativePath(string parentRelativePath, string name) =>
        parentRelativePath.Length == 0 ? name : $"{parentRelativePath}/{name}";

    private static string GetParentRelativePath(string relativePath)
    {
        int separator = relativePath.LastIndexOf('/');
        return separator < 0 ? string.Empty : relativePath[..separator];
    }

    private static string GetParentDocumentId(string relativePath)
    {
        string parentRelativePath = GetParentRelativePath(relativePath);
        return ToDocumentId(parentRelativePath);
    }

    private static string ToDocumentId(string relativePath) =>
        relativePath.Length == 0 ? RootDocumentId : EncodeDocumentId(relativePath);

    private static string EncodeDocumentId(string relativePath)
    {
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(relativePath))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return DocumentIdPrefix + encoded;
    }

    private static string DecodeBase64Url(string encoded)
    {
        string base64 = encoded.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException("无效的 Base64URL 长度。"),
        };
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static string GetCanonicalPath(string path) => new Java.IO.File(path).CanonicalPath
        ?? throw new IOException("无法解析 canonical path。");

    private static bool IsWithinRoot(string path, string root, bool allowEqual = false)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        string normalizedPath = Path.TrimEndingDirectorySeparator(path);
        if (allowEqual && string.Equals(normalizedPath, normalizedRoot, StringComparison.Ordinal))
        {
            return true;
        }
        return normalizedPath.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            StringComparison.Ordinal);
    }

    private static string GetDisplayName(ResolvedDocument document) =>
        document.IsRoot ? "OpenUtau Mobile 应用数据" : document.RelativePath[(document.RelativePath.LastIndexOf('/') + 1)..];

    private static string GetMimeType(string path)
    {
        string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (extension.Length == 0)
        {
            return "application/octet-stream";
        }
        return extension switch
        {
            "json" => "application/json",
            "xml" => "application/xml",
            "db" or "sqlite" or "sqlite3" => "application/vnd.sqlite3",
            "yaml" or "yml" or "ustx" => "application/yaml",
            "log" => "text/plain",
            _ => MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension)
                 ?? "application/octet-stream",
        };
    }

    private static bool IsSymbolicLink(string path)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path)
                ? new DirectoryInfo(path)
                : new FileInfo(path);
            return info.LinkTarget != null;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureDirectory(ResolvedDocument document)
    {
        if (!Directory.Exists(document.LexicalPath))
        {
            throw new FileNotFoundException("目标文档不是目录。");
        }
    }

    private static void EnsureTargetIsNotDescendant(
        ResolvedDocument source,
        ResolvedDocument targetParent)
    {
        if (Directory.Exists(source.LexicalPath) &&
            IsWithinRoot(targetParent.CanonicalPath, source.CanonicalPath, allowEqual: true))
        {
            throw new FileNotFoundException("不能把目录复制或移动到自身内部。");
        }
    }

    private static void MovePath(string sourcePath, string targetPath)
    {
        try
        {
            if (Directory.Exists(sourcePath) && !IsSymbolicLink(sourcePath))
            {
                Directory.Move(sourcePath, targetPath);
            }
            else
            {
                File.Move(sourcePath, targetPath);
            }
        }
        catch (System.Exception exception)
        {
            throw AsFileNotFound("无法移动文档。", exception);
        }
    }

    private void CopyPath(string sourcePath, string targetPath)
    {
        string temporaryPath = targetPath + $".opum-copy-{Guid.NewGuid():N}";
        try
        {
            if (Directory.Exists(sourcePath) && !IsSymbolicLink(sourcePath))
            {
                CopyDirectory(sourcePath, temporaryPath, [with(StringComparer.Ordinal)]);
                Directory.Move(temporaryPath, targetPath);
            }
            else
            {
                if (IsSymbolicLink(sourcePath))
                {
                    throw new IOException("不支持复制符号链接。");
                }
                File.Copy(sourcePath, temporaryPath, overwrite: false);
                File.Move(temporaryPath, targetPath);
            }
        }
        catch (System.Exception exception)
        {
            TryDeletePath(temporaryPath);
            throw AsFileNotFound("无法复制文档。", exception);
        }
    }

    private void CopyDirectory(string sourcePath, string targetPath, HashSet<string> visited)
    {
        string canonicalSource = GetCanonicalPath(sourcePath);
        if (!IsWithinRoot(canonicalSource, DataRoot, allowEqual: true) || !visited.Add(canonicalSource))
        {
            throw new IOException("目录包含越界或循环符号链接。");
        }

        Directory.CreateDirectory(targetPath);
        foreach (string entry in Directory.EnumerateFileSystemEntries(sourcePath))
        {
            if (IsSymbolicLink(entry))
            {
                throw new IOException("不支持复制包含符号链接的目录。");
            }
            string destination = Path.Combine(targetPath, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                CopyDirectory(entry, destination, visited);
            }
            else
            {
                File.Copy(entry, destination, overwrite: false);
            }
        }
        visited.Remove(canonicalSource);
    }

    private static void TryDeletePath(string path)
    {
        try
        {
            if (Directory.Exists(path) && !IsSymbolicLink(path))
            {
                Directory.Delete(path, recursive: true);
            }
            else if (File.Exists(path) || IsSymbolicLink(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 仅用于清理未完成的复制临时项。
        }
    }

    private void NotifyChanged(params string[] documentIds)
    {
        ContentResolver? resolver = Context?.ContentResolver;
        if (resolver == null)
        {
            return;
        }

        foreach (string documentId in documentIds.Distinct(StringComparer.Ordinal))
        {
            global::Android.Net.Uri? documentUri =
                DocumentsContract.BuildDocumentUri(Authority, documentId);
            global::Android.Net.Uri? childrenUri =
                DocumentsContract.BuildChildDocumentsUri(Authority, documentId);
            if (documentUri != null)
            {
                resolver.NotifyChange(documentUri, null);
            }
            if (childrenUri != null)
            {
                resolver.NotifyChange(childrenUri, null);
            }
        }
    }

    private static FileNotFoundException AsFileNotFound(string message, System.Exception exception) =>
        new($"{message} {exception.Message}");

    private sealed record ResolvedDocument(
        string DocumentId,
        string RelativePath,
        string LexicalPath,
        string CanonicalPath,
        bool IsRoot);
}
