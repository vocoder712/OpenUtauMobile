using System;
using System.IO;
using System.Linq;

namespace OpenUtauMobile.Services.Game;

/// <summary>
/// 模型文件的解析与物化：把 EmbeddedResource 里的 game_medium.gguf
/// 首次运行时解包到平台可写目录（Android = app 私有 Files；桌面 = %LOCALAPPDATA%），
/// 之后直接引用该路径（原生层 ggml 用 FILE* 读路径，无法直接读流/Asset，
/// 必须先落到磁盘）。
/// </summary>
public static class GameModelResolver
{
    private const string ModelResourceName = "OpenUtauMobile.Models.game_medium.gguf";

    /// <summary>模型在运行时的相对子目录名。</summary>
    public const string ModelSubDir = "game";

    /// <summary>模型文件名（与资源、csproj 引用一致）。</summary>
    public const string ModelFileName = "game_medium.gguf";

    /// <summary>
    /// 返回模型实际磁盘路径（不存在则从嵌入式资源解包）。
    /// 抛出 IOException/InvalidOperationException 表示无法提供模型。
    /// </summary>
    public static string EnsureModelPath()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string modelDir = Path.Combine(baseDir, ModelSubDir);
        string modelPath = Path.Combine(modelDir, ModelFileName);

        if (File.Exists(modelPath))
        {
            return modelPath;
        }

        ExtractEmbeddedModel(modelDir, modelPath);
        return modelPath;
    }

    /// <summary>可选：外部已经有一个 .gguf 文件路径（例如用户下载），直接使用它。</summary>
    public static bool TryResolveExistingModel(string? externalPath, out string resolvedPath)
    {
        if (!string.IsNullOrEmpty(externalPath) && File.Exists(externalPath))
        {
            resolvedPath = externalPath;
            return true;
        }

        resolvedPath = string.Empty;
        return false;
    }

    private static void ExtractEmbeddedModel(string modelDir, string targetPath)
    {
        using (System.IO.Stream? stream = typeof(GameModelResolver).Assembly
                   .GetManifestResourceStream(ModelResourceName))
        {
            if (stream == null)
            {
                throw new InvalidOperationException(
                    $"Embedded model resource not found: {ModelResourceName}");
            }

            Directory.CreateDirectory(modelDir);

            // 先写临时文件再原子替换，避免中途崩溃留下半截模型。
            string tmpPath = targetPath + ".tmp";
            using (FileStream output = File.Create(tmpPath))
            {
                stream.CopyTo(output);
            }

            File.Move(tmpPath, targetPath, overwrite: true);
        }
    }
}
