using Android.Database;
using Android.OS;
using Android.Provider;
using OpenUtau.Core;
using Android.Content;
using Android.App;
using Serilog;
using Android.Webkit;

namespace OpenUtauMobile.Platforms.Android.Providers
{
    [ContentProvider(
        ["pers.vocoder712.openutaumobile.documents"], // ID
        Name = "pers.vocoder712.openutaumobile.OpenUtauDocumentsProvider", // 类名
        Exported = true, // 允许外部访问
        GrantUriPermissions = true, // 允许授予URI权限
        Permission = "android.permission.MANAGE_DOCUMENTS")]
    [IntentFilter(["android.content.action.DOCUMENTS_PROVIDER"])]
    public class OpenUtauDocumentsProvider : DocumentsProvider
    {
        private const string RootId = "openutaumobile_root"; // 根目录ID

        /// <summary>
        /// 根目录的默认投影字段
        /// </summary>
        private static string[] DefaultRootProjection { get; } =
        [
            DocumentsContract.Root.ColumnRootId,
            DocumentsContract.Root.ColumnMimeTypes,
            DocumentsContract.Root.ColumnFlags,
            DocumentsContract.Root.ColumnIcon,
            DocumentsContract.Root.ColumnTitle,
            DocumentsContract.Root.ColumnSummary,
            DocumentsContract.Root.ColumnDocumentId,
            DocumentsContract.Root.ColumnAvailableBytes
        ];
        /// <summary>
        /// 文档的默认投影字段
        /// </summary>
        private static string[] DefaultDocumentProjection { get; } =
        [
            DocumentsContract.Document.ColumnDocumentId,
            DocumentsContract.Document.ColumnMimeType,
            DocumentsContract.Document.ColumnDisplayName,
            DocumentsContract.Document.ColumnLastModified,
            DocumentsContract.Document.ColumnFlags,
            DocumentsContract.Document.ColumnSize
        ];

        private Java.IO.File BaseDir { get; set; } = null!;
        /// <summary>
        /// 初始化
        /// </summary>
        /// <returns></returns>
        public override bool OnCreate()
        {
            BaseDir = new Java.IO.File(PathManager.Inst.DataPath);
            return true;
        }
        /// <summary>
        /// 查询根目录
        /// </summary>
        /// <param name="projection"></param>
        /// <returns></returns>
        public override ICursor QueryRoots(string[]? projection)
        {
            MatrixCursor result = new(projection ?? DefaultRootProjection); // 创建结果集
            MatrixCursor.RowBuilder? rowBuilder = result.NewRow();

            if (rowBuilder == null)
            {
                return result;
            }

            rowBuilder.Add(DocumentsContract.Root.ColumnRootId, RootId); // 根目录ID
            rowBuilder.Add(DocumentsContract.Root.ColumnDocumentId, GetDocIdForFile(BaseDir)); // 根目录对应的文档ID
            rowBuilder.Add(DocumentsContract.Root.ColumnTitle, "OpenUtau Mobile"); // 根目录标题
            rowBuilder.Add(DocumentsContract.Root.ColumnSummary, "OpenUtau数据目录"); // 根目录摘要
            rowBuilder.Add(DocumentsContract.Root.ColumnIcon, Resource.Mipmap.appicon); // 根目录图标资源ID
            rowBuilder.Add(DocumentsContract.Root.ColumnFlags, // 根目录标志
                (int)DocumentRootFlags.LocalOnly | // 仅本地
                (int)DocumentRootFlags.SupportsIsChild); // 支持子项检查
            rowBuilder.Add(DocumentsContract.Root.ColumnMimeTypes, "*/*"); // 支持的MIME类型：所有类型

            if (BaseDir.Exists())
            {
                rowBuilder.Add(DocumentsContract.Root.ColumnAvailableBytes, BaseDir.FreeSpace);
            }

            return result;
        }
        /// <summary>
        /// 查询指定文档信息
        /// </summary>
        /// <param name="documentId"></param>
        /// <param name="projection"></param>
        /// <returns></returns>
        public override ICursor QueryDocument(string? documentId, string[]? projection)
        {
            MatrixCursor result = new(projection ?? DefaultDocumentProjection);
            if (documentId == null)
            {
                return result;
            }

            IncludeFile(result, documentId, null!); // 添加指定文档信息
            return result;
        }

