using System;
using System.Collections.Generic;
using System.Threading;
using OpenUtau.Core.Util;

namespace OpenUtauMobile.Controls
{
    // 只保存正半轴峰值。分层预聚合使缩放时的查询成本与 PCM 长度无关。
    internal sealed class RenderPeakPyramid
    {
        private readonly float[][] _levels;
        private readonly int _blockFrames;
        public int ByteSize { get; }

        private RenderPeakPyramid(float[][] levels, int blockFrames)
        {
            _levels = levels;
            _blockFrames = blockFrames;
            foreach (float[] level in levels)
            {
                ByteSize += level.Length * sizeof(float);
            }
        }

        public static RenderPeakPyramid Build(Frozen<float> pcm, int channels, CancellationToken token)
        {
            int frames = pcm.Length / channels;
            int block = 64;
            // 超长乐句也限制单个缓存的大小，避免低内存设备出现分配尖峰。
            while ((long)frames > (long)block * 65536)
            {
                block *= 2;
            }
            float[] peaks = new float[(int)(((long)frames + block - 1) / block)];
            for (int i = 0; i < peaks.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                int end = (int)Math.Min((long)(i + 1) * block * channels, pcm.Length);
                float peak = 0;
                for (int s = i * block * channels; s < end; s++)
                {
                    float sample = pcm.Buffer[s];
                    if (float.IsFinite(sample))
                    {
                        peak = Math.Max(peak, sample);
                    }
                }
                peaks[i] = Math.Min(1, peak);
            }
            List<float[]> levels = [peaks];
            while (peaks.Length > 1)
            {
                token.ThrowIfCancellationRequested();
                float[] next = new float[(peaks.Length + 1) / 2];
                for (int i = 0; i < next.Length; i++)
                {
                    next[i] = Math.Max(peaks[i * 2], peaks[Math.Min(i * 2 + 1, peaks.Length - 1)]);
                }
                levels.Add(next);
                peaks = next;
            }
            return new RenderPeakPyramid(levels.ToArray(), block);
        }

        public float Peak(double startFrame, double endFrame)
        {
            int level = 0;
            double block = _blockFrames;
            while (level + 1 < _levels.Length && block * 2 <= endFrame - startFrame)
            {
                level++;
                block *= 2;
            }
            float[] peaks = _levels[level];
            int first = (int)Math.Clamp(Math.Floor(startFrame / block), 0, peaks.Length);
            int end = (int)Math.Clamp(Math.Ceiling(endFrame / block), first, peaks.Length);
            float peak = 0;
            // 每个屏幕列最多读取三个聚合块；边界保守保留峰值，不漏掉瞬态。
            for (int i = first; i < end; i++)
            {
                peak = Math.Max(peak, peaks[i]);
            }
            return peak;
        }
    }
}
