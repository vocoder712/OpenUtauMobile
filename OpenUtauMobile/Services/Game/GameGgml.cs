using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace OpenUtauMobile.Services.Game;

/// <summary>
/// GAME 原生后端（libgame_ggml_shared）的托管封装。
///
/// 职责：持有模型句柄，把托管参数转换成 C ABI 调用，并回读音符结果。
/// 设计要点：
///   * 与 Core.Analysis.Game 平行的替换实现（不修改 OpenUtau.Core）。
///   * 单句柄串行调用（原生 Model::infer 非线程安全），上层需保证同一实例不被并发 infer。
///   * IDisposable 释放原生句柄。
/// </summary>
public sealed class GameGgml : IDisposable
{
    // 音符回读缓冲区的最大容量（原生按此上限写入，超出的部分被截断）。
    // 转写整段语音的音符数一般只有几十；保留足够余量。
    private const int MaxNotesBufferCapacity = 4096;

    private IntPtr nativeHandle;
    private bool disposed;

    /// <summary>此实例加载的模型路径。</summary>
    public string ModelPath { get; }

    private GameGgml(IntPtr handle, string modelPath)
    {
        this.nativeHandle = handle;
        this.ModelPath = modelPath;
    }

    /// <summary>
    /// 打开模型（自动解析嵌入式模型到磁盘路径）。
    /// 失败时返回 null 并在 error 中给出原因。
    /// </summary>
    public static GameGgml? Open(
        out string error,
        string? modelPathOverride = null,
        string? configJson = null)
    {
        error = string.Empty;
        string modelPath;
        if (!GameModelResolver.TryResolveExistingModel(modelPathOverride, out modelPath))
        {
            try
            {
                modelPath = GameModelResolver.EnsureModelPath();
            }
            catch (Exception ex)
            {
                error = $"模型解析失败: {ex.Message}";
                return null;
            }
        }

        if (!TryOpen(modelPath, configJson, out GameGgml? instance, out error))
        {
            return null;
        }

        return instance;
    }

    /// <summary>已确定路径时打开模型。</summary>
    public static bool TryOpen(
        string modelPath, string? configJson,
        out GameGgml? instance, out string error)
    {
        instance = null;
        error = string.Empty;

        byte[] errbuf = new byte[GameCapiErrorBufferSize];
        IntPtr handle = GameGgmlNative.Open(modelPath, configJson, errbuf, errbuf.Length);
        if (handle == IntPtr.Zero)
        {
            error = ReadUtf8Buffer(errbuf) ?? "模型打开失败";
            return false;
        }

        instance = new GameGgml(handle, modelPath);
        return true;
    }

    /// <summary>原生错误码（与 game_capi.h 常量一致）。</summary>
    public const int Ok = 0;
    public const int ErrHandle = -1;
    public const int ErrInit = -2;
    public const int ErrInfer = -3;
    public const int ErrInvalidArg = -4;

    // 缓冲区约定：GAME_CAPI_ERRBUF = 512（各 buf 均按其最大容量分配）。

    /// <summary>GAME 错误缓冲容量。</summary>
    private const int GameCapiErrorBufferSize = 512;

    /// <summary>版本号。</summary>
    public static string? VersionString()
    {
        return CallStringBuffer(GameGgmlNative.Version);
    }

    /// <summary>ggml 版本号。</summary>
    public static string? GgmlVersionString()
    {
        return CallStringBuffer(GameGgmlNative.GgmlVersion);
    }

    private delegate int StringBufferWriter(byte[] buf, int cap);

    private static string? CallStringBuffer(StringBufferWriter writer)
    {
        byte[] buffer = new byte[GameCapiErrorBufferSize];
        int length = writer(buffer, buffer.Length);
        if (length <= 0)
        {
            return null;
        }

        int valid = Math.Min(length - 1, buffer.Length);
        // 缓冲可能未写到末尾：以首个 NUL 为界。
        int end = Array.IndexOf(buffer, (byte)0, 0, valid);
        if (end < 0)
        {
            end = valid;
        }

        return Encoding.UTF8.GetString(buffer, 0, end);
    }

    /// <summary>编译期可用后端列表。</summary>
    public static string? AvailableBackendsString()
    {
        return CallStringBuffer(GameGgmlNative.AvailableBackends);
    }

