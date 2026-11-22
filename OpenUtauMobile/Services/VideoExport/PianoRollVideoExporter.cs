using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Serilog;
using SkiaSharp;

namespace OpenUtauMobile.Services.VideoExport;

/// <summary>
/// 钢琴卷帘视频导出流程：建立场景快照（含直接计算的音高线与和声层）→ 混音 → 离线逐帧渲染 → 交给平台编码器。
/// 渲染按“第 n 帧对应 n/fps 秒”的确定性时间推进（时间轴为毫秒），与手机当时的卡顿无关，所以不是屏幕录制而是离线合成。
/// </summary>
public static class PianoRollVideoExporter
{
    /// <summary>当前平台是否有可用的视频编码器。</summary>
    public static bool IsAvailable => ServiceHub.PianoRollVideoEncoderFactory != null || FfmpegPipeVideoEncoder.IsFfmpegAvailable();

    /// <summary>导出视频。必须在 UI 线程调用，因为场景快照要读取工程对象；耗时部分会自行切到后台线程。</summary>
    /// <param name="project">当前工程。</param>
    /// <param name="part">要录制的人声分片。</param>
    /// <param name="options">导出参数。</param>
    /// <param name="outputPath">目标 mp4 路径。</param>
    /// <param name="progress">进度回调，0 到 1。</param>
    /// <param name="ct">取消令牌，取消后会清理未完成的文件。</param>
    public static async Task ExportAsync(UProject project, UVoicePart part, PianoRollVideoOptions options,
        string outputPath, IProgress<double>? progress, CancellationToken ct)
    {
        options = options.Normalized();
        IPianoRollVideoEncoder encoder = ServiceHub.PianoRollVideoEncoderFactory?.Invoke()
            ?? FfmpegPipeVideoEncoder.TryCreate()
            ?? throw new NotSupportedException("当前平台没有可用的视频编码器");
        string? wavPath = null;
        try
        {
            progress?.Report(0.01);
            PianoRollVideoScene scene = PianoRollVideoSceneBuilder.Build(project, part, options.ShowPortrait);

            if (options.IncludeAudio)
            {
                wavPath = Path.Combine(Path.GetTempPath(), $"pianoroll_mix_{Guid.NewGuid():N}.wav");
                await PlaybackManager.Inst.RenderMixdown(project, wavPath);
                // 混音失败时退化为无声视频，而不是让整次导出失败。
                if (!File.Exists(wavPath))
                {
                    Log.Warning("钢琴卷帘视频：混音未生成文件，导出无声视频");
                    wavPath = null;
                }
            }

            progress?.Report(0.15);
            string? audio = wavPath;
            await Task.Run(() => RenderFrames(scene, options, encoder, outputPath, audio, progress, ct), ct);
            progress?.Report(1);
        }
        catch
        {
            encoder.Abort();
            TryDelete(outputPath);
            throw;
        }
        finally
        {
            encoder.Dispose();
            if (wavPath != null) TryDelete(wavPath);
        }
    }

    private static void RenderFrames(PianoRollVideoScene scene, PianoRollVideoOptions options,
        IPianoRollVideoEncoder encoder, string outputPath, string? wavPath, IProgress<double>? progress, CancellationToken ct)
    {
        using PianoRollVideoRenderer renderer = new(scene, options);
        SKImageInfo info = new(renderer.Width, renderer.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        double startMs = scene.StartMs;
        int frameCount = Math.Max(1, (int)Math.Ceiling((scene.EndMs - startMs) / 1000.0 * options.Fps));
        double dt = 1.0 / options.Fps;
        // 视频实际时长；音频会被截到这个长度。
        double durationMs = frameCount * 1000.0 / options.Fps;

        encoder.Start(outputPath, renderer.Width, renderer.Height, options.Fps, options.BitrateKbps, wavPath, startMs,
            durationMs);
        for (int frame = 0; frame < frameCount; frame++)
        {
            ct.ThrowIfCancellationRequested();
            double ms = startMs + frame * 1000.0 / options.Fps;
            surface.Canvas.Clear(SKColors.Black);
            renderer.Render(surface.Canvas, ms, dt);
            using SKPixmap pixmap = surface.PeekPixels();
            encoder.WriteFrame(pixmap.GetPixelSpan(), pixmap.RowBytes);
            if (frame % 8 == 0) progress?.Report(0.15 + 0.83 * (frame + 1) / frameCount);
        }

        encoder.Finish();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "钢琴卷帘视频：清理临时文件失败 {Path}", path);
        }
    }
}
