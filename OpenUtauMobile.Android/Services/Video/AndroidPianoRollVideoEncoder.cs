using System;
using System.IO;
using System.Threading.Tasks;
using Android.Media;
using Android.Runtime;
using Java.Nio;
using OpenUtauMobile.Services.VideoExport;
using Serilog;

namespace OpenUtauMobile.Android.Services.Video;

/// <summary>
/// 基于 MediaCodec / MediaMuxer 的钢琴卷帘视频编码器。
/// <para>
/// 为了让离线渲染拥有确定的时间戳，视频走 ByteBuffer 输入（每帧显式给 PTS），而不是 Surface 输入。
/// 流程分三段：视频写入临时 mp4；有音频时另编码为临时 m4a；最后用 MediaExtractor 无重编码地封装成最终文件。
/// </para>
/// </summary>
internal sealed class AndroidPianoRollVideoEncoder : IPianoRollVideoEncoder
{
    // 与 MediaFormat.MIMETYPE_VIDEO_AVC / MIMETYPE_AUDIO_AAC 相同。
    private const string VideoMime = "video/avc";
    private const string AudioMime = "audio/mp4a-latm";

    // COLOR_FormatYUV420Flexible，配合 GetInputImage 适配不同芯片的平面布局。
    private const int ColorFormatYuv420Flexible = 0x7F420888;
    private const long TimeoutUs = 10_000;

    // DequeueOutputBuffer 的特殊返回值：INFO_TRY_AGAIN_LATER / INFO_OUTPUT_FORMAT_CHANGED。
    private const int InfoTryAgainLater = -1;
    private const int InfoOutputFormatChanged = -2;

    private MediaCodec? _codec;
    private MediaMuxer? _muxer;
    private int _videoTrack = -1;
    private bool _muxerStarted;
    private string _outputPath = string.Empty;
    private string _videoTempPath = string.Empty;
    private string _audioTempPath = string.Empty;
    private string? _audioWavPath;
    private double _audioStartMs;
    private double _durationMs;
    private int _width;
    private int _height;
    private int _fps;
    private long _frameIndex;

    /// <inheritdoc />
    public void Start(string outputPath, int width, int height, int fps, int bitrateKbps,
        string? audioWavPath, double audioStartMs, double durationMs)
    {
        _outputPath = outputPath;
        _durationMs = durationMs;
        _width = width;
        _height = height;
        _fps = fps;
        _audioWavPath = audioWavPath;
        _audioStartMs = audioStartMs;
        string id = Guid.NewGuid().ToString("N");
        _videoTempPath = Path.Combine(Path.GetTempPath(), $"pianoroll_v_{id}.mp4");
        _audioTempPath = Path.Combine(Path.GetTempPath(), $"pianoroll_a_{id}.m4a");

        MediaFormat format = MediaFormat.CreateVideoFormat(VideoMime, width, height)!;
        format.SetInteger(MediaFormat.KeyColorFormat, ColorFormatYuv420Flexible);
        format.SetInteger(MediaFormat.KeyBitRate, bitrateKbps * 1000);
        format.SetInteger(MediaFormat.KeyFrameRate, fps);
        format.SetInteger(MediaFormat.KeyIFrameInterval, 1);
        _codec = MediaCodec.CreateEncoderByType(VideoMime)!;
        _codec.Configure(format, null, null, MediaCodecConfigFlags.Encode);
        _codec.Start();
        _muxer = new MediaMuxer(_videoTempPath, MuxerOutputType.Mpeg4);
    }

    /// <inheritdoc />
    public void WriteFrame(ReadOnlySpan<byte> pixels, int rowBytes)
    {
        MediaCodec codec = _codec ?? throw new InvalidOperationException("编码器尚未开始");
        int inputIndex;
        // 编码器输入队列满时先排空输出，腾出缓冲区。
        while ((inputIndex = codec.DequeueInputBuffer(TimeoutUs)) < 0)
        {
            DrainVideo(false);
        }

        Image image = codec.GetInputImage(inputIndex) ?? throw new InvalidOperationException("编码器不支持 Image 输入");
        FillImage(image, pixels, rowBytes);
        long ptsUs = _frameIndex * 1_000_000L / _fps;
        codec.QueueInputBuffer(inputIndex, 0, _width * _height * 3 / 2, ptsUs, MediaCodecBufferFlags.None);
        _frameIndex++;
        DrainVideo(false);
    }

