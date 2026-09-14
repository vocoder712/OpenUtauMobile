using System;
using System.Threading;
using NAudio.Wave;

namespace OpenUtau.Core.SignalChain {
    class MasterAdapter : ISampleProvider {
        private const int SampleRate = 44100;
        private const int Channels = 2;
        // Short edge fades prevent clicks without mixing audio past the playback end.
        private const int FadeMilliseconds = 3;
        private const int FadeFrames = SampleRate * FadeMilliseconds / 1000;

        private readonly WaveFormat waveFormat;
        private readonly ISignalSource source;
        private readonly int endPosition;
        private int position;
        private int startPosition;

        private struct PositionSpan
        {
            public long OutputStartFrame;
            public int SourceStart, FrameCount;
            public bool Waiting;
        }
        private readonly PositionSpan[] timeline = new PositionSpan[2048];
        private int timelineVersion;
        private long spanCount, outputFrames, lastAudiblePosition;
        private bool lastAudibleWaiting;

        // 音频线程只发布时间段；界面读取一致快照，不把尚未播放的等待静音提前扣除。
        private void RecordPosition(int sourceStart, int samples, bool waiting)
        {
            if (samples <= 0) return;
            Interlocked.Increment(ref timelineVersion);
            timeline[spanCount % timeline.Length] = new PositionSpan
            {
                OutputStartFrame = outputFrames,
                SourceStart = sourceStart,
                FrameCount = samples / Channels,
                Waiting = waiting,
            };
            outputFrames += samples / Channels;
            spanCount++;
            Interlocked.Increment(ref timelineVersion);
        }

        public long GetProjectPosition(long playedFrames, out bool waiting)
        {
            int version = Volatile.Read(ref timelineVersion);
            waiting = lastAudibleWaiting;
            if ((version & 1) != 0) return lastAudiblePosition;
            long total = spanCount;
            long position = startPosition;
            bool isWaiting = false;
            for (long i = total - 1; i >= Math.Max(0, total - timeline.Length); i--)
            {
                PositionSpan span = timeline[i % timeline.Length];
                if (playedFrames < span.OutputStartFrame) continue;
                position = span.SourceStart + (span.Waiting ? 0 : Math.Clamp(playedFrames - span.OutputStartFrame, 0, span.FrameCount) * Channels);
                isWaiting = span.Waiting;
                break;
            }
            Thread.MemoryBarrier();
            if (version != Volatile.Read(ref timelineVersion)) return lastAudiblePosition;
            lastAudiblePosition = position;
            waiting = lastAudibleWaiting = isWaiting;
            return position;
        }

        public WaveFormat WaveFormat => waveFormat;
        public int Waited { get; private set; }
        public bool IsWaiting { get; private set; }
        public PlaybackMeters Meters { get; }
        public PlaybackMixer Mixer { get; set; }
        public MasterAdapter(ISignalSource source, double endMs = double.PositiveInfinity, PlaybackMeters meters = null) {
            Meters = meters;
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);
            this.source = source;
            endPosition = double.IsPositiveInfinity(endMs)
                ? -1
                : (int)(endMs * SampleRate / 1000) * Channels;
        }

        public int Read(float[] buffer, int offset, int count) {
            if (endPosition >= 0) {
                count = Math.Min(count, endPosition - position);
                if (count <= 0) {
                    return 0;
                }
            }
            for (int i = offset; i < offset + count; ++i) {
                buffer[i] = 0;
            }
            if (!source.IsReady(position, count)) {
                RecordPosition(position, count, true);
                Waited += count;
                IsWaiting = true;
                return count;
            } else {
                int readPosition = position;
                int pos = source.Mix(position, buffer, offset, count);
                int n = Math.Max(0, pos - position);
                position = pos;
                int startFrame = startPosition / Channels;
                int endFrame = endPosition / Channels;
                for (int i = 0; i < n; ++i) {
                    int frame = (readPosition + i) / Channels;
                    int elapsedFrames = frame - startFrame;
                    float gain = Math.Clamp(
                        elapsedFrames / (FadeFrames - 1f),
                        0,
                        1);
                    if (endPosition >= 0) {
                        int remainingFrames = endFrame - (readPosition + i) / Channels;
                        if (remainingFrames <= FadeFrames) {
                            gain = Math.Min(
                                gain,
                                Math.Clamp(
                                    (remainingFrames - 1f) / (FadeFrames - 1),
                                    0,
                                    1));
                        }
                    }
                    if (gain < 1) {
                        buffer[offset + i] *= gain;
                    }
                }
                // 总线取实际返回的输出，包含节拍器与播放边缘淡入淡出。
                if (Meters != null && Meters.Enabled) Meters.Master.Push(buffer, offset, n, readPosition);
                RecordPosition(readPosition, n, false);
                IsWaiting = false;
                return n;
            }
        }

        public void SetPosition(int position) {
            this.position = position;
            startPosition = position;
            Waited = 0;
            spanCount = outputFrames = 0;
            lastAudiblePosition = position;
            lastAudibleWaiting = false;
        }
    }
}
