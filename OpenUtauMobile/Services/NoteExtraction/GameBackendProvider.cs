using System;
using System.IO;
using System.Linq;
using OpenUtau.Core;
using OpenUtau.Core.Analysis;
using OpenUtauMobile.Helpers;
using Serilog;

namespace OpenUtauMobile.Services.NoteExtraction;

internal enum NoteExtractionBackend { Onnx, Ggml }

internal sealed record GameDependency(string PackageId, string? Path, GameConfig Config)
{
    public bool IsInstalled => Path != null;
    public bool SupportsLanguage(string? language) => language == null || Config.Languages?.ContainsKey(language) == true;
}

internal static class GameBackendProvider
{
    public static bool IsSupported => !OperatingSystem.IsBrowser() && !OperatingSystem.IsIOS();

    // 弹窗只读取模型文件与配置，不在 UI 线程创建推理会话。
    public static GameDependency Inspect(NoteExtractionBackend choice)
    {
        string packageId = choice == NoteExtractionBackend.Onnx ? "game" : "game-ggml-medium";
        try
        {
            string? directory = PackageManager.Inst.GetInstalledPath(packageId);
            if (choice == NoteExtractionBackend.Onnx)
            {
                return directory != null && GameOnnxBackend.IsInstalled(directory)
                    ? new(packageId, directory, Game.LoadConfig(directory))
                    : new(packageId, null, new());
            }
            bool legacy = directory == null;
            if (legacy)
            {
                directory = PackageManager.Inst.GetInstalledPath("game");
            }
            string[] models = directory == null ? [] : Directory.GetFiles(directory, "*.gguf", SearchOption.AllDirectories);
            if (models.Length != 1) return new(packageId, null, new());
            if (legacy) packageId = "game";
            string modelDirectory = System.IO.Path.GetDirectoryName(models[0])!;
            GameConfig config = File.Exists(System.IO.Path.Combine(modelDirectory, "config.json"))
                ? Game.LoadConfig(modelDirectory) : new();
            return new(packageId, models.Single(), config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log.Warning(ex, "Cannot read GAME dependency {PackageId}", packageId);
            return new(packageId, null, new());
        }
    }

    public static IGameBackend Create(NoteExtractionBackend choice)
    {
        if (!IsSupported) throw new PlatformNotSupportedException(L.S("NoteExtraction.Unsupported"));
        GameDependency dependency = Inspect(choice);
        if (!dependency.IsInstalled)
        {
            throw new InvalidOperationException(L.S(choice == NoteExtractionBackend.Onnx
                ? "NoteExtraction.MissingOnnx" : "NoteExtraction.MissingGgml"));
        }
        return choice == NoteExtractionBackend.Onnx
            ? new GameOnnxBackend(dependency.Config, dependency.Path!)
            : new NativeGameBackend(dependency.Path!);
    }
}
