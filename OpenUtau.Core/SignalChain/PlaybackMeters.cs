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
        private float left;
        private float right;

        public void Push(float[] buffer, int offset, int count) {
            float l = 0, r = 0;
            for (int i = 0; i < count; i++) {
                float value = Math.Abs(buffer[offset + i]);
                if (float.IsNaN(value)) continue;
                if ((i & 1) == 0) l = Math.Max(l, value);
                else r = Math.Max(r, value);
            }
            Accumulate(ref left, l);
            Accumulate(ref right, r);
        }

        private static void Accumulate(ref float target, float value) {
            float previous = Volatile.Read(ref target);
            while (value > previous) {
                float observed = Interlocked.CompareExchange(ref target, value, previous);
                if (observed == previous) return;
                previous = observed;
            }
        }

        /// <summary>取走刷新间隔内的最大值，无样本时返回静音；不阻塞音频线程。</summary>
        public (double LeftDb, double RightDb) Consume() {
            return (ToDb(Interlocked.Exchange(ref left, 0)), ToDb(Interlocked.Exchange(ref right, 0)));
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
            meter.Push(scratch, 0, count);
            for (int i = 0; i < count; i++) buffer[index + i] += scratch[i];
            return end;
        }
    }
}
