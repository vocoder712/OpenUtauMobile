using System;

namespace OpenUtauMobile.Services.VideoExport;

/// <summary>
/// 视频编码器抽象。导出流程在后台线程中按 Start → WriteFrame(多次) → Finish 的顺序调用。
/// 平台实现负责把原始帧编码为 H.264，并在提供音频时封装为 AAC。
/// </summary>
public interface IPianoRollVideoEncoder : IDisposable
{
    /// <summary>开始编码。</summary>
    /// <param name="outputPath">最终 mp4 路径。</param>
    /// <param name="width">画面宽度，偶数。</param>
    /// <param name="height">画面高度，偶数。</param>
    /// <param name="fps">帧率。</param>
    /// <param name="bitrateKbps">视频码率。</param>
    /// <param name="audioWavPath">待封装的 wav 混音路径，为空表示不含音频。</param>
    /// <param name="audioStartMs">音频起点偏移，视频第 0 帧对应混音的这一时刻。</param>
    /// <param name="durationMs">视频总时长；音频会被截断到这个长度，避免音轨比画面长。</param>
    void Start(string outputPath, int width, int height, int fps, int bitrateKbps,
        string? audioWavPath, double audioStartMs, double durationMs);

    /// <summary>写入一帧预乘 BGRA8888 像素，调用方保证帧按时间顺序提交。</summary>
    /// <param name="pixels">像素数据。</param>
    /// <param name="rowBytes">每行字节数，可能大于 width * 4。</param>
    void WriteFrame(ReadOnlySpan<byte> pixels, int rowBytes);

    /// <summary>结束编码并写出文件。</summary>
    void Finish();

    /// <summary>放弃编码并清理临时文件；调用后对象不可再用。</summary>
    void Abort();
}
