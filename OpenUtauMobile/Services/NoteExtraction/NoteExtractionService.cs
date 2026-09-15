using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenUtau.Core.Analysis;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services.NoteExtraction;

internal static class NoteExtractionService
{
    // 无静音的长输入进一步分块，控制移动设备峰值内存并提供取消检查点。
    private const int MaximumChunkSeconds = 30;

    public static UVoicePart Extract(UProject project, UWavePart source, IGameBackend backend,
        CancellationToken cancellation, Action<int, int> progress, NoteExtractionSettings? settings = null)
    {
        settings ??= new();
        settings.Validate();
        cancellation.ThrowIfCancellationRequested();
        if (source.Samples == null || source.channels <= 0 || source.sampleRate != AudioSlicer.SampleRate
            || backend.Config.SampleRate != AudioSlicer.SampleRate)
        {
            throw new InvalidOperationException("GAME requires loaded 44100 Hz audio.");
        }
        var (_, _, channels, pcm) = source.GetTrimmedSamples(project);
        float[] mono = new float[pcm.Length / channels];
        for (int frame = 0; frame < mono.Length; frame++)
        {
            for (int channel = 0; channel < channels; channel++)
            {
                mono[frame] += pcm[(frame * channels) + channel] / channels;
            }
        }
        cancellation.ThrowIfCancellationRequested();
        UVoicePart result = new() { position = source.position, Duration = source.Duration, name = source.name };
        if (mono.Length < 441) return result;

        List<AudioSlicer.Chunk> chunks = SplitChunks(AudioSlicer.Slice(mono));
        using CancellationTokenRegistration registration = cancellation.Register(backend.Interrupt);
        GameOptions options = settings.ToGameOptions();
        double originMs = project.timeAxis.TickPosToMsPos(source.position);
        int total = chunks.Sum(chunk => chunk.samples.Length);
        int processed = 0;
        foreach (List<AudioSlicer.Chunk> batch in BuildBatches(chunks, settings, backend is GameOnnxBackend))
        {
            cancellation.ThrowIfCancellationRequested();
            List<List<TranscribedNote>> predictions = backend.RunInferenceBatch(batch.Select(chunk => chunk.samples).ToList(), options);
            cancellation.ThrowIfCancellationRequested();
            if (predictions.Count != batch.Count) throw new InvalidOperationException("Invalid GAME batch result count.");
            for (int i = 0; i < batch.Count; i++)
            {
                AudioSlicer.Chunk chunk = batch[i];
                double cursorMs = originMs + chunk.offsetMs;
                double chunkEndMs = cursorMs + chunk.samples.Length * 1000.0 / AudioSlicer.SampleRate;
                foreach (TranscribedNote note in predictions[i])
                {
                    if (!float.IsFinite(note.noteDuration) || note.noteDuration < 0 || !float.IsFinite(note.noteScore))
                    {
                        throw new InvalidOperationException("Invalid GAME inference result.");
                    }
                    double endMs = Math.Min(cursorMs + note.noteDuration * 1000.0, chunkEndMs);
                    int position = Math.Clamp(project.timeAxis.MsPosToTickPos(cursorMs) - source.position, 0, source.Duration);
                    int endTick = Math.Clamp(project.timeAxis.MsPosToTickPos(endMs) - source.position, 0, source.Duration);
                    if (note.noteVoiced && endTick > position)
                    {
                        result.notes.Add(project.CreateNote(Math.Clamp((int)Math.Round(note.noteScore), 0, 127), position, endTick - position));
                    }
                    cursorMs = endMs;
                }
                processed += chunk.samples.Length;
            }
            progress(processed, total);
        }
        cancellation.ThrowIfCancellationRequested();
        progress(mono.Length, mono.Length);
        return result;
    }

    private static List<AudioSlicer.Chunk> SplitChunks(List<AudioSlicer.Chunk> chunks)
    {
        List<AudioSlicer.Chunk> result = [];
        int maximumSamples = MaximumChunkSeconds * AudioSlicer.SampleRate;
        foreach (AudioSlicer.Chunk chunk in chunks)
        {
            for (int start = 0; start < chunk.samples.Length; start += maximumSamples)
            {
                int end = Math.Min(start + maximumSamples, chunk.samples.Length);
                // 不足一个 10 ms 模型帧的尾部无法产生有效音符。
                if (end - start < 441) continue;
                result.Add(chunk with
                {
                    samples = start == 0 && end == chunk.samples.Length ? chunk.samples : chunk.samples[start..end],
                    offsetMs = chunk.offsetMs + start * 1000.0 / AudioSlicer.SampleRate,
                });
            }
        }
        return result;
    }

