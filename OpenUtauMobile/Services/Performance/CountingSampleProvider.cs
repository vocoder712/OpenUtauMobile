using System.Threading;
using NAudio.Wave;

namespace OpenUtauMobile.Services.Performance;

/// <summary>统计已交给输出后端的帧数，不修改音频样本。</summary>
public sealed class CountingSampleProvider(ISampleProvider source) : ISampleProvider
{
    private long _frames;
    public WaveFormat WaveFormat => source.WaveFormat;
    public long Frames => Interlocked.Read(ref _frames);

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        Interlocked.Add(ref _frames, read / WaveFormat.Channels);
        return read;
    }
}
