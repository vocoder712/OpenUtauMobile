using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace OpenUtauMobile.Services.VideoExport;

/// <summary>
/// 桌面端后备编码器：把原始 BGRA 帧通过管道交给 PATH 里的 ffmpeg 编码为 H.264 + AAC。
/// 移动端不使用它；Android 由平台层注册 MediaCodec 实现。
/// </summary>
public sealed class FfmpegPipeVideoEncoder : IPianoRollVideoEncoder
{
    private Process? _process;
    private Stream? _stdin;
    private int _width;
    private readonly StringBuilder _stderr = new();

    /// <summary>探测 PATH 中是否存在可执行的 ffmpeg。</summary>
    public static bool IsFfmpegAvailable()
    {
        try
        {
            using Process? probe = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (probe == null) return false;
            probe.WaitForExit(3000);
            return probe.HasExited && probe.ExitCode == 0;
        }
        catch
        {
            // 没有该可执行文件、或平台不允许启动进程。
            return false;
        }
    }

    /// <summary>ffmpeg 可用时创建编码器，否则返回空。</summary>
    public static FfmpegPipeVideoEncoder? TryCreate() => IsFfmpegAvailable() ? new FfmpegPipeVideoEncoder() : null;

    /// <inheritdoc />
    public void Start(string outputPath, int width, int height, int fps, int bitrateKbps,
        string? audioWavPath, double audioStartMs, double durationMs)
    {
        _width = width;
        ProcessStartInfo startInfo = new("ffmpeg")
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string arg in (string[])["-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "bgra",
                     "-s", $"{width}x{height}", "-r", fps.ToString(CultureInfo.InvariantCulture), "-i", "-"])
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (audioWavPath != null)
        {
            // 视频第 0 帧对应混音的 audioStartMs，用输入侧 -ss 对齐。
            startInfo.ArgumentList.Add("-ss");
            startInfo.ArgumentList.Add((audioStartMs / 1000.0).ToString("F3", CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("-i");
            startInfo.ArgumentList.Add(audioWavPath);
        }

        foreach (string arg in (string[])["-c:v", "libx264", "-pix_fmt", "yuv420p", "-preset", "veryfast",
                     "-b:v", $"{bitrateKbps}k"])
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (audioWavPath != null)
        {
            foreach (string arg in (string[])["-c:a", "aac", "-b:a", "192k", "-shortest"])
            {
                startInfo.ArgumentList.Add(arg);
            }
        }

        // 输出时长以视频为准，音频过长时被截断。
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add((durationMs / 1000.0).ToString("F3", CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-movflags");
        startInfo.ArgumentList.Add("+faststart");
        startInfo.ArgumentList.Add(outputPath);

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法启动 ffmpeg");
        // 必须持续读取 stderr，否则缓冲区写满会让 ffmpeg 阻塞。
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lock (_stderr)
            {
                _stderr.AppendLine(e.Data);
                if (_stderr.Length > 4096) _stderr.Remove(0, _stderr.Length - 4096);
            }
        };
        _process.BeginErrorReadLine();
        _stdin = _process.StandardInput.BaseStream;
    }

    /// <inheritdoc />
    public void WriteFrame(ReadOnlySpan<byte> pixels, int rowBytes)
    {
        if (_stdin == null) throw new InvalidOperationException("编码器尚未开始");
        int packed = _width * 4;
        if (rowBytes == packed)
        {
            _stdin.Write(pixels);
            return;
        }

        // 行有对齐填充时逐行去掉填充。
        for (int offset = 0; offset + packed <= pixels.Length; offset += rowBytes)
        {
            _stdin.Write(pixels.Slice(offset, packed));
        }
    }

    /// <inheritdoc />
    public void Finish()
    {
        if (_process == null || _stdin == null) return;
        _stdin.Flush();
        _stdin.Close();
        _process.WaitForExit();
        if (_process.ExitCode != 0)
        {
            string tail;
            lock (_stderr) tail = _stderr.ToString();
            throw new InvalidOperationException($"ffmpeg 编码失败（退出码 {_process.ExitCode}）：{tail}");
        }
    }

    /// <inheritdoc />
    public void Abort()
    {
        try
        {
            if (_process is { HasExited: false }) _process.Kill();
        }
        catch
        {
            // 进程可能已经退出。
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stdin?.Dispose();
        _process?.Dispose();
    }
}