    /// <summary>BGRA 转 BT.601 限幅 YUV420，按各平面实际的行跨度与像素跨度写入，兼容 I420 / NV12 / NV21 布局。</summary>
    private unsafe void FillImage(Image image, ReadOnlySpan<byte> pixels, int rowBytes)
    {
        Image.Plane[] planes = image.GetPlanes()!;
        nint yAddr = JNIEnv.GetDirectBufferAddress(planes[0].Buffer!.Handle);
        nint uAddr = JNIEnv.GetDirectBufferAddress(planes[1].Buffer!.Handle);
        nint vAddr = JNIEnv.GetDirectBufferAddress(planes[2].Buffer!.Handle);
        int yRow = planes[0].RowStride;
        int yPix = planes[0].PixelStride;
        int uRow = planes[1].RowStride;
        int uPix = planes[1].PixelStride;
        int vRow = planes[2].RowStride;
        int vPix = planes[2].PixelStride;
        int width = _width;
        int height = _height;

        fixed (byte* srcFixed = pixels)
        {
            nint src = (nint)srcFixed;
            Parallel.For(0, height, r =>
            {
                byte* s = (byte*)src + (long)r * rowBytes;
                byte* y = (byte*)yAddr + (long)r * yRow;
                for (int c = 0; c < width; c++)
                {
                    int b = s[c * 4];
                    int g = s[c * 4 + 1];
                    int rr = s[c * 4 + 2];
                    y[c * yPix] = (byte)(((66 * rr + 129 * g + 25 * b + 128) >> 8) + 16);
                }
            });
            Parallel.For(0, height / 2, r =>
            {
                byte* s0 = (byte*)src + (long)(r * 2) * rowBytes;
                byte* s1 = s0 + rowBytes;
                byte* u = (byte*)uAddr + (long)r * uRow;
                byte* v = (byte*)vAddr + (long)r * vRow;
                for (int c = 0; c < width / 2; c++)
                {
                    int i = c * 8;
                    // 2×2 像素取平均，得到一个色度样本。
                    int b = (s0[i] + s0[i + 4] + s1[i] + s1[i + 4]) >> 2;
                    int g = (s0[i + 1] + s0[i + 5] + s1[i + 1] + s1[i + 5]) >> 2;
                    int rr = (s0[i + 2] + s0[i + 6] + s1[i + 2] + s1[i + 6]) >> 2;
                    u[c * uPix] = (byte)(((-38 * rr - 74 * g + 112 * b + 128) >> 8) + 128);
                    v[c * vPix] = (byte)(((112 * rr - 94 * g - 18 * b + 128) >> 8) + 128);
                }
            });
        }
    }

    /// <summary>取走编码器已有输出写入 muxer；endOfStream 为真时阻塞到收到结束标记。</summary>
    private void DrainVideo(bool endOfStream)
    {
        MediaCodec codec = _codec!;
        MediaCodec.BufferInfo info = new();
        while (true)
        {
            int index = codec.DequeueOutputBuffer(info, endOfStream ? TimeoutUs : 0);
            if (index == InfoTryAgainLater)
            {
                if (!endOfStream) return;
                continue;
            }

            if (index == InfoOutputFormatChanged)
            {
                _videoTrack = _muxer!.AddTrack(codec.OutputFormat!);
                _muxer.Start();
                _muxerStarted = true;
                continue;
            }

            if (index < 0) continue;
            ByteBuffer buffer = codec.GetOutputBuffer(index)!;
            // 编码配置数据已经在 muxer 的轨道格式里，不作为样本写入。
            if ((info.Flags & MediaCodecBufferFlags.CodecConfig) != 0) info.Size = 0;
            if (info.Size > 0 && _muxerStarted)
            {
                buffer.Position(info.Offset);
                buffer.Limit(info.Offset + info.Size);
                _muxer!.WriteSampleData(_videoTrack, buffer, info);
            }

            codec.ReleaseOutputBuffer(index, false);
            if ((info.Flags & MediaCodecBufferFlags.EndOfStream) != 0) return;
        }
    }

