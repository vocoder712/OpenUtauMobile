using System;
using System.Numerics;
using System.Threading;

namespace OpenUtauMobile.Helpers.Audio;

/// <summary>固定容量的播放样本邮箱；音频线程争用时跳过，频谱计算只在界面线程执行。</summary>
internal sealed class MixerAudioAnalysis
{
    internal const int FrameCount = 4096;
    internal const int BandCount = 128;
    private readonly float[] _ring = new float[131072];
    private readonly float[] _snapshot = new float[FrameCount * 2];
    private readonly Complex[] _left = new Complex[FrameCount];
    private readonly Complex[] _right = new Complex[FrameCount];
    private readonly double[] _window = new double[FrameCount];
    private readonly double[] _power = new double[FrameCount / 2 + 1];
    private readonly double _windowSum;
    private int _gate;
    private int _write;
    private int _available;
    private long _endPosition;
    private long _lastReadPosition = -1;
    public double PeakDb { get; private set; } = double.NegativeInfinity;
    public double[] SpectrumDb { get; } = new double[BandCount];

    public MixerAudioAnalysis()
    {
        Array.Fill(SpectrumDb, -90);
        for (int i = 0; i < FrameCount; i++)
        {
            _window[i] = .5 - .5 * Math.Cos(2 * Math.PI * i / (FrameCount - 1));
            _windowSum += _window[i];
        }
    }

    public void Push(int position, ReadOnlySpan<float> samples)
    {
        if (Interlocked.CompareExchange(ref _gate, 1, 0) != 0) return;
        try
        {
            int count = samples.Length & ~1;
            if (_endPosition != position) _available = 0;
            int start = Math.Max(0, count - _ring.Length);
            for (int i = start; i < count; i++)
            {
                float value = samples[i];
                _ring[_write] = float.IsFinite(value) ? value : 0;
                _write = (_write + 1) % _ring.Length;
            }
            _available = Math.Min(_ring.Length, _available + count - start);
            _endPosition = (long)position + count;
        }
        finally { Volatile.Write(ref _gate, 0); }
    }

    public bool Read(bool spectrum, long audiblePosition)
    {
        if (Interlocked.CompareExchange(ref _gate, 1, 0) != 0) return false;
        try
        {
            long end = Math.Min(audiblePosition & ~1L, _endPosition);
            long earliest = _endPosition - _available;
            if (end <= earliest || end <= _lastReadPosition) return false;
            float peak = 0;
            for (long position = Math.Max(earliest, _lastReadPosition); position < end; position++)
                peak = Math.Max(peak, Math.Abs(SampleAt(position)));
            PeakDb = peak > 0 ? 20 * Math.Log10(peak) : double.NegativeInfinity;
            _lastReadPosition = end;
            if (spectrum)
            {
                long first = Math.Max(earliest, end - _snapshot.Length);
                int padding = _snapshot.Length - (int)(end - first);
                Array.Clear(_snapshot, 0, padding);
                for (int i = padding; i < _snapshot.Length; i++) _snapshot[i] = SampleAt(first + i - padding);
            }
        }
        finally { Volatile.Write(ref _gate, 0); }
        if (spectrum) AnalyzeSpectrum();
        return true;
    }

    private float SampleAt(long position) => _ring[(int)((_write - (_endPosition - position) + _ring.Length) % _ring.Length)];

    private void AnalyzeSpectrum()
    {
        for (int i = 0; i < FrameCount; i++)
        {
            _left[i] = new Complex(_snapshot[2 * i] * _window[i], 0);
            _right[i] = new Complex(_snapshot[2 * i + 1] * _window[i], 0);
        }
        Transform(_left);
        Transform(_right);
        double scale = 4 / (_windowSum * _windowSum);
        for (int i = 0; i < _power.Length; i++)
        {
            // 合并功率而非波形，反相的左右声道不会相互抵消。
            _power[i] = (_left[i].Magnitude * _left[i].Magnitude + _right[i].Magnitude * _right[i].Magnitude) * .5 * scale;
        }
        for (int band = 0; band < BandCount; band++)
        {
            double low = 20 * Math.Pow(1000, (double)band / BandCount);
            double high = 20 * Math.Pow(1000, (double)(band + 1) / BandCount);
            int first = Math.Clamp((int)Math.Round(low * FrameCount / 44100), 1, FrameCount / 2);
            int last = Math.Clamp((int)Math.Round(high * FrameCount / 44100), first, FrameCount / 2);
            double peak = 0;
            for (int bin = first; bin <= last; bin++) peak = Math.Max(peak, _power[bin]);
            SpectrumDb[band] = peak > 0 ? Math.Clamp(10 * Math.Log10(peak), -90, 0) : -90;
        }
    }

    private static void Transform(Complex[] data)
    {
        for (int i = 1, j = 0; i < data.Length; i++)
        {
            int bit = data.Length >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (data[i], data[j]) = (data[j], data[i]);
        }
        for (int size = 2; size <= data.Length; size <<= 1)
        {
            Complex step = Complex.FromPolarCoordinates(1, -2 * Math.PI / size);
            for (int start = 0; start < data.Length; start += size)
            {
                Complex factor = Complex.One;
                for (int i = 0; i < size / 2; i++)
                {
                    Complex even = data[start + i];
                    Complex odd = data[start + i + size / 2] * factor;
                    data[start + i] = even + odd;
                    data[start + i + size / 2] = even - odd;
                    factor *= step;
                }
            }
        }
    }
}
