using NAudio.Wave;
using OpenUtau.Core.SignalChain;

namespace OpenUtauMobile.Audio;

/// <summary>
/// 从 ISignalSource 到 ISampleProvider 的适配器。
/// Adapter that wraps an ISignalSource as an ISampleProvider for NAudio output.
/// </summary>
internal class SoundFontAdapter : ISampleProvider
{
    private readonly ISignalSource _source;
    private int _position;

    public WaveFormat WaveFormat { get; }

    public SoundFontAdapter(ISignalSource source)
    {
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        _source = source;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        // Clear buffer first
        for (int i = offset; i < offset + count; i++)
        {
            buffer[i] = 0;
        }

        if (!_source.IsReady(_position, count))
        {
            // Still return count to keep stream going
            return count;
        }

        int pos = _source.Mix(_position, buffer, offset, count);
        int n = pos - _position;
        _position = pos;
        return n > 0 ? n : count;
    }
}