    /// <inheritdoc />
    public void Finish()
    {
        MediaCodec codec = _codec ?? throw new InvalidOperationException("编码器尚未开始");
        int inputIndex;
        while ((inputIndex = codec.DequeueInputBuffer(TimeoutUs)) < 0)
        {
            DrainVideo(false);
        }

        long endPtsUs = _frameIndex * 1_000_000L / _fps;
        codec.QueueInputBuffer(inputIndex, 0, 0, endPtsUs, MediaCodecBufferFlags.EndOfStream);
        DrainVideo(true);
        ReleaseVideo();

        string final = _videoTempPath;
        if (_audioWavPath != null)
        {
            string combined = Path.Combine(Path.GetTempPath(), $"pianoroll_f_{Guid.NewGuid():N}.mp4");
            try
            {
                EncodeAudio(_audioWavPath, _audioStartMs, _durationMs, _audioTempPath);
                Remux(_videoTempPath, _audioTempPath, combined);
                final = combined;
            }
            catch (Exception ex)
            {
                // 音频失败时保留无声视频，比整次导出失败更有用。
                Log.Warning(ex, "钢琴卷帘视频：音频封装失败，导出无声视频");
                TryDelete(combined);
            }
        }

        File.Copy(final, _outputPath, true);
        if (final != _videoTempPath) TryDelete(final);
        TryDelete(_videoTempPath);
        TryDelete(_audioTempPath);
    }

    #region 音频

    /// <summary>读取 wav 的 PCM16 / float32 数据，跳过 startMs，只取 durationMs 长度，用 AAC 编码为 m4a。</summary>
    private static void EncodeAudio(string wavPath, double startMs, double durationMs, string outputPath)
    {
        using FileStream stream = File.OpenRead(wavPath);
        (int channels, int sampleRate, bool isFloat, long dataStart, long dataLength) = ReadWavHeader(stream);
        int bytesPerSample = isFloat ? 4 : 2;
        int blockAlign = channels * bytesPerSample;
        long skip = Math.Min(dataLength / blockAlign, (long)(startMs / 1000.0 * sampleRate)) * blockAlign;
        stream.Position = dataStart + skip;
        // 音频长度以视频为准：混音通常比这一段分片长得多。
        long durationBytes = (long)Math.Ceiling(durationMs / 1000.0 * sampleRate) * blockAlign;
        long remaining = Math.Min(dataLength - skip, durationBytes);

        MediaFormat format = MediaFormat.CreateAudioFormat(AudioMime, sampleRate, channels)!;
        format.SetInteger(MediaFormat.KeyAacProfile, 2); // AAC-LC
        format.SetInteger(MediaFormat.KeyBitRate, 192_000);
        format.SetInteger(MediaFormat.KeyMaxInputSize, 65536);
        MediaCodec codec = MediaCodec.CreateEncoderByType(AudioMime)!;
        MediaMuxer muxer = new(outputPath, MuxerOutputType.Mpeg4);
        bool codecStarted = false;
        bool muxerStarted = false;
        try
        {
            codec.Configure(format, null, null, MediaCodecConfigFlags.Encode);
            codec.Start();
            codecStarted = true;
            byte[] chunk = new byte[32768 * bytesPerSample / 2];
            byte[] pcm = new byte[32768];
            int track = -1;
            bool inputDone = false;
            bool outputDone = false;
            long samplesFed = 0;
            MediaCodec.BufferInfo info = new();
            while (!outputDone)
            {
                if (!inputDone)
                {
                    int inputIndex = codec.DequeueInputBuffer(TimeoutUs);
                    if (inputIndex >= 0)
                    {
                        ByteBuffer input = codec.GetInputBuffer(inputIndex)!;
                        int want = Math.Min(pcm.Length, input.Capacity()) / (channels * 2) * (channels * 2);
                        int got = ReadPcm16(stream, chunk, pcm, want, isFloat, ref remaining);
                        long ptsUs = samplesFed * 1_000_000L / sampleRate;
                        if (got <= 0)
                        {
                            codec.QueueInputBuffer(inputIndex, 0, 0, ptsUs, MediaCodecBufferFlags.EndOfStream);
                            inputDone = true;
                        }
                        else
                        {
                            input.Clear();
                            input.Put(pcm, 0, got);
                            codec.QueueInputBuffer(inputIndex, 0, got, ptsUs, MediaCodecBufferFlags.None);
                            samplesFed += got / (channels * 2);
                        }
                    }
                }

                int outIndex = codec.DequeueOutputBuffer(info, TimeoutUs);
                if (outIndex == InfoTryAgainLater) continue;
                if (outIndex == InfoOutputFormatChanged)
                {
                    track = muxer.AddTrack(codec.OutputFormat!);
                    muxer.Start();
                    muxerStarted = true;
                    continue;
                }

                if (outIndex < 0) continue;
                ByteBuffer output = codec.GetOutputBuffer(outIndex)!;
                if ((info.Flags & MediaCodecBufferFlags.CodecConfig) != 0) info.Size = 0;
                if (info.Size > 0 && muxerStarted)
                {
                    output.Position(info.Offset);
                    output.Limit(info.Offset + info.Size);
                    muxer.WriteSampleData(track, output, info);
                }

                codec.ReleaseOutputBuffer(outIndex, false);
                outputDone = (info.Flags & MediaCodecBufferFlags.EndOfStream) != 0;
            }
        }
        finally
        {
            // 清理阶段的异常会盖住真正的失败原因，所以每一步单独保护；muxer 没启动过就不能 Stop。
            if (codecStarted) SafeCall(codec.Stop, "停止音频编码器");
            SafeCall(codec.Release, "释放音频编码器");
            if (muxerStarted) SafeCall(muxer.Stop, "停止音频 muxer");
            SafeCall(muxer.Release, "释放音频 muxer");
        }
    }