    /// <summary>运行时实际选择的后端名（GPU 首选，fallback 后反映真实后端）。</summary>
    public string? BackendDecidedString()
    {
        EnsureNotDisposed();
        return CallStringBufferWithHandle(nativeHandle, GameGgmlNative.BackendDecided);
    }

    private delegate int HandleStringBufferWriter(IntPtr handle, byte[] buf, int cap);

    private static string? CallStringBufferWithHandle(
        IntPtr handle, HandleStringBufferWriter writer)
    {
        byte[] buffer = new byte[GameCapiErrorBufferSize];
        int length = writer(handle, buffer, buffer.Length);
        if (length <= 0)
        {
            return null;
        }

        int valid = Math.Min(length - 1, buffer.Length);
        int end = Array.IndexOf(buffer, (byte)0, 0, valid);
        if (end < 0)
        {
            end = valid;
        }

        return Encoding.UTF8.GetString(buffer, 0, end);
    }

    /// <summary>把语言代码映射为 id；未知名返回 -1。可直接按 Core 默认传 0 处理。</summary>
    public int LanguageId(string langCode)
    {
        EnsureNotDisposed();
        return GameGgmlNative.LanguageId(nativeHandle, langCode);
    }

    /// <summary>最近一次失败详情。</summary>
    public string? LastErrorString()
    {
        IntPtr ptr = GameGgmlNative.LastError(nativeHandle);
        if (ptr == IntPtr.Zero)
        {
            return null;
        }

        return Marshal.PtrToStringUTF8(ptr);
    }

    /// <summary>
    /// 端到端转写：输入 44100Hz 单声道 float 波形 [-1,1]，返回音符列表。
    /// 抛 ArgumentException/FailedInference 表示调用/推理失败。
    /// </summary>
    public IReadOnlyList<GameGgmlNote> Infer(float[] waveform, GameGgmlOptions options)
    {
        EnsureNotDisposed();
        if (waveform == null || waveform.Length == 0)
        {
            throw new ArgumentException("waveform 不能为空", nameof(waveform));
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        int language = options.LanguageCode is null or ""
            ? 0
            : LanguageId(options.LanguageCode);
        // 未知名语言回落 universal（0）。
        if (language < 0)
        {
            language = 0;
        }

        // 分配音符回读缓冲（capacity 固定）。
        int noteStructSize = Marshal.SizeOf<GameGgmlNote>();
        IntPtr notesBuffer = Marshal.AllocHGlobal(noteStructSize * MaxNotesBufferCapacity);
        try
        {
            int code = GameGgmlNative.Infer(
                nativeHandle,
                waveform,
                waveform.Length,
                language,
                options.SamplingSteps,
                options.BoundaryThreshold,
                options.BoundaryRadius,
                options.ScoreThreshold,
                options.Seed,
                notesBuffer,
                MaxNotesBufferCapacity,
                out int notesCount,
                out int numFrames);

            if (code == ErrInvalidArg)
            {
                throw new ArgumentException(LastErrorString() ?? "参数非法");
            }

            if (code != Ok)
            {
                string msg = $"推理失败 (错误码 {code}): {LastErrorString() ?? "未知"}";
                throw new InvalidOperationException(msg);
            }

            return ReadNotesOut(notesBuffer, notesCount);
        }
        finally
        {
            Marshal.FreeHGlobal(notesBuffer);
        }
    }

    private static List<GameGgmlNote> ReadNotesOut(IntPtr buffer, int count)
    {
        int size = Marshal.SizeOf<GameGgmlNote>();
        var notes = new List<GameGgmlNote>(count);
        for (int i = 0; i < count; i++)
        {
            IntPtr item = IntPtr.Add(buffer, i * size);
            notes.Add(Marshal.PtrToStructure<GameGgmlNote>(item));
        }

        return notes;
    }

    private void EnsureNotDisposed()
    {
        if (this.disposed)
        {
            throw new ObjectDisposedException(nameof(GameGgml));
        }
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        if (this.nativeHandle != IntPtr.Zero)
        {
            GameGgmlNative.Close(this.nativeHandle);
            this.nativeHandle = IntPtr.Zero;
        }
    }

    private static string ReadUtf8Buffer(byte[] buffer)
    {
        int end = Array.IndexOf(buffer, (byte)0);
        if (end < 0)
        {
            end = buffer.Length;
        }

        return Encoding.UTF8.GetString(buffer, 0, end);
    }
}
