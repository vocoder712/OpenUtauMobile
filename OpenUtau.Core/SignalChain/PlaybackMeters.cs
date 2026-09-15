using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.SignalChain {
    /// <summary>单次播放的电平邮箱；音频线程只累计峰值，界面线程按需读取。</summary>
    public sealed class PlaybackMeters {
        public UProject Project { get; }
        public IReadOnlyDictionary<UTrack, StereoPeakMeter> Tracks { get; }
        public StereoPeakMeter Master { get; } = new StereoPeakMeter();
        public volatile bool Enabled;

        public PlaybackMeters(UProject project) {
            Project = project;
            Tracks = project.tracks.ToDictionary(track => track, _ => new StereoPeakMeter());
        }
    }

    public sealed class StereoPeakMeter {
        private struct PeakFrame
        {
            public long EndPosition;
            public float Left, Right;
        }
        private readonly PeakFrame[] frames = new PeakFrame[512];
        private int read, write, count, gate;

        public void Push(float[] buffer, int offset, int sampleCount, long position = 0)
        {
            if (Interlocked.CompareExchange(ref gate, 1, 0) != 0) return;
            try
            {
                // 以约 6 ms 的片段保留峰值，界面仅消费播放头已经经过的片段。
                for (int start = 0; start < sampleCount; start += 512)
                {
                    int end = Math.Min(sampleCount, start + 512);
                    float left = 0, right = 0;
                    for (int i = start; i < end; i++)
                    {
                        float value = Math.Abs(buffer[offset + i]);
                        if (!float.IsFinite(value)) continue;
                        if ((i & 1) == 0) left = Math.Max(left, value);
                        else right = Math.Max(right, value);
                    }
                    frames[write] = new PeakFrame { EndPosition = position + end, Left = left, Right = right };
                    write = (write + 1) % frames.Length;
                    if (count == frames.Length) read = (read + 1) % frames.Length;
                    else count++;
                }
            }
            finally { Volatile.Write(ref gate, 0); }
        }

        /// <summary>取走已播放片段的峰值；保留尚在输出缓冲区内的数据。</summary>
        public (double LeftDb, double RightDb) Consume(long audiblePosition = long.MaxValue)
        {
            if (Interlocked.CompareExchange(ref gate, 1, 0) != 0)
                return (double.NegativeInfinity, double.NegativeInfinity);
            try
            {
                float left = 0, right = 0;
                while (count > 0 && frames[read].EndPosition <= audiblePosition)
                {
                    left = Math.Max(left, frames[read].Left);
                    right = Math.Max(right, frames[read].Right);
                    read = (read + 1) % frames.Length;
                    count--;
                }
                return (ToDb(left), ToDb(right));
            }
            finally { Volatile.Write(ref gate, 0); }
        }

        private static double ToDb(float value) => value > 0 ? 20 * Math.Log10(value) : double.NegativeInfinity;
    }

    /// <summary>在独立缓冲区测量效果器输出，避免混入先前轨道的累加结果。</summary>
    internal sealed class MeteredSource : ISignalSource {
        private readonly ISignalSource source;
        private readonly PlaybackMeters session;
        private readonly StereoPeakMeter meter;
        private float[] scratch = Array.Empty<float>();

        public MeteredSource(ISignalSource source, PlaybackMeters session, StereoPeakMeter meter) {
            this.source = source;
            this.session = session;
            this.meter = meter;
        }

        public bool IsReady(int position, int count) => source.IsReady(position, count);

        public int Mix(int position, float[] buffer, int index, int count) {
            if (!session.Enabled) return source.Mix(position, buffer, index, count);
            if (scratch.Length < count) scratch = new float[count];
            Array.Clear(scratch, 0, count);
            int end = source.Mix(position, scratch, 0, count);
            meter.Push(scratch, 0, count, position);
            for (int i = 0; i < count; i++) buffer[index + i] += scratch[i];
            return end;
        }
    }
}