    /// <summary>执行清理动作，失败只记录日志，不向外抛出。</summary>
    private static void SafeCall(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "钢琴卷帘视频：{What}时出错", what);
        }
    }

    /// <summary>读取至多 want 字节的 16 位 PCM；float32 源会在这里转换。返回写入 pcm 的字节数。</summary>
    private static int ReadPcm16(Stream stream, byte[] chunk, byte[] pcm, int want, bool isFloat, ref long remaining)
    {
        if (remaining <= 0 || want <= 0) return 0;
        if (!isFloat)
        {
            int read = stream.Read(pcm, 0, (int)Math.Min(want, remaining));
            remaining -= read;
            return read;
        }

        int floatBytes = (int)Math.Min(want * 2L, remaining) / 4 * 4;
        int got = stream.Read(chunk, 0, Math.Min(floatBytes, chunk.Length));
        remaining -= got;
        int samples = got / 4;
        for (int i = 0; i < samples; i++)
        {
            float value = Math.Clamp(BitConverter.ToSingle(chunk, i * 4), -1f, 1f);
            short s = (short)(value * short.MaxValue);
            pcm[i * 2] = (byte)(s & 0xFF);
            pcm[i * 2 + 1] = (byte)(s >> 8);
        }

        return samples * 2;
    }

    /// <summary>解析 RIFF/WAVE 头，仅支持 16 位 PCM 与 32 位浮点。</summary>
    private static (int Channels, int SampleRate, bool IsFloat, long DataStart, long DataLength) ReadWavHeader(Stream stream)
    {
        using BinaryReader reader = new(stream, System.Text.Encoding.ASCII, true);
        if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("不是 RIFF 文件");
        reader.ReadInt32();
        if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("不是 WAVE 文件");
        int channels = 0;
        int sampleRate = 0;
        bool isFloat = false;
        while (stream.Position + 8 <= stream.Length)
        {
            string id = new(reader.ReadChars(4));
            int size = reader.ReadInt32();
            if (id == "fmt ")
            {
                short tag = reader.ReadInt16();
                channels = reader.ReadInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32();
                reader.ReadInt16();
                short bits = reader.ReadInt16();
                isFloat = tag == 3;
                if (!(tag == 1 && bits == 16) && !(tag == 3 && bits == 32))
                {
                    throw new NotSupportedException($"不支持的 wav 格式 tag={tag} bits={bits}");
                }

                stream.Position += size - 16;
            }
            else if (id == "data")
            {
                if (channels == 0) throw new InvalidDataException("data 块出现在 fmt 块之前");
                return (channels, sampleRate, isFloat, stream.Position, Math.Min(size, stream.Length - stream.Position));
            }
            else
            {
                stream.Position += size + (size & 1);
            }
        }

        throw new InvalidDataException("找不到 data 块");
    }

    /// <summary>不重编码地把视频轨与音频轨封装进同一个 mp4。</summary>
    private static void Remux(string videoPath, string audioPath, string outputPath)
    {
        MediaExtractor videoExtractor = new();
        MediaExtractor audioExtractor = new();
        MediaMuxer muxer = new(outputPath, MuxerOutputType.Mpeg4);
        try
        {
            videoExtractor.SetDataSource(videoPath);
            audioExtractor.SetDataSource(audioPath);
            int videoIndex = SelectTrack(videoExtractor, "video/");
            int audioIndex = SelectTrack(audioExtractor, "audio/");
            int videoOut = muxer.AddTrack(videoExtractor.GetTrackFormat(videoIndex)!);
            int audioOut = muxer.AddTrack(audioExtractor.GetTrackFormat(audioIndex)!);
            muxer.Start();
            ByteBuffer buffer = ByteBuffer.Allocate(4 * 1024 * 1024)!;
            CopySamples(videoExtractor, muxer, videoOut, buffer);
            CopySamples(audioExtractor, muxer, audioOut, buffer);
            muxer.Stop();
        }
        finally
        {
            SafeCall(muxer.Release, "释放封装 muxer");
            SafeCall(videoExtractor.Release, "释放视频 extractor");
            SafeCall(audioExtractor.Release, "释放音频 extractor");
        }
    }

    private static int SelectTrack(MediaExtractor extractor, string mimePrefix)
    {
        for (int i = 0; i < extractor.TrackCount; i++)
        {
            string? mime = extractor.GetTrackFormat(i)?.GetString(MediaFormat.KeyMime);
            if (mime != null && mime.StartsWith(mimePrefix, StringComparison.Ordinal))
            {
                extractor.SelectTrack(i);
                return i;
            }
        }

        throw new InvalidDataException($"没有找到 {mimePrefix} 轨道");
    }

    private static void CopySamples(MediaExtractor extractor, MediaMuxer muxer, int track, ByteBuffer buffer)
    {
        MediaCodec.BufferInfo info = new();
        while (true)
        {
            buffer.Clear();
            int size = extractor.ReadSampleData(buffer, 0);
            if (size < 0) return;
            // MediaExtractor 的样本标志位与 MediaCodec 的缓冲区标志位对关键帧都是 1。
            info.Set(0, size, extractor.SampleTime, (MediaCodecBufferFlags)(int)extractor.SampleFlags);
            muxer.WriteSampleData(track, buffer, info);
            extractor.Advance();
        }
    }

    #endregion

    private void ReleaseVideo()
    {
        try
        {
            _codec?.Stop();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "钢琴卷帘视频：停止视频编码器时出错");
        }

        if (_codec != null) SafeCall(_codec.Release, "释放视频编码器");
        _codec = null;
        try
        {
            if (_muxerStarted) _muxer?.Stop();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "钢琴卷帘视频：停止 muxer 时出错");
        }

        if (_muxer != null) SafeCall(_muxer.Release, "释放视频 muxer");
        _muxer = null;
    }

    /// <inheritdoc />
    public void Abort()
    {
        ReleaseVideo();
        TryDelete(_videoTempPath);
        TryDelete(_audioTempPath);
    }

    /// <inheritdoc />
    public void Dispose() => ReleaseVideo();

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "钢琴卷帘视频：删除临时文件失败 {Path}", path);
        }
    }
}
