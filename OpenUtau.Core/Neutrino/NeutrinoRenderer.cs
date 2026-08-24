using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Neutrino
{
    public sealed class NeutrinoRenderer : IRenderer
    {
        public const int HeadTicks = 480;
        public const int TailTicks = 480;

        private const int SampleRate = 48000;
        private const int OutputSampleRate = 44100;
        private const int HopSize = 480;
        private const int PitchInterval = 5;
        private const int MelBins = 100;
        private const int CacheVersion = 1;
        private const int EdgeSilenceSamples = 240;
        private const int FadeInSamples = 240;
        private const int FadeOutSamples = 240;
        private const float F0Minimum = 40f;
        private const float F0Maximum = 2000f;
        private const float MelspecMinimum = -7f;
        private const float MelspecMaximum = 1f;
        private const float WaveformScale = 0.9885531068f;
        private const float WaveformClamp = 0.9988493919f;

        private static readonly HashSet<string> SupportedExpressions =
            new HashSet<string>()
            {
                Format.Ustx.DYN,
                Format.Ustx.PITD,
            };
        private static readonly object RenderLock = new object();

        public USingerType SingerType => USingerType.Neutrino;
        public bool SupportsRenderPitch => true;

        private sealed class NeutrinoTimingContext
        {
            public long[] PhonemeIds { get; }
            public float[] ScorePitchesHz { get; }
            public float[] ScoreDurations { get; }
            public long[] PhonePositions { get; }
            public float[] TimingDurations { get; }
            public long[] FramePhonemeMap { get; }
            public int TotalFrames { get; }
            public double StartOffsetSeconds { get; }
            public NeutrinoFrameChunk[] Chunks { get; }

            public NeutrinoTimingContext(
                long[] phonemeIds,
                float[] scorePitchesHz,
                float[] scoreDurations,
                long[] phonePositions,
                float[] timingDurations,
                long[] framePhonemeMap,
                int totalFrames,
                double startOffsetSeconds,
                NeutrinoFrameChunk[] chunks)
            {
                PhonemeIds = phonemeIds;
                ScorePitchesHz = scorePitchesHz;
                ScoreDurations = scoreDurations;
                PhonePositions = phonePositions;
                TimingDurations = timingDurations;
                FramePhonemeMap = framePhonemeMap;
                TotalFrames = totalFrames;
                StartOffsetSeconds = startOffsetSeconds;
                Chunks = chunks;
            }
        }

        public bool SupportsExpression(UExpressionDescriptor descriptor)
        {
            return SupportedExpressions.Contains(descriptor.abbr);
        }

        public RenderResult Layout(RenderPhrase phrase)
        {
            double headMs = phrase.positionMs
                - phrase.timeAxis.TickPosToMsPos(phrase.position - HeadTicks);
            double tailMs = phrase.timeAxis.TickPosToMsPos(phrase.end + TailTicks)
                - phrase.endMs;
            return new RenderResult()
            {
                leadingMs = headMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = headMs + phrase.durationMs + tailMs,
            };
        }

        public Task<RenderResult> Render(
            RenderPhrase phrase,
            Progress progress,
            int trackNo,
            CancellationTokenSource cancellation,
            bool isPreRender = false)
        {
            return Task.Run(() =>
            {
                lock (RenderLock)
                {
                    if (cancellation.IsCancellationRequested)
                    {
                        return new RenderResult();
                    }

                    string progressInfo = $"Track {trackNo + 1}: {this} "
                        + $"\"{string.Join(" ", phrase.phones.Select(phone => phone.phoneme))}\"";
                    progress.Complete(0, progressInfo);
                    RenderResult result = Layout(phrase);
                    NeutrinoSinger singer = phrase.singer as NeutrinoSinger
                        ?? throw new InvalidDataException("Singer is not a NEUTRINO v3 singer.");
                    string wavePath = Path.Join(
                        PathManager.Inst.CachePath,
                        $"neutrino-v3-{CacheVersion}-{singer.ModelFingerprint:x16}-{phrase.hash:x16}.wav");
                    phrase.AddCacheFile(wavePath);

                    if (TryLoadWaveCache(wavePath, out float[] cachedSamples))
                    {
                        result.samples = cachedSamples;
                    }
                    if (result.samples == null)
                    {
                        result.samples = InvokeNeutrino(phrase, cancellation);
                        if (result.samples != null)
                        {
                            SaveWaveCache(wavePath, result.samples);
                        }
                    }
                    if (result.samples != null)
                    {
                        Renderers.ApplyDynamics(phrase, result);
                    }
                    progress.Complete(phrase.phones.Length, progressInfo);
                    return result;
                }
            });
        }

        private float[] InvokeNeutrino(
            RenderPhrase phrase,
            CancellationTokenSource cancellation)
        {
            if (cancellation.IsCancellationRequested)
            {
                return null;
            }

            NeutrinoTimingContext timing = BuildTimingContext(phrase);
            if (timing.PhonemeIds.Length == 0)
            {
                return Array.Empty<float>();
            }

            float[] f0 = BuildEditorF0(phrase, timing);
            ClampF0(f0);
            NeutrinoSinger singer = phrase.singer as NeutrinoSinger
                ?? throw new InvalidDataException("Singer is not a NEUTRINO v3 singer.");
            float[] waveform = new float[timing.TotalFrames * HopSize];

            foreach (NeutrinoFrameChunk chunk in timing.Chunks)
            {
                if (!chunk.IsActive || chunk.FrameCount <= 0)
                {
                    continue;
                }
                if (cancellation.IsCancellationRequested)
                {
                    return null;
                }

                NeutrinoTimingContext chunkTiming = BuildChunkTimingContext(timing, chunk);
                float[] chunkF0 = NeutrinoInferenceUtil.Slice(
                    f0,
                    chunk.FrameStart,
                    chunk.FrameCount);
                float[] chunkWaveform = RunAcousticChunk(
                    singer,
                    chunkTiming,
                    chunkF0,
                    cancellation);
                if (chunkWaveform == null)
                {
                    return null;
                }
                Array.Copy(
                    chunkWaveform,
                    0,
                    waveform,
                    chunk.FrameStart * HopSize,
                    chunkWaveform.Length);
            }

            RenderResult layout = Layout(phrase);
            double waveformOffsetMs = layout.leadingMs
                + timing.StartOffsetSeconds * 1000.0;
            int headSamples = Math.Max(
                0,
                (int)(waveformOffsetMs / 1000.0 * SampleRate));
            int tailSamples = Math.Max(
                0,
                (int)(layout.estimatedLengthMs / 1000.0 * SampleRate)
                    - headSamples
                    - waveform.Length);
            float[] result = new float[headSamples + waveform.Length + tailSamples];
            Array.Copy(waveform, 0, result, headSamples, waveform.Length);

            if (SampleRate != OutputSampleRate)
            {
                NWaves.Signals.DiscreteSignal signal =
                    new NWaves.Signals.DiscreteSignal(SampleRate, result);
                signal = NWaves.Operations.Operation.Resample(signal, OutputSampleRate);
                result = signal.Samples;
            }
            return result;
        }

        private float[] RunAcousticChunk(
            NeutrinoSinger singer,
            NeutrinoTimingContext timing,
            float[] f0,
            CancellationTokenSource cancellation)
        {
            int numPhones = timing.PhonemeIds.Length;
            int totalFrames = timing.TotalFrames;
            List<NamedOnnxValue> melspecInputs = new List<NamedOnnxValue>()
            {
                NamedOnnxValue.CreateFromTensor(
                    "electron",
                    new DenseTensor<long>(
                        timing.PhonemeIds,
                        new int[] { 1, numPhones })),
                NamedOnnxValue.CreateFromTensor(
                    "muon",
                    new DenseTensor<float>(
                        timing.TimingDurations,
                        new int[] { 1, numPhones })),
                NamedOnnxValue.CreateFromTensor(
                    "tau",
                    new DenseTensor<float>(
                        timing.ScorePitchesHz,
                        new int[] { 1, numPhones })),
                NamedOnnxValue.CreateFromTensor(
                    "selectron",
                    new DenseTensor<float>(
                        timing.ScoreDurations,
                        new int[] { 1, numPhones })),
                NamedOnnxValue.CreateFromTensor(
                    "smuon",
                    new DenseTensor<long>(
                        timing.PhonePositions,
                        new int[] { 1, numPhones })),
                NamedOnnxValue.CreateFromTensor(
                    "stau",
                    new DenseTensor<long>(
                        timing.FramePhonemeMap,
                        new int[] { 1, totalFrames })),
                NamedOnnxValue.CreateFromTensor(
                    "photon",
                    new DenseTensor<float>(f0, new int[] { 1, totalFrames })),
            };
            float[] melSpectrogram = NeutrinoInferenceUtil.RequireLength(
                singer.RunMelspec(melspecInputs),
                totalFrames * MelBins,
                "NEUTRINO v3 s.bin mel output");
            ClampMelspec(melSpectrogram);

            if (cancellation.IsCancellationRequested)
            {
                return null;
            }

            float[] vocoderInput = new float[totalFrames * (MelBins + 1)];
            for (int frame = 0; frame < totalFrames; frame++)
            {
                for (int bin = 0; bin < MelBins; bin++)
                {
                    vocoderInput[frame * (MelBins + 1) + bin] =
                        melSpectrogram[frame * MelBins + bin];
                }
                vocoderInput[frame * (MelBins + 1) + MelBins] = f0[frame];
            }
            List<NamedOnnxValue> vocoderInputs = new List<NamedOnnxValue>()
            {
                NamedOnnxValue.CreateFromTensor(
                    "input",
                    new DenseTensor<float>(
                        vocoderInput,
                        new int[] { 1, totalFrames, MelBins + 1 })),
            };
            float[] waveform = NeutrinoInferenceUtil.RequireLength(
                singer.RunVocoder(vocoderInputs),
                totalFrames * HopSize,
                "NEUTRINO v3 v.bin waveform output");
            if (cancellation.IsCancellationRequested)
            {
                return null;
            }
            PostProcessWaveform(waveform);
            return waveform;
        }

        private NeutrinoTimingContext BuildTimingContext(RenderPhrase phrase)
        {
            NeutrinoSinger singer = phrase.singer as NeutrinoSinger
                ?? throw new InvalidDataException("Singer is not a NEUTRINO v3 singer.");
            (
                long[] phonemeIds,
                float[] scorePitchesHz,
                float[] scoreDurations,
                long[] phonePositions,
                double?[] manualBoundaries) = BuildPhonemeSequence(phrase);

            int numPhones = phonemeIds.Length;
            if (numPhones == 0)
            {
                return new NeutrinoTimingContext(
                    phonemeIds,
                    scorePitchesHz,
                    scoreDurations,
                    phonePositions,
                    Array.Empty<float>(),
                    Array.Empty<long>(),
                    0,
                    0,
                    Array.Empty<NeutrinoFrameChunk>());
            }

            NeutrinoPhoneChunk[] phoneChunks =
                NeutrinoInferenceUtil.BuildPhoneChunks(phonemeIds);
            double frameSeconds = (double)HopSize / SampleRate;
            double scoreOriginMs = GetScoreOriginMs(phrase);
            double leadingContextSeconds = GetLeadingContextSeconds(phrase);
            double[] boundaries = NeutrinoInferenceUtil.BuildTimingBoundaries(
                scoreDurations,
                phonePositions,
                phoneChunks,
                frameSeconds,
                chunk =>
                {
                    float[] chunkPitches = NeutrinoInferenceUtil.Slice(
                        scorePitchesHz,
                        chunk.PhoneStart,
                        chunk.PhoneCount);
                    float[] chunkScoreDurations = NeutrinoInferenceUtil.Slice(
                        scoreDurations,
                        chunk.PhoneStart,
                        chunk.PhoneCount);
                    long[] chunkPhonePositions = NeutrinoInferenceUtil.Slice(
                        phonePositions,
                        chunk.PhoneStart,
                        chunk.PhoneCount);
                    long[] chunkPhonemeIds = NeutrinoInferenceUtil.Slice(
                        phonemeIds,
                        chunk.PhoneStart,
                        chunk.PhoneCount);
                    List<NamedOnnxValue> timingInputs = new List<NamedOnnxValue>()
                    {
                        NamedOnnxValue.CreateFromTensor(
                            "electron",
                            new DenseTensor<long>(
                                chunkPhonemeIds,
                                new int[] { 1, chunk.PhoneCount })),
                        NamedOnnxValue.CreateFromTensor(
                            "muon",
                            new DenseTensor<float>(
                                chunkPitches,
                                new int[] { 1, chunk.PhoneCount })),
                        NamedOnnxValue.CreateFromTensor(
                            "tau",
                            new DenseTensor<float>(
                                chunkScoreDurations,
                                new int[] { 1, chunk.PhoneCount })),
                        NamedOnnxValue.CreateFromTensor(
                            "selectron",
                            new DenseTensor<long>(
                                chunkPhonePositions,
                                new int[] { 1, chunk.PhoneCount })),
                    };
                    return NeutrinoInferenceUtil.RequireTimingBoundaryLength(
                        singer.RunTiming(timingInputs),
                        chunk.PhoneCount,
                        "NEUTRINO v3 t.bin timing output");
                },
                leadingContextSeconds);

            ApplyManualBoundaryOverrides(
                boundaries,
                manualBoundaries,
                leadingContextSeconds);
            double boundaryStartSeconds =
                NeutrinoInferenceUtil.NormalizeBoundaryStart(boundaries);
            double startOffsetSeconds =
                (scoreOriginMs - phrase.positionMs) / 1000.0
                + boundaryStartSeconds;
            float[] timingDurations = BuildTimingDurations(boundaries);
            int totalFrames = Math.Max(
                1,
                (int)Math.Round(boundaries[^1] * SampleRate / HopSize));
            long[] framePhonemeMap = BuildFramePhonemeMap(
                timingDurations,
                totalFrames);
            NeutrinoFrameChunk[] frameChunks = NeutrinoInferenceUtil.BuildFrameChunks(
                phoneChunks,
                boundaries,
                totalFrames,
                frameSeconds);
            return new NeutrinoTimingContext(
                phonemeIds,
                scorePitchesHz,
                scoreDurations,
                phonePositions,
                timingDurations,
                framePhonemeMap,
                totalFrames,
                startOffsetSeconds,
                frameChunks);
        }

        private NeutrinoTimingContext BuildChunkTimingContext(
            NeutrinoTimingContext timing,
            NeutrinoFrameChunk chunk)
        {
            float[] timingDurations = NeutrinoInferenceUtil.Slice(
                timing.TimingDurations,
                chunk.PhoneStart,
                chunk.PhoneCount);
            return new NeutrinoTimingContext(
                NeutrinoInferenceUtil.Slice(
                    timing.PhonemeIds,
                    chunk.PhoneStart,
                    chunk.PhoneCount),
                NeutrinoInferenceUtil.Slice(
                    timing.ScorePitchesHz,
                    chunk.PhoneStart,
                    chunk.PhoneCount),
                NeutrinoInferenceUtil.Slice(
                    timing.ScoreDurations,
                    chunk.PhoneStart,
                    chunk.PhoneCount),
                NeutrinoInferenceUtil.Slice(
                    timing.PhonePositions,
                    chunk.PhoneStart,
                    chunk.PhoneCount),
                timingDurations,
                BuildFramePhonemeMap(timingDurations, chunk.FrameCount),
                chunk.FrameCount,
                0,
                Array.Empty<NeutrinoFrameChunk>());
        }

        private float[] RunPredictedF0(
            RenderPhrase phrase,
            NeutrinoTimingContext timing)
        {
            if (timing.TotalFrames <= 0 || timing.PhonemeIds.Length == 0)
            {
                return Array.Empty<float>();
            }

            NeutrinoSinger singer = phrase.singer as NeutrinoSinger
                ?? throw new InvalidDataException("Singer is not a NEUTRINO v3 singer.");
            float[] f0 = new float[timing.TotalFrames];
            foreach (NeutrinoFrameChunk chunk in timing.Chunks)
            {
                if (!chunk.IsActive || chunk.FrameCount <= 0)
                {
                    continue;
                }

                NeutrinoTimingContext chunkTiming = BuildChunkTimingContext(timing, chunk);
                int numPhones = chunkTiming.PhonemeIds.Length;
                List<NamedOnnxValue> pitchInputs = new List<NamedOnnxValue>()
                {
                    NamedOnnxValue.CreateFromTensor(
                        "electron",
                        new DenseTensor<long>(
                            chunkTiming.PhonemeIds,
                            new int[] { 1, numPhones })),
                    NamedOnnxValue.CreateFromTensor(
                        "muon",
                        new DenseTensor<float>(
                            chunkTiming.TimingDurations,
                            new int[] { 1, numPhones })),
                    NamedOnnxValue.CreateFromTensor(
                        "tau",
                        new DenseTensor<float>(
                            chunkTiming.ScorePitchesHz,
                            new int[] { 1, numPhones })),
                    NamedOnnxValue.CreateFromTensor(
                        "selectron",
                        new DenseTensor<float>(
                            chunkTiming.ScoreDurations,
                            new int[] { 1, numPhones })),
                    NamedOnnxValue.CreateFromTensor(
                        "smuon",
                        new DenseTensor<long>(
                            chunkTiming.PhonePositions,
                            new int[] { 1, numPhones })),
                    NamedOnnxValue.CreateFromTensor(
                        "stau",
                        new DenseTensor<long>(
                            chunkTiming.FramePhonemeMap,
                            new int[] { 1, chunkTiming.TotalFrames })),
                };
                float[] chunkF0 = NeutrinoInferenceUtil.RequireLength(
                    singer.RunPitch(pitchInputs),
                    chunkTiming.TotalFrames,
                    "NEUTRINO v3 p.bin F0 output");
                ClampF0(chunkF0);
                Array.Copy(chunkF0, 0, f0, chunk.FrameStart, chunkF0.Length);
            }
            return f0;
        }

        private float[] BuildEditorF0(
            RenderPhrase phrase,
            NeutrinoTimingContext timing)
        {
            float[] f0 = new float[timing.TotalFrames];
            for (int frame = 0; frame < f0.Length; frame++)
            {
                int phoneIndex = GetFramePhoneIndex(timing, frame);
                if (phoneIndex < 0
                    || timing.PhonemeIds[phoneIndex] == NeutrinoPhoneme.PAU
                    || timing.ScorePitchesHz[phoneIndex] <= 0)
                {
                    continue;
                }

                if (phrase.pitches == null || phrase.pitches.Length == 0)
                {
                    f0[frame] = timing.ScorePitchesHz[phoneIndex];
                    continue;
                }
                int pitchIndex = GetFramePitchIndex(phrase, timing, frame);
                f0[frame] = (float)MusicMath.ToneToFreq(
                    phrase.pitches[pitchIndex] * 0.01);
            }
            return f0;
        }

        private int GetFramePhoneIndex(NeutrinoTimingContext timing, int frame)
        {
            if (timing.FramePhonemeMap.Length == 0 || timing.PhonemeIds.Length == 0)
            {
                return -1;
            }
            int mapIndex = Math.Clamp(frame, 0, timing.FramePhonemeMap.Length - 1);
            return Math.Clamp(
                (int)timing.FramePhonemeMap[mapIndex] - 1,
                0,
                timing.PhonemeIds.Length - 1);
        }

        private int GetFramePitchIndex(
            RenderPhrase phrase,
            NeutrinoTimingContext timing,
            int frame)
        {
            int ticks = GetFramePitchTick(phrase, timing, frame);
            return Math.Clamp(
                (int)(ticks / (double)PitchInterval),
                0,
                phrase.pitches.Length - 1);
        }

        private int GetFramePitchTick(
            RenderPhrase phrase,
            NeutrinoTimingContext timing,
            int frame)
        {
            double frameMs = 1000.0 * HopSize / SampleRate;
            double positionMs = phrase.positionMs
                - phrase.leadingMs
                + timing.StartOffsetSeconds * 1000.0
                + frame * frameMs;
            return phrase.timeAxis.MsPosToTickPos(positionMs)
                - (phrase.position - phrase.leading);
        }

        private int GetFrameResultTick(
            RenderPhrase phrase,
            NeutrinoTimingContext timing,
            int frame)
        {
            double frameMs = 1000.0 * HopSize / SampleRate;
            double positionMs = phrase.positionMs
                - phrase.leadingMs
                + timing.StartOffsetSeconds * 1000.0
                + frame * frameMs;
            return phrase.timeAxis.MsPosToTickPos(positionMs) - phrase.position;
        }

        private (
            long[] PhonemeIds,
            float[] ScorePitchesHz,
            float[] ScoreDurations,
            long[] PhonePositions,
            double?[] ManualBoundaries) BuildPhonemeSequence(RenderPhrase phrase)
        {
            List<long> phonemeIds = new List<long>();
            List<float> scorePitchesHz = new List<float>();
            List<float> scoreDurations = new List<float>();
            List<long> phonePositions = new List<long>();
            List<double?> manualBoundaries = new List<double?>();
            int lastNoteIndex = -1;
            int positionInNote = 0;
            double scoreOriginMs = GetScoreOriginMs(phrase);

            foreach (RenderPhone phone in phrase.phones)
            {
                string[] phonePhonemes =
                    NeutrinoPhoneme.RenderPhoneToPhonemes(phone.phoneme);
                int noteIndex = Math.Clamp(phone.noteIndex, 0, phrase.notes.Length - 1);
                if (noteIndex != lastNoteIndex)
                {
                    positionInNote = 0;
                    lastNoteIndex = noteIndex;
                }

                RenderNote note = phrase.notes[noteIndex];
                float notePitchHz = phonePhonemes.All(phoneme =>
                    NeutrinoPhoneme.GetPhonemeId(phoneme) == NeutrinoPhoneme.PAU)
                    ? 0
                    : (float)NeutrinoConfig.MidiToFreq(
                        note.tone + note.tuning * 0.01f);
                float noteDurationSeconds = Math.Max(
                    0.001f,
                    (float)(GetExtendedNoteDurationMs(phrase.notes, noteIndex) / 1000.0));

                for (int i = 0; i < phonePhonemes.Length; i++)
                {
                    int id = NeutrinoPhoneme.GetPhonemeId(phonePhonemes[i]);
                    phonemeIds.Add(id);
                    scorePitchesHz.Add(id == NeutrinoPhoneme.PAU ? 0 : notePitchHz);
                    scoreDurations.Add(noteDurationSeconds);
                    phonePositions.Add(positionInNote++);
                    manualBoundaries.Add(phone.positionOverridden && i == 0
                        ? (phone.positionMs - scoreOriginMs) / 1000.0
                        : null);
                }
            }

            if (phonemeIds.Count == 0)
            {
                return (
                    Array.Empty<long>(),
                    Array.Empty<float>(),
                    Array.Empty<float>(),
                    Array.Empty<long>(),
                    new double?[] { null });
            }
            manualBoundaries.Add(null);
            return (
                phonemeIds.ToArray(),
                scorePitchesHz.ToArray(),
                scoreDurations.ToArray(),
                phonePositions.ToArray(),
                manualBoundaries.ToArray());
        }

        private int GetFirstPhoneNoteIndex(RenderPhrase phrase)
        {
            if (phrase.phones.Length == 0 || phrase.notes.Length == 0)
            {
                return 0;
            }
            return Math.Clamp(
                phrase.phones[0].noteIndex,
                0,
                phrase.notes.Length - 1);
        }

        private double GetScoreOriginMs(RenderPhrase phrase)
        {
            if (phrase.notes.Length == 0)
            {
                return phrase.positionMs;
            }
            return phrase.notes[GetFirstPhoneNoteIndex(phrase)].positionMs;
        }

        private double GetLeadingContextSeconds(RenderPhrase phrase)
        {
            if (phrase.notes.Length == 0)
            {
                return 0;
            }
            RenderNote firstNote = phrase.notes[GetFirstPhoneNoteIndex(phrase)];
            int scoreOriginTick = phrase.position + firstNote.position;
            double contextStartMs = phrase.timeAxis.TickPosToMsPos(
                scoreOriginTick - HeadTicks);
            double maximumContextMs = Math.Max(
                0,
                firstNote.positionMs - contextStartMs);
            return Math.Min(maximumContextMs, phrase.availableLeadingMs) / 1000.0;
        }

        private double GetExtendedNoteDurationMs(RenderNote[] notes, int noteIndex)
        {
            double endMs = notes[noteIndex].endMs;
            for (int i = noteIndex + 1;
                i < notes.Length
                    && NeutrinoInferenceUtil.IsExtensionLyric(notes[i].lyric);
                i++)
            {
                endMs = notes[i].endMs;
            }
            return Math.Max(1, endMs - notes[noteIndex].positionMs);
        }

        private static void ApplyManualBoundaryOverrides(
            double[] boundaries,
            double?[] manualBoundaries,
            double leadingContextSeconds)
        {
            if (manualBoundaries == null || manualBoundaries.Length == 0)
            {
                return;
            }

            double frameSeconds = (double)HopSize / SampleRate;
            int count = Math.Min(boundaries.Length - 1, manualBoundaries.Length - 1);
            for (int i = 0; i < count; i++)
            {
                if (!manualBoundaries[i].HasValue)
                {
                    continue;
                }
                double minimum = i == 0
                    ? Math.Min(0, -Math.Max(0, leadingContextSeconds) + frameSeconds)
                    : boundaries[i - 1] + frameSeconds;
                double maximum = boundaries[i + 1] - frameSeconds;
                if (maximum < minimum)
                {
                    maximum = minimum;
                }
                boundaries[i] = Math.Round(
                    Math.Clamp(
                        manualBoundaries[i].Value,
                        minimum,
                        maximum) * 1000.0) / 1000.0;
            }

            for (int i = 1; i < boundaries.Length; i++)
            {
                if (boundaries[i] <= boundaries[i - 1])
                {
                    boundaries[i] = Math.Round(
                        (boundaries[i - 1] + frameSeconds) * 1000.0) / 1000.0;
                }
            }
        }

        private static float[] BuildTimingDurations(double[] boundaries)
        {
            float[] durations = new float[boundaries.Length - 1];
            for (int i = 0; i < durations.Length; i++)
            {
                durations[i] = Math.Max(
                    0.001f,
                    (float)(boundaries[i + 1] - boundaries[i]));
            }
            return durations;
        }

        internal static long[] BuildFramePhonemeMap(
            float[] timingDurations,
            int totalFrames)
        {
            long[] frameMap = new long[totalFrames];
            double frameSeconds = (double)HopSize / SampleRate;
            double time = 0;
            for (int phone = 0; phone < timingDurations.Length; phone++)
            {
                int startFrame = (int)Math.Round(time / frameSeconds);
                time += timingDurations[phone];
                int endFrame = Math.Min(
                    totalFrames,
                    (int)Math.Round(time / frameSeconds));
                for (int frame = startFrame; frame < endFrame; frame++)
                {
                    frameMap[frame] = phone + 1;
                }
            }

            long finalPhone = timingDurations.Length;
            for (int frame = 0; frame < totalFrames; frame++)
            {
                if (frameMap[frame] == 0)
                {
                    frameMap[frame] = finalPhone;
                }
            }
            return frameMap;
        }

        private static void ClampF0(float[] f0)
        {
            for (int i = 0; i < f0.Length; i++)
            {
                if (!float.IsFinite(f0[i]) || f0[i] < F0Minimum)
                {
                    f0[i] = 0;
                }
                else if (f0[i] > F0Maximum)
                {
                    f0[i] = F0Maximum;
                }
            }
        }

        private static void ClampMelspec(float[] melSpectrogram)
        {
            for (int i = 0; i < melSpectrogram.Length; i++)
            {
                if (!float.IsFinite(melSpectrogram[i])
                    || melSpectrogram[i] < MelspecMinimum)
                {
                    melSpectrogram[i] = MelspecMinimum;
                }
                else if (melSpectrogram[i] > MelspecMaximum)
                {
                    melSpectrogram[i] = MelspecMaximum;
                }
            }
        }

        private static void PostProcessWaveform(float[] waveform)
        {
            int edge = Math.Min(EdgeSilenceSamples, waveform.Length / 2);
            for (int i = 0; i < edge; i++)
            {
                waveform[i] = 0;
                waveform[waveform.Length - 1 - i] = 0;
            }

            int fadeIn = Math.Min(
                FadeInSamples,
                Math.Max(0, waveform.Length - edge));
            for (int i = 0; i < fadeIn; i++)
            {
                int index = edge + i;
                if (index >= waveform.Length)
                {
                    break;
                }
                float gain = (float)Math.Pow((double)i / FadeInSamples, 2.0);
                waveform[index] *= gain;
            }

            int fadeOut = Math.Min(
                FadeOutSamples,
                Math.Max(0, waveform.Length - edge));
            for (int i = 0; i < fadeOut; i++)
            {
                int index = waveform.Length - edge - 1 - i;
                if (index < 0)
                {
                    break;
                }
                float gain = (float)Math.Pow((double)i / FadeOutSamples, 2.0);
                waveform[index] *= gain;
            }

            for (int i = 0; i < waveform.Length; i++)
            {
                float value = waveform[i] * WaveformScale;
                if (!float.IsFinite(value))
                {
                    value = 0;
                }
                waveform[i] = Math.Clamp(value, -WaveformClamp, WaveformClamp);
            }
        }

        private static bool TryLoadWaveCache(string path, out float[] samples)
        {
            samples = null;
            if (!File.Exists(path))
            {
                return false;
            }
            try
            {
                using WaveStream waveStream = Wave.OpenFile(path);
                samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                return true;
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "Failed to read NEUTRINO cache {CachePath}; rerendering",
                    path);
                return false;
            }
        }

        private static void SaveWaveCache(string path, float[] samples)
        {
            WaveSource source = new WaveSource(0, 0, 0, 1);
            source.SetSamples(samples);
            WaveFileWriter.CreateWaveFile16(
                path,
                new ExportAdapter(source).ToMono(1, 0));
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase)
        {
            NeutrinoTimingContext timing = BuildTimingContext(phrase);
            if (timing.TotalFrames <= 0)
            {
                return null;
            }

            float[] f0 = RunPredictedF0(phrase, timing);
            RenderPitchResult result = new RenderPitchResult()
            {
                ticks = new float[f0.Length],
                tones = new float[f0.Length],
            };
            for (int frame = 0; frame < f0.Length; frame++)
            {
                result.ticks[frame] = GetFrameResultTick(phrase, timing, frame);
                int phoneIndex = GetFramePhoneIndex(timing, frame);
                bool voiced = phoneIndex >= 0
                    && timing.PhonemeIds[phoneIndex] != NeutrinoPhoneme.PAU
                    && timing.ScorePitchesHz[phoneIndex] > 0
                    && f0[frame] > 0;
                result.tones[frame] = voiced
                    ? (float)MusicMath.FreqToTone(f0[frame])
                    : -1f;
            }
            return result;
        }

        public UExpressionDescriptor[] GetSuggestedExpressions(
            USinger singer,
            URenderSettings renderSettings)
        {
            return Array.Empty<UExpressionDescriptor>();
        }

        public override string ToString()
        {
            return Renderers.NEUTRINO;
        }
    }
}
