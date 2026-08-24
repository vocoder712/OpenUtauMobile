using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Core.Neutrino
{
    [Phonemizer("NEUTRINO Phonemizer", "NEUTRINO", language: "JA")]
    public class NeutrinoPhonemizer : Phonemizer
    {
        private const double DefaultConsonantMs = 60;
        private const int MinimumPhonemeTicks = 10;
        private const int SampleRate = 48000;
        private const int HopSize = 480;

        private NeutrinoSinger neutrinoSinger;
        private readonly object timingLock = new object();
        private readonly Dictionary<int, Phoneme[]> timedPhonemes =
            new Dictionary<int, Phoneme[]>();

        public override void SetSinger(USinger singer)
        {
            lock (timingLock)
            {
                neutrinoSinger = singer as NeutrinoSinger;
                timedPhonemes.Clear();
            }
        }

        public override void SetUp(Note[][] notes, UProject project, UTrack track)
        {
            lock (timingLock)
            {
                timedPhonemes.Clear();
                if (neutrinoSinger == null
                    || notes == null
                    || notes.Length == 0
                    || timeAxis == null)
                {
                    return;
                }

                try
                {
                    neutrinoSinger.EnsureTimingSession();
                    foreach (TimedPhrase phrase in SplitPhrases(notes))
                    {
                        BuildTimedPhonemes(phrase);
                    }
                }
                catch (Exception exception)
                {
                    timedPhonemes.Clear();
                    Log.Warning(
                        exception,
                        "Failed to run NEUTRINO t.bin for the phoneme panel; using estimated positions");
                }
            }
        }

        public override Result Process(
            Note[] notes,
            Note? prev,
            Note? next,
            Note? prevNeighbour,
            Note? nextNeighbour,
            Note[] prevs)
        {
            lock (timingLock)
            {
                if (timedPhonemes.TryGetValue(notes[0].position, out Phoneme[] timed))
                {
                    return new Result()
                    {
                        phonemes = timed.ToArray(),
                    };
                }
            }

            string lyric = string.IsNullOrWhiteSpace(notes[0].phoneticHint)
                ? notes[0].lyric ?? "R"
                : notes[0].phoneticHint;
            string[] phonemes = LyricToPhonemes(lyric);
            int[] positions = DistributePhonemes(phonemes, notes);
            Phoneme[] resultPhonemes = phonemes
                .Select((phoneme, index) => new Phoneme()
                {
                    index = index,
                    phoneme = phoneme,
                    position = positions[index],
                })
                .ToArray();
            return new Result()
            {
                phonemes = PostProcessPhonemePositions(resultPhonemes, notes),
            };
        }

        protected virtual string[] LyricToPhonemes(string lyric)
        {
            return neutrinoSinger?.LyricToPhonemes(lyric)
                ?? NeutrinoPhoneme.KanaToPhonemes(lyric);
        }

        protected virtual Phoneme[] PostProcessPhonemePositions(
            Phoneme[] phonemes,
            Note[] notes)
        {
            return phonemes;
        }

        protected virtual Phoneme[] PostProcessTimedPhonemePositions(
            Phoneme[] phonemes,
            Note[] notes)
        {
            return PostProcessPhonemePositions(phonemes, notes);
        }

        private List<TimedPhrase> SplitPhrases(Note[][] noteGroups)
        {
            List<TimedPhrase> phrases = new List<TimedPhrase>();
            List<Note[]> phrase = null;
            int previousEnd = int.MinValue;
            foreach (Note[] group in noteGroups.Where(group => group.Length > 0))
            {
                int start = group[0].position;
                int end = group[^1].position + group[^1].duration;
                if (phrase == null || start > previousEnd)
                {
                    phrase = new List<Note[]>();
                    int contextStart = previousEnd == int.MinValue ? 0 : previousEnd;
                    phrases.Add(new TimedPhrase(phrase, Math.Min(start, contextStart)));
                }
                phrase.Add(group);
                previousEnd = Math.Max(previousEnd, end);
            }
            return phrases;
        }

        private void BuildTimedPhonemes(TimedPhrase phrase)
        {
            List<Note[]> noteGroups = phrase.NoteGroups;
            List<long> phonemeIds = new List<long>();
            List<float> scorePitchesHz = new List<float>();
            List<float> scoreDurations = new List<float>();
            List<long> phonePositions = new List<long>();
            List<TimedPhoneReference> phoneReferences = new List<TimedPhoneReference>();
            Dictionary<int, Note[]> groupsByPosition = noteGroups
                .ToDictionary(group => group[0].position);
            Dictionary<int, List<Phoneme>> groupedPhonemes = groupsByPosition
                .ToDictionary(pair => pair.Key, pair => new List<Phoneme>());

            foreach (Note[] group in noteGroups)
            {
                string lyric = string.IsNullOrWhiteSpace(group[0].phoneticHint)
                    ? group[0].lyric ?? "R"
                    : group[0].phoneticHint;
                string[] phonemes = LyricToPhonemes(lyric);
                float notePitchHz = phonemes.All(phoneme =>
                    NeutrinoPhoneme.GetPhonemeId(phoneme) == NeutrinoPhoneme.PAU)
                    ? 0
                    : (float)NeutrinoConfig.MidiToFreq(group[0].tone);
                float durationSeconds = Math.Max(
                    0.001f,
                    (float)(GetGroupDurationMs(group) / 1000.0));

                for (int i = 0; i < phonemes.Length; i++)
                {
                    int id = NeutrinoPhoneme.GetPhonemeId(phonemes[i]);
                    phonemeIds.Add(id);
                    scorePitchesHz.Add(id == NeutrinoPhoneme.PAU ? 0 : notePitchHz);
                    scoreDurations.Add(durationSeconds);
                    phonePositions.Add(i);
                    phoneReferences.Add(new TimedPhoneReference()
                    {
                        GroupPosition = group[0].position,
                        Index = i,
                        Phoneme = phonemes[i],
                    });
                }
            }

            int numPhones = phonemeIds.Count;
            if (numPhones == 0)
            {
                return;
            }

            int phraseStartTick = noteGroups[0][0].position;
            double leadingContextSeconds = GetLeadingContextSeconds(
                phraseStartTick,
                phrase.ContextStartTick);
            double[] boundaries = BuildChunkedTimingBoundaries(
                phonemeIds.ToArray(),
                scorePitchesHz.ToArray(),
                scoreDurations.ToArray(),
                phonePositions.ToArray(),
                leadingContextSeconds);
            double phraseStartMs = timeAxis.TickPosToMsPos(phraseStartTick);

            for (int i = 0; i < phoneReferences.Count; i++)
            {
                TimedPhoneReference phoneReference = phoneReferences[i];
                double positionMs = phraseStartMs + boundaries[i] * 1000.0;
                int position = timeAxis.MsPosToTickPos(positionMs)
                    - phoneReference.GroupPosition;
                groupedPhonemes[phoneReference.GroupPosition].Add(new Phoneme()
                {
                    index = phoneReference.Index,
                    phoneme = phoneReference.Phoneme,
                    position = position,
                });
            }

            foreach (KeyValuePair<int, List<Phoneme>> pair in groupedPhonemes)
            {
                if (pair.Value.Count > 0)
                {
                    timedPhonemes[pair.Key] = PostProcessTimedPhonemePositions(
                        pair.Value.ToArray(),
                        groupsByPosition[pair.Key]);
                }
            }
        }

        private double[] BuildChunkedTimingBoundaries(
            long[] phonemeIds,
            float[] scorePitchesHz,
            float[] scoreDurations,
            long[] phonePositions,
            double leadingContextSeconds)
        {
            NeutrinoPhoneChunk[] chunks =
                NeutrinoInferenceUtil.BuildPhoneChunks(phonemeIds);
            double frameSeconds = (double)HopSize / SampleRate;
            return NeutrinoInferenceUtil.BuildTimingBoundaries(
                scoreDurations,
                phonePositions,
                chunks,
                frameSeconds,
                chunk =>
                {
                    float[] chunkPitches = NeutrinoInferenceUtil.Slice(
                        scorePitchesHz,
                        chunk.PhoneStart,
                        chunk.PhoneCount);
                    float[] chunkDurations = NeutrinoInferenceUtil.Slice(
                        scoreDurations,
                        chunk.PhoneStart,
                        chunk.PhoneCount);
                    long[] chunkPositions = NeutrinoInferenceUtil.Slice(
                        phonePositions,
                        chunk.PhoneStart,
                        chunk.PhoneCount);
                    long[] chunkIds = NeutrinoInferenceUtil.Slice(
                        phonemeIds,
                        chunk.PhoneStart,
                        chunk.PhoneCount);
                    List<NamedOnnxValue> inputs = new List<NamedOnnxValue>()
                    {
                        NamedOnnxValue.CreateFromTensor(
                            "electron",
                            new DenseTensor<long>(
                                chunkIds,
                                new int[] { 1, chunk.PhoneCount })),
                        NamedOnnxValue.CreateFromTensor(
                            "muon",
                            new DenseTensor<float>(
                                chunkPitches,
                                new int[] { 1, chunk.PhoneCount })),
                        NamedOnnxValue.CreateFromTensor(
                            "tau",
                            new DenseTensor<float>(
                                chunkDurations,
                                new int[] { 1, chunk.PhoneCount })),
                        NamedOnnxValue.CreateFromTensor(
                            "selectron",
                            new DenseTensor<long>(
                                chunkPositions,
                                new int[] { 1, chunk.PhoneCount })),
                    };
                    return NeutrinoInferenceUtil.RequireTimingBoundaryLength(
                        neutrinoSinger.RunTiming(inputs),
                        chunk.PhoneCount,
                        "NEUTRINO v3 t.bin timing output");
                },
                leadingContextSeconds);
        }

        private double GetGroupDurationMs(Note[] notes)
        {
            double startMs = timeAxis.TickPosToMsPos(notes[0].position);
            Note last = notes[^1];
            double endMs = timeAxis.TickPosToMsPos(last.position + last.duration);
            return Math.Max(1, endMs - startMs);
        }

        private double GetLeadingContextSeconds(int phraseStartTick, int contextStartTick)
        {
            contextStartTick = Math.Max(
                phraseStartTick - NeutrinoRenderer.HeadTicks,
                Math.Min(phraseStartTick, contextStartTick));
            double phraseStartMs = timeAxis.TickPosToMsPos(phraseStartTick);
            double contextStartMs = timeAxis.TickPosToMsPos(contextStartTick);
            return Math.Max(0, (phraseStartMs - contextStartMs) / 1000.0);
        }

        private int[] DistributePhonemes(string[] phonemes, Note[] notes)
        {
            if (phonemes.Length == 0)
            {
                return Array.Empty<int>();
            }
            if (phonemes.Length == 1)
            {
                return new int[] { 0 };
            }

            int noteStart = notes[0].position;
            int noteEnd = notes[^1].position + notes[^1].duration;
            int totalDuration = Math.Max(1, noteEnd - noteStart);
            int lastStart = Math.Max(0, totalDuration - 1);
            if (lastStart < phonemes.Length - 1)
            {
                return EvenlyDistribute(phonemes.Length, lastStart);
            }

            int spacing = Math.Max(
                1,
                Math.Min(MinimumPhonemeTicks, lastStart / (phonemes.Length - 1)));
            int consonantTicks = Math.Clamp(
                DefaultConsonantTicks(noteStart),
                spacing,
                Math.Max(spacing, lastStart));
            int firstVowel = Array.FindIndex(
                phonemes,
                NeutrinoPhoneme.IsVowelPhoneme);
            int[] positions = new int[phonemes.Length];

            if (firstVowel > 0)
            {
                for (int i = 0; i < phonemes.Length; i++)
                {
                    if (i <= firstVowel)
                    {
                        positions[i] = (int)Math.Round(
                            (double)consonantTicks * i / firstVowel);
                    }
                    else
                    {
                        int remaining = phonemes.Length - firstVowel;
                        positions[i] = consonantTicks
                            + (int)Math.Round(
                                (double)(lastStart - consonantTicks)
                                    * (i - firstVowel)
                                    / remaining);
                    }
                }
            }
            else
            {
                positions = EvenlyDistribute(phonemes.Length, lastStart);
            }

            positions[0] = 0;
            for (int i = 1; i < positions.Length; i++)
            {
                int minimum = positions[i - 1] + spacing;
                int maximum = lastStart - spacing * (positions.Length - 1 - i);
                positions[i] = Math.Clamp(positions[i], minimum, Math.Max(minimum, maximum));
            }
            return positions;
        }

        private static int[] EvenlyDistribute(int count, int lastStart)
        {
            int[] positions = new int[count];
            for (int i = 0; i < count; i++)
            {
                positions[i] = (int)Math.Round((double)lastStart * i / (count - 1));
            }
            return positions;
        }

        private int DefaultConsonantTicks(int notePosition)
        {
            if (timeAxis == null)
            {
                return 60;
            }
            double noteMs = timeAxis.TickPosToMsPos(notePosition);
            return Math.Max(
                MinimumPhonemeTicks,
                timeAxis.TicksBetweenMsPos(noteMs, noteMs + DefaultConsonantMs));
        }

        private readonly struct TimedPhrase
        {
            public List<Note[]> NoteGroups { get; }
            public int ContextStartTick { get; }

            public TimedPhrase(List<Note[]> noteGroups, int contextStartTick)
            {
                NoteGroups = noteGroups;
                ContextStartTick = contextStartTick;
            }
        }

        private struct TimedPhoneReference
        {
            public int GroupPosition;
            public int Index;
            public string Phoneme;
        }
    }
}