    internal static IEnumerable<List<AudioSlicer.Chunk>> BuildBatches(
        List<AudioSlicer.Chunk> chunks, NoteExtractionSettings settings, bool supportsBatch)
    {
        // 与桌面端一致：按长度分组，以最长片段补齐后的总时长控制批大小。
        List<AudioSlicer.Chunk> batch = [];
        int maximumLength = 0;
        foreach (AudioSlicer.Chunk chunk in chunks.OrderBy(chunk => chunk.samples.Length))
        {
            int nextMaximum = Math.Max(maximumLength, chunk.samples.Length);
            double paddedSeconds = (batch.Count + 1) * (double)nextMaximum / AudioSlicer.SampleRate;
            if (batch.Count > 0 && (!supportsBatch || batch.Count >= settings.BatchSize
                || settings.MaxBatchDuration > 0 && paddedSeconds > settings.MaxBatchDuration))
            {
                yield return batch;
                batch = [];
                maximumLength = 0;
            }
            batch.Add(chunk);
            maximumLength = Math.Max(maximumLength, chunk.samples.Length);
        }
        if (batch.Count > 0) yield return batch;
    }

    public static void ExtractPitch(UProject project, UWavePart source, UVoicePart result,
        CancellationToken cancellation, Action<int, int> progress)
    {
        cancellation.ThrowIfCancellationRequested();
        if (result.notes.Count == 0) return;
        using RmvpeTranscriber rmvpe = new();
        using CancellationTokenRegistration registration = cancellation.Register(rmvpe.Interrupt);
        double durationMs = project.timeAxis.MsBetweenTickPos(source.position, source.End);
        double skipMs = source.GetSkipMs(project);
        for (double offset = 0; offset < durationMs; offset += MaximumChunkSeconds * 1000)
        {
            cancellation.ThrowIfCancellationRequested();
            double end = Math.Min(offset + MaximumChunkSeconds * 1000, durationMs);
            RmvpeResult? pitch = rmvpe.Infer(source, skipMs + offset, skipMs + end);
            cancellation.ThrowIfCancellationRequested();
            // Infer 的起点位于源音频；曲线偏移相对于提取结果起点。
            if (pitch != null) ApplyPitch(project, result, pitch, offset);
            progress((int)end, (int)durationMs);
        }
    }


    internal static void ApplyPitch(UProject project, UVoicePart part, RmvpeResult pitch, double offsetMs)
    {
        // Core 的 ApplyToPart 会提交全局撤销命令；后台结果应只修改尚未加入工程的分片。
        if (!project.expressions.TryGetValue(OpenUtau.Core.Format.Ustx.PITD, out UExpressionDescriptor? descriptor))
            throw new InvalidOperationException("Pitch deviation expression is missing.");
        UCurve curve = part.curves.FirstOrDefault(value => value.abbr == descriptor.abbr) ?? new UCurve(descriptor);
        UNote[] notes = part.notes.OrderBy(note => note.position).ToArray();
        int noteIndex = 0;
        double origin = project.timeAxis.TickPosToMsPos(part.position);
        int? lastX = null;
        int lastY = 0;
        int previousNoteIndex = -1;
        for (int i = 0; i < pitch.MidiPitch.Length && noteIndex < notes.Length; i++)
        {
            float midi = pitch.MidiPitch[i];
            if (!float.IsFinite(midi))
            {
                lastX = null;
                continue;
            }
            double time = origin + offsetMs + i * pitch.TimeStepSeconds * 1000;
            int tick = project.timeAxis.MsPosToTickPos(time) - part.position;
            while (noteIndex < notes.Length && tick >= notes[noteIndex].End) noteIndex++;
            if (noteIndex >= notes.Length) break;
            UNote note = notes[noteIndex];
            if (tick < note.position) continue;
            if (noteIndex != previousNoteIndex) lastX = null;
            previousNoteIndex = noteIndex;
            int x = (int)Math.Round((double)tick / UCurve.interval) * UCurve.interval;
            if (x < 0 || x >= part.Duration) continue;
            int y = (int)Math.Round(Math.Clamp((midi - note.tone) * 100, descriptor.min, descriptor.max));
            curve.Set(x, y, lastX ?? x, lastX.HasValue ? lastY : y);
            lastX = x;
            lastY = y;
        }
        if (curve.xs.Count > 0)
        {
            curve.Simplify();
            if (!part.curves.Contains(curve)) part.curves.Add(curve);
        }
    }

}
