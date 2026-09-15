using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using OpenUtau.Core.Analysis;
using OpenUtauMobile.Helpers;

namespace OpenUtauMobile.Services.NoteExtraction;

// 模型只在后台工作线程创建、调用和释放；取消只设置标志，不并发销毁原生句柄。
internal sealed class NativeGameBackend(string modelPath) : IGameBackend
{
    private GameModelHandle? handle;
    private volatile bool stopping;
    public string Name => "GGML";
    public GameConfig Config { get; } = new();

    public bool EnsureLoaded()
    {
        if (stopping) throw new OperationCanceledException();
        if (handle != null) return true;
        try
        {
            byte[] error = new byte[1024];
            IntPtr pointer = GameNative.Open(modelPath, error, error.Length);
            if (pointer == IntPtr.Zero)
            {
                int end = Array.IndexOf(error, (byte)0);
                throw new InvalidOperationException(Encoding.UTF8.GetString(error, 0, end < 0 ? error.Length : end));
            }
            handle = new GameModelHandle(pointer);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new InvalidOperationException(L.S("NoteExtraction.MissingNative"), ex);
        }
        if (stopping) throw new OperationCanceledException();
        return true;
    }

    public List<TranscribedNote> RunInference(float[] samples, GameOptions options)
    {
        EnsureLoaded();
        int code = GameNative.Infer(handle!, samples, (nuint)samples.Length,
            options.LanguageCode ?? "", options.SamplingSteps, options.BoundaryThreshold,
            options.BoundaryRadius, options.ScoreThreshold, options.Seed, out IntPtr data, out int count);
        if (stopping) throw new OperationCanceledException();
        if (code != 0)
        {
            throw new InvalidOperationException(Marshal.PtrToStringUTF8(GameNative.LastError(handle!)));
        }
        if (count < 0 || (count > 0 && data == IntPtr.Zero))
        {
            throw new InvalidOperationException("Invalid GAME native result.");
        }

        List<TranscribedNote> notes = new(count);
        double cursor = 0;
        for (int i = 0; i < count; i++)
        {
            NativeNote note = Marshal.PtrToStructure<NativeNote>(IntPtr.Add(data, checked(i * Marshal.SizeOf<NativeNote>())));
            if (!float.IsFinite(note.Offset) || !float.IsFinite(note.Duration) || !float.IsFinite(note.Pitch)
                || note.Offset < cursor - 0.001 || note.Duration < 0)
            {
                throw new InvalidOperationException("Invalid GAME note timing.");
            }
            if (note.Offset > cursor)
            {
                notes.Add(new TranscribedNote((float)(note.Offset - cursor), 0, false));
            }
            notes.Add(new TranscribedNote(note.Duration, note.Pitch, note.Voiced != 0));
            cursor = note.Offset + note.Duration;
        }
        return notes;
    }

    public void Interrupt() => stopping = true;
    public void Dispose() => handle?.Dispose();
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeNote
{
    public float Offset;
    public float Duration;
    public float Pitch;
    public int Voiced;
}

internal sealed class GameModelHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public GameModelHandle(IntPtr value) : base(true) => SetHandle(value);
    protected override bool ReleaseHandle()
    {
        GameNative.Close(handle);
        return true;
    }
}

internal static partial class GameNative
{
    private const string Library = "opum_game";

    [LibraryImport(Library, EntryPoint = "opum_game_open", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial IntPtr Open(string path, [Out] byte[] error, int capacity);

    [LibraryImport(Library, EntryPoint = "opum_game_close")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void Close(IntPtr model);

    [LibraryImport(Library, EntryPoint = "opum_game_infer", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial int Infer(GameModelHandle model, float[] samples, nuint count,
        string language, int steps, float boundary, int radius, float score, ulong seed,
        out IntPtr notes, out int noteCount);

    [LibraryImport(Library, EntryPoint = "opum_game_error")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial IntPtr LastError(GameModelHandle model);
}
