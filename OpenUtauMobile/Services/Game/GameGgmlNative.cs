using System;
using System.Runtime.InteropServices;

namespace OpenUtauMobile.Services.Game;

/// <summary>
/// 原生 game_ggml_shared 的 P/Invoke 声明（.NET 10 LibraryImport 源生成，AOT-safe）。
///
/// 与 native/shim/game_capi.h 的 9 个导出一一对应。库名用通用名 "game_ggml_shared"，
/// 运行时按平台解析：Android => libgame_ggml_shared.so；Windows => game_ggml_shared.dll。
///
/// 注意点：
///   * 字符串参数统一 UTF-8，与原生 const char* 一致。
///   * 波形通过 float[] 传数组指针（blittable，不拷贝）。
///   * 音符输出缓冲区手动 Marshal.Alloc/Fill（原生语义 = 调用方提供容量，
///     原生回填 notes_count 个；源生成器不支持 [Out] 自增变长数组，故此处手写，
///     更贴近"调用方分配、调用方读取"的 C ABI 契约）。
/// </summary>
public static partial class GameGgmlNative
{
    private const string LibraryName = "game_ggml_shared";

    /// <summary>写产物版本号到 buf。返回长度。</summary>
    [LibraryImport(LibraryName, EntryPoint = "game_capi_version")]
    internal static partial int Version(byte[] buf, int cap);

    /// <summary>写 ggml 版本号到 buf。返回长度。</summary>
    [LibraryImport(LibraryName, EntryPoint = "game_capi_ggml_version")]
    internal static partial int GgmlVersion(byte[] buf, int cap);

    /// <summary>写可用后端（逗号分隔小写）到 buf。返回长度。</summary>
    [LibraryImport(LibraryName, EntryPoint = "game_capi_available_backends")]
    internal static partial int AvailableBackends(byte[] buf, int cap);

    /// <summary>打开 GGUF 模型。返回句柄；失败返回 IntPtr.Zero 并把错误写入 errbuf。</summary>
    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8,
        EntryPoint = "game_capi_open")]
    internal static partial IntPtr Open(
        string ggufPath, string? configJson,
        byte[] errbuf, int errcap);

    /// <summary>关闭并释放句柄。NULL 安全。</summary>
    [LibraryImport(LibraryName, EntryPoint = "game_capi_close")]
    internal static partial void Close(IntPtr model);

    /// <summary>写运行时实际选择的后端名到 buf（诊断/UI 展示用）。</summary>
    [LibraryImport(LibraryName, EntryPoint = "game_capi_backend_decided")]
    internal static partial int BackendDecided(IntPtr model, byte[] buf, int cap);

    /// <summary>执行推理。返回错误码（0 = 成功），notes_count/num_frames 回填。</summary>
    [LibraryImport(LibraryName, EntryPoint = "game_capi_infer")]
    internal static partial int Infer(
        IntPtr model,
        float[] waveform,
        int n,
        int language,
        int nsteps,
        float segThreshold,
        int segRadius,
        float estThreshold,
        ulong seed,
        IntPtr notesOut,
        int notesCapacity,
        out int notesCount,
        out int numFrames);

    /// <summary>把语言代码映射为 id；未知名返回 -1。</summary>
    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8,
        EntryPoint = "game_capi_language_id")]
    internal static partial int LanguageId(IntPtr model, string langCode);

    /// <summary>最近一次失败的详细消息。可为 IntPtr.Zero。</summary>
    [LibraryImport(LibraryName, EntryPoint = "game_capi_last_error")]
    internal static partial IntPtr LastError(IntPtr model);
}