        /// <summary>
        /// 列出指定父文档的子文档
        /// </summary>
        /// <param name="parentDocumentId"></param>
        /// <param name="projection"></param>
        /// <param name="sortOrder"></param>
        /// <returns></returns>
        public override ICursor QueryChildDocuments(string? parentDocumentId, string[]? projection, string? sortOrder)
        {
            MatrixCursor result = new(projection ?? DefaultDocumentProjection);
            if (parentDocumentId == null)
            {
                return result;
            }
            Java.IO.File parent = GetFileForDocId(parentDocumentId);

            if (parent.IsDirectory && parent.CanRead())
            {
                Java.IO.File[]? files = parent.ListFiles();
                if (files == null)
                {
                    return result;
                }
                foreach (Java.IO.File file in files)
                {
                    IncludeFile(result, null!, file);
                }
            }

            return result;
        }

        /// <summary>
        /// 打开指定文档，供其它应用访问
        /// </summary>
        /// <param name="documentId"></param>
        /// <param name="mode"></param>
        /// <param name="signal"></param>
        /// <returns></returns>
        /// <exception cref="Java.IO.FileNotFoundException"></exception>
        public override ParcelFileDescriptor OpenDocument(string? documentId, string? mode, CancellationSignal? signal)
        {
            if (documentId == null || mode == null)
            {
                Log.Error("Document ID 或 mode 为 null");
                throw new Java.IO.FileNotFoundException("Document ID 或 mode 为 null");
            }
            Java.IO.File file = GetFileForDocId(documentId);
            ParcelFileMode accessMode = ParcelFileDescriptor.ParseMode(mode);
            return ParcelFileDescriptor.Open(file, accessMode) ?? throw new Java.IO.FileNotFoundException("找不到指定文件");
        }

        private string GetDocIdForFile(Java.IO.File file)
        {
            var path = file.AbsolutePath;
            var rootPath = BaseDir.AbsolutePath;

            if (path.Equals(rootPath))
            {
                return RootId;
            }

            if (path.StartsWith(rootPath))
            {
                var relativePath = path.Substring(rootPath.Length);
                if (relativePath.StartsWith("/"))
                {
                    relativePath = relativePath.Substring(1);
                }
                return $"{RootId}/{relativePath}";
            }

            throw new Java.IO.FileNotFoundException($"File {path} is outside root directory");
        }

        private Java.IO.File GetFileForDocId(string docId)
        {
            if (docId.Equals(RootId))
            {
                return BaseDir;
            }

            if (docId.StartsWith($"{RootId}/"))
            {
                var path = docId.Substring($"{RootId}/".Length);
                return new Java.IO.File(BaseDir, path);
            }

            throw new Java.IO.FileNotFoundException($"Invalid document ID: {docId}");
        }

        /// <summary>
        /// 填充文件信息到结果集中
        /// </summary>
        /// <param name="result">返回的结果集</param>
        /// <param name="docId"></param>
        /// <param name="file"></param>
        private void IncludeFile(MatrixCursor result, string docId, Java.IO.File file)
        {
            if (docId == null)
            {
                docId = GetDocIdForFile(file);
            }
            else
            {
                file = GetFileForDocId(docId);
            }

            int flags = 0;

            if (file.IsDirectory) // 如果是目录
            {
                if (file.CanWrite())
                {
                    flags |= (int)DocumentContractFlags.DirSupportsCreate;
                }
            }
            else if (file.CanWrite()) // 如果是文件且可写
            {
                flags |= (int)DocumentContractFlags.SupportsWrite;
                flags |= (int)DocumentContractFlags.SupportsDelete;
            }

            MatrixCursor.RowBuilder? rowBuilder = result.NewRow();
            if (rowBuilder == null)
            {
                return;
            }
            rowBuilder.Add(DocumentsContract.Document.ColumnDocumentId, docId);
            rowBuilder.Add(DocumentsContract.Document.ColumnDisplayName, file.Name);
            rowBuilder.Add(DocumentsContract.Document.ColumnSize, file.Length());
            rowBuilder.Add(DocumentsContract.Document.ColumnMimeType, GetTypeForFile(file));
            rowBuilder.Add(DocumentsContract.Document.ColumnLastModified, file.LastModified());
            rowBuilder.Add(DocumentsContract.Document.ColumnFlags, flags);
        }

        private static string GetTypeForFile(Java.IO.File file)
        {
            if (file.IsDirectory)
            {
                return DocumentsContract.Document.MimeTypeDir;
            }

            string name = file.Name;
            int lastDotIndex = name.LastIndexOf('.'); // 找到最后一个点的位置
            if (lastDotIndex >= 0)
            {
                string? extension = name[(lastDotIndex + 1)..].ToLower();
                string? mime = MimeTypeMap.Singleton?.GetMimeTypeFromExtension(extension);
                if (mime != null)
                {
                    return mime;
                }
            }

            return "application/octet-stream";
        }
    }
}