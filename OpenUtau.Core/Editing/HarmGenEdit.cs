using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Util;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Editing {
    public sealed class KeyEstimate {
        public int KeyIndex { get; }
        public string Mode { get; }
        public double Score { get; }
        public double Confidence { get; }

        public KeyEstimate(int keyIndex, string mode, double score, double confidence) {
            KeyIndex = keyIndex;
            Mode = mode;
            Score = score;
            Confidence = confidence;
        }
    }

    public static class HarmonyAnalysis {
        private static readonly double[] MajorProfile = {
            6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 3.78, 2.14, 4.04, 2.0, 3.5,
        };
        private static readonly double[] MinorProfile = {
            6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 2.53, 4.48, 2.42, 3.17, 2.35,
        };

        public static IReadOnlyList<KeyEstimate> DetectKeys(
            IEnumerable<(int Tone, int Duration)> notes,
            int topN = 3) {
            var histogram = new double[12];
            foreach (var note in notes) {
                histogram[PositiveMod(note.Tone, 12)] += Math.Max(1, note.Duration);
            }
            if (histogram.Sum() <= 0) {
                return Array.Empty<KeyEstimate>();
            }

            var scores = new List<(int KeyIndex, string Mode, double Score)>();
            for (int keyIndex = 0; keyIndex < 12; keyIndex++) {
                scores.Add((keyIndex, "major", Correlation(histogram, MajorProfile, keyIndex)));
                scores.Add((keyIndex, "minor", Correlation(histogram, MinorProfile, keyIndex)));
            }
            scores = scores.OrderByDescending(item => item.Score).ToList();

            // A softmax makes ambiguity visible instead of reporting the best candidate as 100% by definition.
            const double temperature = 8.0;
            double maxScore = scores[0].Score;
            var weights = scores.Select(item => Math.Exp((item.Score - maxScore) * temperature)).ToArray();
            double totalWeight = weights.Sum();
            int count = Math.Clamp(topN, 1, scores.Count);
            return scores.Take(count)
                .Select((item, index) => new KeyEstimate(
                    item.KeyIndex,
                    item.Mode,
                    item.Score,
                    totalWeight > 0 ? weights[index] / totalWeight : 0))
                .ToList();
        }

        internal static int PositiveMod(int value, int divisor) {
            int result = value % divisor;
            return result < 0 ? result + divisor : result;
        }

        private static double Correlation(double[] histogram, double[] profile, int keyIndex) {
            double histogramMean = histogram.Average();
            double profileMean = profile.Average();
            double numerator = 0;
            double histogramVariance = 0;
            double profileVariance = 0;
            for (int relativePc = 0; relativePc < 12; relativePc++) {
                double histogramValue = histogram[(keyIndex + relativePc) % 12] - histogramMean;
                double profileValue = profile[relativePc] - profileMean;
                numerator += histogramValue * profileValue;
                histogramVariance += histogramValue * histogramValue;
                profileVariance += profileValue * profileValue;
            }
            double denominator = Math.Sqrt(histogramVariance * profileVariance);
            return denominator > 1e-9 ? numerator / denominator : 0;
        }
    }

    public class HarmGenConfig {
        public List<int> TrackIndexes { get; set; } = new List<int>();
        public int NumVoices { get; set; } = 2;
        public string StyleName { get; set; } = "Pop";
        public bool SustainedChoir { get; set; } = false;
        public int WindowSizeIndex { get; set; } = 1;
        public bool UseManualKey { get; set; } = false;
        public int ManualKeyIndex { get; set; } = 0;
        public string ManualMode { get; set; } = "major";
        public bool BlankLyrics { get; set; } = false;
        public string LyricReplacement { get; set; } = "";
        public bool AvoidCrossing { get; set; } = true;
        public bool RangesRelative { get; set; } = true;
        public bool UseDefaultRanges { get; set; } = true;
        public List<(int Min, int Max)> VoiceRanges { get; set; } = new List<(int, int)>();
        public int DensityMinDuration { get; set; } = 0;
        public int MelismaNoteDuration { get; set; } = 0;
        public int MelismaRunLen { get; set; } = 4;
        public int HumanizeTiming { get; set; } = 0;
        public int HumanizeVelocity { get; set; } = 0;
        public int Seed { get; set; } = 0;
        public double HarmonyVolume { get; set; } = -5.0;
        public bool AllowSus { get; set; } = true;
        public bool AllowSevenths { get; set; } = true;
        public double ChordConfidenceThreshold { get; set; } = 0.45;
        public double StrongBeatWeight { get; set; } = 1.0;
        public double WeakBeatWeight { get; set; } = 0.5;
        public string? PhonemizerFullName { get; set; } = null;
    }

    internal sealed class NoteEvent {
        public int Tone { get; }
        public int Position { get; }
        public int Duration { get; }
        public int AbsPosition { get; }
        public string Lyric { get; }
        public UVoicePart VoicePart { get; }
        public UNote Note { get; }
        public int NoteIndex { get; }
        public int VoicePartIndex { get; }

        public NoteEvent(int tone, int position, int duration, int absPosition, string lyric, UVoicePart voicePart,
            UNote note, int noteIndex, int voicePartIndex) {
            Tone = tone;
            Position = position;
            Duration = duration;
            AbsPosition = absPosition;
            Lyric = lyric;
            VoicePart = voicePart;
            Note = note;
            NoteIndex = noteIndex;
            VoicePartIndex = voicePartIndex;
        }
    }

    internal sealed class ChordInfo {
        public int Root { get; }
        public string Quality { get; }
        public List<int> Tones { get; }

        public ChordInfo(int root, string quality, List<int> tones) {
            Root = root;
            Quality = quality;
            Tones = tones;
        }
    }

    internal sealed class ChordWindow {
        public int Start { get; }
        public int End { get; }
        public ChordInfo Chord { get; }
        public double Confidence { get; }
        public bool LowConfidence { get; }

        public ChordWindow(int start, int end, ChordInfo chord, double confidence, bool lowConfidence) {
            Start = start;
            End = end;
            Chord = chord;
            Confidence = confidence;
            LowConfidence = lowConfidence;
        }
    }

    internal sealed class ChordCandidate {
        public ChordInfo Chord { get; }
        public bool IsDiatonic { get; }
        public double Prior { get; }

        public ChordCandidate(ChordInfo chord, bool isDiatonic, double prior) {
            Chord = chord;
            IsDiatonic = isDiatonic;
            Prior = prior;
        }
    }

    internal sealed class StyleProfile {
        public string Name { get; }
        public List<int> PreferredIntervals { get; }
        public double MovementWeight { get; }
        public double DissonanceWeight { get; }
        public double ChordBonus { get; }
        public double ScalePenalty { get; }
        public double Jitter { get; }
        public double WideIntervalPenalty { get; }

        public StyleProfile(
            string name,
            List<int> preferredIntervals,
            double movementWeight,
            double dissonanceWeight,
            double chordBonus,
            double scalePenalty,
            double jitter,
            double wideIntervalPenalty) {
            Name = name;
            PreferredIntervals = preferredIntervals;
            MovementWeight = movementWeight;
            DissonanceWeight = dissonanceWeight;
            ChordBonus = chordBonus;
            ScalePenalty = scalePenalty;
            Jitter = jitter;
            WideIntervalPenalty = wideIntervalPenalty;
        }
    }

    internal static class StyleProfiles {
        private static readonly Dictionary<string, StyleProfile> Profiles = new Dictionary<string, StyleProfile> {
            ["Pop"] = new StyleProfile(
                name: "Pop",
                preferredIntervals: new List<int> { 3, 4, 7, 8, 9 },
                movementWeight: 0.35,
                dissonanceWeight: 2.2,
                chordBonus: -1.2,
                scalePenalty: 0.4,
                jitter: 0.0,
                wideIntervalPenalty: 0.6),
            ["Choir"] = new StyleProfile(
                name: "Choir",
                preferredIntervals: new List<int> { 3, 4, 5, 7, 8, 9, 12 },
                movementWeight: 0.25,
                dissonanceWeight: 2.6,
                chordBonus: -1.4,
                scalePenalty: 0.6,
                jitter: 0.0,
                wideIntervalPenalty: 0.8),
            ["Cinematic"] = new StyleProfile(
                name: "Cinematic",
                preferredIntervals: new List<int> { 7, 12, 19 },
                movementWeight: 0.18,
                dissonanceWeight: 1.8,
                chordBonus: -1.0,
                scalePenalty: 0.5,
                jitter: 0.0,
                wideIntervalPenalty: 0.4),
            ["Randomized Human"] = new StyleProfile(
                name: "Randomized Human",
                preferredIntervals: new List<int> { 3, 4, 7, 8, 9 },
                movementWeight: 0.35,
                dissonanceWeight: 2.1,
                chordBonus: -1.1,
                scalePenalty: 0.4,
                jitter: 0.6,
                wideIntervalPenalty: 0.6),
        };

        public static StyleProfile? GetProfile(string name) {
            if (Profiles.TryGetValue(name, out var profile)) {
                return profile;
            }
            return null;
        }

        public static string[] Names => Profiles.Keys.ToArray();
    }

    internal sealed class HarmonyConfig {
        public int NumVoices { get; }
        public StyleProfile Style { get; }
        public int KeyIndex { get; }
        public string Mode { get; }
        public bool UseManualKey { get; }
        public List<int> ScalePcs { get; }
        public int TicksPerBeat { get; }
        public int BeatsPerBar { get; }
        public int BeatUnit { get; }
        public int WindowTicks { get; }
        public bool AllowSus { get; }
        public bool AllowSevenths { get; }
        public double ChordConfidenceThreshold { get; }
        public bool AvoidCrossing { get; }
        public int DensityMinDuration { get; }
        public int MelismaNoteDuration { get; }
        public int MelismaRunLen { get; }
        public bool BlankLyrics { get; }
        public string LyricReplacement { get; }
        public int HumanizeTiming { get; }
        public int HumanizeVelocity { get; }
        public int Seed { get; }
        public bool RangesRelative { get; }
        public List<(int Min, int Max)> VoiceRanges { get; }
        public double StrongBeatWeight { get; }
        public double WeakBeatWeight { get; }

        public HarmonyConfig(
            int numVoices,
            StyleProfile style,
            int keyIndex,
            string mode,
            bool useManualKey,
            List<int> scalePcs,
            int ticksPerBeat,
            int beatsPerBar,
            int beatUnit,
            int windowTicks,
            bool allowSus,
            bool allowSevenths,
            double chordConfidenceThreshold,
            bool avoidCrossing,
            int densityMinDuration,
            int melismaNoteDuration,
            int melismaRunLen,
            bool blankLyrics,
            string lyricReplacement,
            int humanizeTiming,
            int humanizeVelocity,
            int seed,
            bool rangesRelative,
            List<(int Min, int Max)> voiceRanges,
            double strongBeatWeight,
            double weakBeatWeight) {
            NumVoices = numVoices;
            Style = style;
            KeyIndex = keyIndex;
            Mode = mode;
            UseManualKey = useManualKey;
            ScalePcs = scalePcs;
            TicksPerBeat = ticksPerBeat;
            BeatsPerBar = beatsPerBar;
            BeatUnit = beatUnit;
            WindowTicks = windowTicks;
            AllowSus = allowSus;
            AllowSevenths = allowSevenths;
            ChordConfidenceThreshold = chordConfidenceThreshold;
            AvoidCrossing = avoidCrossing;
            DensityMinDuration = densityMinDuration;
            MelismaNoteDuration = melismaNoteDuration;
            MelismaRunLen = melismaRunLen;
            BlankLyrics = blankLyrics;
            LyricReplacement = lyricReplacement ?? string.Empty;
            HumanizeTiming = humanizeTiming;
            HumanizeVelocity = humanizeVelocity;
            Seed = seed;
            RangesRelative = rangesRelative;
            VoiceRanges = voiceRanges;
            StrongBeatWeight = strongBeatWeight;
            WeakBeatWeight = weakBeatWeight;
        }
    }

    public class HarmGenEdit : BatchEdit {
        public string Name => "BatchEdit.Action.HarmonyGenerator";

        private readonly HarmGenConfig config;

        public HarmGenEdit(HarmGenConfig config) {
            this.config = config;
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            if (config.TrackIndexes == null || config.TrackIndexes.Count == 0) {
                return;
            }

            var style = StyleProfiles.GetProfile(config.StyleName) ?? StyleProfiles.GetProfile("Pop")!;
            var selectedSet = selectedNotes.Count > 0 ? new HashSet<UNote>(selectedNotes) : null;
            int selectedTrackNo = part.trackNo;

            docManager.StartUndoGroup(Name, true);
            try {
                (int keyIndex, string mode)? detectedKey = null;
                if (!config.UseManualKey) {
                    var analysisNotes = project.parts.OfType<UVoicePart>()
                        .Where(p => config.TrackIndexes.Contains(p.trackNo))
                        .SelectMany(p => p.notes)
                        .Select(note => (note.tone, note.duration));
                    var estimate = HarmonyAnalysis.DetectKeys(analysisNotes, 1).FirstOrDefault();
                    if (estimate != null) {
                        detectedKey = (estimate.KeyIndex, estimate.Mode);
                    }
                }

                foreach (var trackIndex in config.TrackIndexes.Distinct()) {
                    if (trackIndex < 0 || trackIndex >= project.tracks.Count) {
                        continue;
                    }
                    GenerateForTrack(project, trackIndex, style, docManager, selectedSet, selectedTrackNo, detectedKey);
                }
            } finally {
                docManager.EndUndoGroup();
            }
        }

        private void GenerateForTrack(UProject project, int trackIndex, StyleProfile style, DocManager docManager,
            HashSet<UNote>? selectedSet, int selectedTrackNo, (int keyIndex, string mode)? detectedKey) {
            var allNotes = GetNotesForTrack(project, trackIndex);
            if (allNotes.Count == 0) {
                return;
            }
            var notes = allNotes;
            if (selectedSet != null && trackIndex == selectedTrackNo) {
                bool fullTrackSelected = allNotes.Count > 0 && allNotes.All(n => selectedSet.Contains(n.Note));
                if (!fullTrackSelected) {
                    notes = allNotes.Where(n => selectedSet.Contains(n.Note)).ToList();
                    if (notes.Count == 0) {
                        return;
                    }
                }
            }

            int ticksPerBeat = project.resolution;
            var (beatsPerBar, beatUnit) = GetTimeSignature(project);
            int beatTicks = (int)(ticksPerBeat * (4.0 / beatUnit));
            int windowTicks = config.WindowSizeIndex switch {
                0 => beatTicks * beatsPerBar,
                1 => beatTicks * 2,
                _ => beatTicks
            };

            int keyIndex;
            string mode;
            if (config.UseManualKey) {
                keyIndex = Math.Clamp(config.ManualKeyIndex, 0, 11);
                mode = config.ManualMode == "minor" ? "minor" : "major";
            } else if (detectedKey.HasValue) {
                keyIndex = detectedKey.Value.keyIndex;
                mode = detectedKey.Value.mode;
            } else {
                (keyIndex, mode) = DetectKey(allNotes);
            }

            var scalePcs = GetScalePcs(keyIndex, mode);
            var chords = EstimateChords(
                allNotes,
                keyIndex,
                mode,
                windowTicks,
                config.AllowSus,
                config.AllowSevenths,
                config.ChordConfidenceThreshold,
                strictDiatonic: config.UseManualKey,
                ticksPerBeat,
                beatsPerBar,
                beatUnit,
                config.StrongBeatWeight,
                config.WeakBeatWeight);

            int voiceCount = Math.Clamp(config.NumVoices, 1, 8);
            var genConfig = new HarmonyConfig(
                numVoices: voiceCount,
                style: style,
                keyIndex: keyIndex,
                mode: mode,
                useManualKey: config.UseManualKey,
                scalePcs: scalePcs,
                ticksPerBeat: ticksPerBeat,
                beatsPerBar: beatsPerBar,
                beatUnit: beatUnit,
                windowTicks: windowTicks,
                allowSus: config.AllowSus,
                allowSevenths: config.AllowSevenths,
                chordConfidenceThreshold: config.ChordConfidenceThreshold,
                avoidCrossing: config.AvoidCrossing,
                densityMinDuration: config.DensityMinDuration,
                melismaNoteDuration: config.MelismaNoteDuration,
                melismaRunLen: config.MelismaRunLen,
                blankLyrics: config.BlankLyrics,
                lyricReplacement: config.LyricReplacement,
                humanizeTiming: config.HumanizeTiming,
                humanizeVelocity: config.HumanizeVelocity,
                seed: config.Seed,
                rangesRelative: config.RangesRelative,
                voiceRanges: config.UseDefaultRanges
                    ? DefaultVoiceRanges(style.Name, voiceCount, config.SustainedChoir)
                    : NormalizeRanges(config.VoiceRanges, voiceCount, config.SustainedChoir),
                strongBeatWeight: config.StrongBeatWeight,
                weakBeatWeight: config.WeakBeatWeight
            );

            if (config.SustainedChoir) {
                var sustainedNotes = GenerateSustainedHarmonies(notes, chords, genConfig, config.LyricReplacement);
                AppendSustainedTracks(project, trackIndex, sustainedNotes, genConfig, config, docManager);
            } else {
                var harmonyNotes = GenerateHarmonies(notes, chords, genConfig);
                AppendTracks(project, trackIndex, harmonyNotes, notes, genConfig, config, docManager);
            }
        }

        private static List<(int Min, int Max)> NormalizeRanges(
            List<(int Min, int Max)> ranges,
            int numVoices,
            bool sustainedChoir) {
            var result = new List<(int Min, int Max)>();
            var fallback = DefaultVoiceRanges("Choir", numVoices, sustainedChoir);
            for (int i = 0; i < numVoices; i++) {
                if (ranges != null && i < ranges.Count) {
                    result.Add(ranges[i]);
                } else {
                    result.Add(i < fallback.Count ? fallback[i] : (-8, 8));
                }
            }
            return result;
        }

        private static (int beatsPerBar, int beatUnit) GetTimeSignature(UProject project) {
            if (project.timeSignatures != null && project.timeSignatures.Count > 0) {
                var first = project.timeSignatures[0];
                return (first.beatPerBar, first.beatUnit);
            }
            return (4, 4);
        }

        private static List<NoteEvent> GetNotesForTrack(UProject project, int trackNo) {
            var notes = new List<NoteEvent>();
            var voiceParts = project.parts.OfType<UVoicePart>()
                .Where(p => p.trackNo == trackNo)
                .OrderBy(p => p.position)
                .ToList();
            for (int vpIndex = 0; vpIndex < voiceParts.Count; vpIndex++) {
                var voicePart = voiceParts[vpIndex];
                int vpPosition = voicePart.position;
                int noteIndex = 0;
                foreach (var note in voicePart.notes) {
                    notes.Add(new NoteEvent(
                        tone: note.tone,
                        position: note.position,
                        duration: note.duration,
                        absPosition: vpPosition + note.position,
                        lyric: note.lyric,
                        voicePart: voicePart,
                        note: note,
                        noteIndex: noteIndex,
                        voicePartIndex: vpIndex));
                    noteIndex++;
                }
            }
            notes.Sort((a, b) => {
                var cmp = a.AbsPosition.CompareTo(b.AbsPosition);
                if (cmp != 0) return cmp;
                cmp = a.Duration.CompareTo(b.Duration);
                if (cmp != 0) return cmp;
                return a.NoteIndex.CompareTo(b.NoteIndex);
            });
            return notes;
        }

        private static (int keyIndex, string mode) DetectKey(List<NoteEvent> notes) {
            var result = HarmonyAnalysis.DetectKeys(notes.Select(note => (note.Tone, note.Duration)), 1)
                .FirstOrDefault();
            return result == null ? (0, "major") : (result.KeyIndex, result.Mode);
        }

        private static List<int> GetScalePcs(int keyIndex, string mode) {
            int[] majorIntervals = { 0, 2, 4, 5, 7, 9, 11 };
            int[] minorIntervals = { 0, 2, 3, 5, 7, 8, 10 };
            var intervals = mode == "minor" ? minorIntervals : majorIntervals;
            return intervals.Select(interval => (keyIndex + interval) % 12).ToList();
        }

        private static List<int> GetTonicChordPcs(int keyIndex, string mode) {
            var intervals = mode == "minor" ? new[] { 0, 3, 7 } : new[] { 0, 4, 7 };
            return intervals.Select(interval => HarmonyAnalysis.PositiveMod(keyIndex + interval, 12)).ToList();
        }

        internal static List<ChordWindow> EstimateChords(
            List<NoteEvent> notes,
            int keyIndex,
            string mode,
            int windowTicks,
            bool allowSus,
            bool allowSevenths,
            double confidenceThreshold,
            bool strictDiatonic,
            int ticksPerBeat,
            int beatsPerBar,
            int beatUnit,
            double strongBeatWeight,
            double weakBeatWeight) {
            var windows = new List<ChordWindow>();
            if (notes.Count == 0 || windowTicks <= 0) {
                return windows;
            }
            int minStart = notes.Min(n => n.AbsPosition);
            int maxEnd = notes.Max(n => n.AbsPosition + n.Duration);
            var scalePcs = GetScalePcs(keyIndex, mode);

            var triads = new Dictionary<string, int[]> {
                ["maj"] = new[] { 0, 4, 7 },
                ["min"] = new[] { 0, 3, 7 },
                ["dim"] = new[] { 0, 3, 6 },
            };
            if (allowSus) {
                triads["sus2"] = new[] { 0, 2, 7 };
                triads["sus4"] = new[] { 0, 5, 7 };
            }
            var sevenths = new Dictionary<string, int[]> {
                ["maj7"] = new[] { 0, 4, 7, 11 },
                ["7"] = new[] { 0, 4, 7, 10 },
                ["min7"] = new[] { 0, 3, 7, 10 },
                ["m7b5"] = new[] { 0, 3, 6, 10 },
            };

            var candidates = new List<ChordCandidate>();
            for (int root = 0; root < 12; root++) {
                foreach (var kv in triads) {
                    AddChordCandidate(candidates, root, kv.Key, kv.Value, scalePcs, keyIndex, mode, strictDiatonic);
                }
                if (allowSevenths) {
                    foreach (var kv in sevenths) {
                        AddChordCandidate(candidates, root, kv.Key, kv.Value, scalePcs, keyIndex, mode, strictDiatonic);
                    }
                }
            }
            if (candidates.Count == 0) {
                candidates.Add(new ChordCandidate(new ChordInfo(keyIndex, "scale", scalePcs), true, 0));
            }

            int firstWindow = (int)Math.Floor(minStart / (double)windowTicks) * windowTicks;
            var observations = new List<double[]>();
            for (int start = firstWindow; start < maxEnd; start += windowTicks) {
                int end = start + windowTicks;
                double[] hist = new double[12];
                double total = 0.0;
                foreach (var note in notes) {
                    int noteStart = note.AbsPosition;
                    int noteEnd = note.AbsPosition + note.Duration;
                    int overlap = Math.Min(noteEnd, end) - Math.Max(noteStart, start);
                    if (overlap <= 0) {
                        continue;
                    }
                    double beatFactor = StrongBeatFactor(note.AbsPosition, ticksPerBeat, beatsPerBar, beatUnit);
                    double beatWeight = Math.Max(0.05, weakBeatWeight + beatFactor * (strongBeatWeight - weakBeatWeight));
                    double weightedOverlap = overlap * beatWeight;
                    hist[HarmonyAnalysis.PositiveMod(note.Tone, 12)] += weightedOverlap;
                    total += weightedOverlap;
                }
                observations.Add(BuildChordObservation(candidates, hist, total, keyIndex, mode));
            }

            if (observations.Count == 0) {
                return windows;
            }

            var dp = new double[observations.Count, candidates.Count];
            var back = new int[observations.Count, candidates.Count];
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++) {
                dp[0, candidateIndex] = observations[0][candidateIndex] + InitialChordPrior(candidates[candidateIndex], keyIndex);
                back[0, candidateIndex] = -1;
            }
            for (int windowIndex = 1; windowIndex < observations.Count; windowIndex++) {
                for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++) {
                    double best = double.NegativeInfinity;
                    int bestPrevious = 0;
                    for (int previousIndex = 0; previousIndex < candidates.Count; previousIndex++) {
                        double score = dp[windowIndex - 1, previousIndex]
                            + TransitionScore(candidates[previousIndex], candidates[candidateIndex], keyIndex);
                        if (score > best) {
                            best = score;
                            bestPrevious = previousIndex;
                        }
                    }
                    dp[windowIndex, candidateIndex] = observations[windowIndex][candidateIndex] + best;
                    back[windowIndex, candidateIndex] = bestPrevious;
                }
            }

            int selectedIndex = 0;
            double finalScore = double.NegativeInfinity;
            for (int candidateIndex = 0; candidateIndex < candidates.Count; candidateIndex++) {
                if (dp[observations.Count - 1, candidateIndex] > finalScore) {
                    finalScore = dp[observations.Count - 1, candidateIndex];
                    selectedIndex = candidateIndex;
                }
            }
            var selected = new int[observations.Count];
            for (int windowIndex = observations.Count - 1; windowIndex >= 0; windowIndex--) {
                selected[windowIndex] = selectedIndex;
                selectedIndex = back[windowIndex, selectedIndex];
                if (selectedIndex < 0) {
                    break;
                }
            }

            for (int windowIndex = 0; windowIndex < observations.Count; windowIndex++) {
                int start = firstWindow + windowIndex * windowTicks;
                int end = start + windowTicks;
                int candidateIndex = selected[windowIndex];
                double selectedObservation = observations[windowIndex][candidateIndex];
                double nextBest = observations[windowIndex]
                    .Where((_, index) => index != candidateIndex)
                    .DefaultIfEmpty(double.NegativeInfinity)
                    .Max();
                double margin = selectedObservation - nextBest;
                double confidence = double.IsNegativeInfinity(margin)
                    ? 0
                    : 1.0 - Math.Exp(-Math.Max(0, margin));
                windows.Add(new ChordWindow(
                    start,
                    end,
                    candidates[candidateIndex].Chord,
                    confidence,
                    confidence < confidenceThreshold));
            }
            return windows;
        }

        private static void AddChordCandidate(
            List<ChordCandidate> candidates,
            int root,
            string quality,
            int[] intervals,
            List<int> scalePcs,
            int keyIndex,
            string mode,
            bool strictDiatonic) {
            var tones = intervals.Select(interval => HarmonyAnalysis.PositiveMod(root + interval, 12)).ToList();
            bool diatonic = tones.All(scalePcs.Contains) && scalePcs.Contains(root);
            bool minorDominant = mode == "minor"
                && root == HarmonyAnalysis.PositiveMod(keyIndex + 7, 12)
                && (quality == "maj" || quality == "7");
            if (minorDominant) {
                diatonic = true;
            }
            if (strictDiatonic && !diatonic) {
                return;
            }
            double prior = diatonic ? 0.40 : -0.90;
            if (root == keyIndex) prior += 0.15;
            if (root == HarmonyAnalysis.PositiveMod(keyIndex + 7, 12)) prior += 0.08;
            if (root == HarmonyAnalysis.PositiveMod(keyIndex + 5, 12)) prior += 0.06;
            if (quality.StartsWith("sus", StringComparison.Ordinal)) prior -= 0.22;
            if (quality == "dim" || quality == "m7b5") prior -= 0.18;
            if (quality.Contains('7')) prior -= 0.04;
            candidates.Add(new ChordCandidate(new ChordInfo(root, quality, tones), diatonic, prior));
        }

        private static double[] BuildChordObservation(
            List<ChordCandidate> candidates,
            double[] hist,
            double total,
            int keyIndex,
            string mode) {
            var scores = new double[candidates.Count];
            if (total <= 0) {
                for (int i = 0; i < candidates.Count; i++) {
                    scores[i] = candidates[i].Prior;
                }
                return scores;
            }
            for (int i = 0; i < candidates.Count; i++) {
                var candidate = candidates[i];
                double chordWeight = candidate.Chord.Tones.Sum(pc => hist[pc]);
                double nonChordWeight = Math.Max(0, total - chordWeight);
                double coverage = chordWeight / total;
                double score = coverage * 4.0 - (nonChordWeight / total) * 2.5 + candidate.Prior;
                int root = candidate.Chord.Root;
                int third = candidate.Chord.Tones.Count > 1 ? candidate.Chord.Tones[1] : root;
                int fifth = candidate.Chord.Tones.Count > 2 ? candidate.Chord.Tones[2] : root;
                score += hist[root] / total * 0.12;
                score += hist[third] / total * 0.08;
                score += hist[fifth] / total * 0.05;
                scores[i] = score;
            }
            return scores;
        }

        private static double InitialChordPrior(ChordCandidate candidate, int keyIndex) {
            if (candidate.Chord.Root == keyIndex && candidate.Chord.Quality == "maj") return 0.30;
            if (candidate.Chord.Root == keyIndex && candidate.Chord.Quality == "min") return 0.30;
            if (candidate.Chord.Root == HarmonyAnalysis.PositiveMod(keyIndex + 5, 12)) return 0.12;
            if (candidate.Chord.Root == HarmonyAnalysis.PositiveMod(keyIndex + 7, 12)) return 0.10;
            return 0;
        }

        private static double TransitionScore(ChordCandidate previous, ChordCandidate current, int keyIndex) {
            if (previous.Chord.Root == current.Chord.Root && previous.Chord.Quality == current.Chord.Quality) {
                return 0.42;
            }
            int commonTones = previous.Chord.Tones.Intersect(current.Chord.Tones).Count();
            int rootDistance = HarmonyAnalysis.PositiveMod(current.Chord.Root - previous.Chord.Root, 12);
            double score = commonTones * 0.08 - 0.12;
            if (rootDistance == 5 || rootDistance == 7) score += 0.22;
            if (previous.Chord.Root == HarmonyAnalysis.PositiveMod(keyIndex + 7, 12)
                && current.Chord.Root == keyIndex) {
                score += 0.35;
            }
            if (previous.Chord.Root == HarmonyAnalysis.PositiveMod(keyIndex + 2, 12)
                && current.Chord.Root == HarmonyAnalysis.PositiveMod(keyIndex + 7, 12)) {
                score += 0.18;
            }
            return score;
        }

        private static List<List<UNote?>> GenerateHarmonies(
            List<NoteEvent> notes,
            List<ChordWindow> chords,
            HarmonyConfig config) {
            var pitchRng = new Random(config.Seed);
            var timingRng = new Random(unchecked(config.Seed ^ 0x4D3A2F11));
            var velocityRng = new Random(unchecked(config.Seed ^ 0x17C9A5E3));
            var harmonyNotes = new List<List<UNote?>>();
            for (int i = 0; i < config.NumVoices; i++) {
                harmonyNotes.Add(Enumerable.Repeat<UNote?>(null, notes.Count).ToList());
            }

            var prevPitches = Enumerable.Repeat<int?>(null, config.NumVoices).ToList();
            var voiceOrder = ChooseVoiceOrder(config.VoiceRanges);
            var orderMap = voiceOrder.Select((voiceIdx, order) => (voiceIdx, order))
                .ToDictionary(x => x.voiceIdx, x => x.order);
            NoteEvent? previousNote = null;
            List<int>? stableChordTones = null;

            for (int idx = 0; idx < notes.Count; idx++) {
                var note = notes[idx];
                if (ShouldSkipNote(note, notes, idx, config)) {
                    continue;
                }
                if (previousNote != null
                    && (previousNote.VoicePart != note.VoicePart
                        || note.AbsPosition - (previousNote.AbsPosition + previousNote.Duration) > config.TicksPerBeat * 2)) {
                    for (int voiceIdx = 0; voiceIdx < config.NumVoices; voiceIdx++) {
                        prevPitches[voiceIdx] = null;
                    }
                }
                var chordWindow = GetChordForPosition(chords, note.AbsPosition);
                bool uncertainChord = chordWindow?.LowConfidence == true;
                var chordTones = chordWindow == null
                    ? config.ScalePcs
                    : uncertainChord
                        ? (stableChordTones ?? GetTonicChordPcs(config.KeyIndex, config.Mode))
                        : chordWindow.Chord.Tones;
                if (!uncertainChord && chordWindow != null) {
                    stableChordTones = chordWindow.Chord.Tones;
                }
                var candidatePcs = chordTones.Concat(config.ScalePcs).Distinct().ToList();
                double chordStrength = uncertainChord ? 0.35 : 1.0;

                double beatFactor = StrongBeatFactor(note.AbsPosition, config.TicksPerBeat, config.BeatsPerBar, config.BeatUnit);
                double dissonanceScale = config.Style.DissonanceWeight * (beatFactor >= 0.9 ? config.StrongBeatWeight : config.WeakBeatWeight);

                var chosenPitches = new Dictionary<int, int>();
                foreach (var voiceIdx in voiceOrder) {
                    var range = config.VoiceRanges[voiceIdx];
                    int minPitch = config.RangesRelative ? note.Tone + range.Min : range.Min;
                    int maxPitch = config.RangesRelative ? note.Tone + range.Max : range.Max;
                    var candidates = BuildCandidates(candidatePcs, minPitch, maxPitch);
                    bool crossingEnforced = false;
                    bool sideEnforced = false;
                    if (config.AvoidCrossing && chosenPitches.Count > 0) {
                        int lowerMax = chosenPitches.Values.Max();
                        var nonCrossing = candidates.Where(candidate => candidate > lowerMax).ToList();
                        if (nonCrossing.Count > 0) {
                            candidates = nonCrossing;
                            crossingEnforced = true;
                        }
                    }
                    if (config.AvoidCrossing && config.RangesRelative) {
                        var side = ExpectedSide(range.Min, range.Max);
                        var sideConstrained = side switch {
                            "below" => candidates.Where(candidate => candidate < note.Tone).ToList(),
                            "above" => candidates.Where(candidate => candidate > note.Tone).ToList(),
                            _ => candidates,
                        };
                        if (sideConstrained.Count > 0) {
                            candidates = sideConstrained;
                            sideEnforced = true;
                        }
                    }

                    double bestCost = double.PositiveInfinity;
                    int bestPitch = note.Tone;
                    foreach (var cand in candidates) {
                        double cost = 0.0;
                        if (chordTones.Contains(HarmonyAnalysis.PositiveMod(cand, 12))) {
                            cost += config.Style.ChordBonus * chordStrength;
                        } else {
                            cost += config.Style.ScalePenalty;
                        }
                        int interval = cand - note.Tone;
                        cost += IntervalCost(interval, config.Style);

                        if (prevPitches[voiceIdx] != null) {
                            int move = Math.Abs(cand - prevPitches[voiceIdx]!.Value);
                            cost += move * config.Style.MovementWeight;
                            if (move > 12) {
                                cost += config.Style.WideIntervalPenalty * (move - 12);
                            }
                        }
                        int intervalClass = Math.Abs(interval) % 12;
                        if (intervalClass == 1 || intervalClass == 2 || intervalClass == 6 || intervalClass == 11) {
                            cost += dissonanceScale;
                        }
                        if (config.AvoidCrossing) {
                            if (!crossingEnforced) {
                                foreach (var other in chosenPitches) {
                                    if (orderMap[voiceIdx] > orderMap[other.Key] && cand <= other.Value) {
                                        cost += 4.0;
                                    }
                                    if (orderMap[voiceIdx] < orderMap[other.Key] && cand >= other.Value) {
                                        cost += 4.0;
                                    }
                                }
                            }
                            if (config.RangesRelative && !sideEnforced) {
                                var side = ExpectedSide(range.Min, range.Max);
                                if (side == "below" && cand >= note.Tone) {
                                    cost += 4.5;
                                }
                                if (side == "above" && cand <= note.Tone) {
                                    cost += 4.5;
                                }
                            }
                        }
                        if (chosenPitches.Values.Contains(cand)) {
                            cost += 1.25;
                        } else if (chosenPitches.Values.Any(other => HarmonyAnalysis.PositiveMod(other, 12) == HarmonyAnalysis.PositiveMod(cand, 12))) {
                            cost += 0.18;
                        }
                        if (config.Style.Jitter > 0.0) {
                            cost += (pitchRng.NextDouble() * 2.0 - 1.0) * config.Style.Jitter;
                        }
                        if (cost < bestCost) {
                            bestCost = cost;
                            bestPitch = cand;
                        }
                    }
                    chosenPitches[voiceIdx] = bestPitch;
                }

                foreach (var kv in chosenPitches) {
                    var voiceIdx = kv.Key;
                    int pitch = kv.Value;
                    var newNote = notes[idx].Note.Clone() as UNote;
                    if (newNote == null) {
                        continue;
                    }
                    newNote.tone = pitch;
                    if (config.BlankLyrics) {
                        newNote.lyric = string.Empty;
                    } else if (!string.IsNullOrWhiteSpace(config.LyricReplacement)) {
                        newNote.lyric = config.LyricReplacement;
                    }
                    if (config.HumanizeTiming > 0) {
                        int offset = timingRng.Next(-config.HumanizeTiming, config.HumanizeTiming + 1);
                        int maxPosition = Math.Max(0, note.VoicePart.duration - newNote.duration);
                        newNote.position = Math.Clamp(newNote.position + offset, 0, maxPosition);
                    }
                    if (config.HumanizeVelocity > 0) {
                        ApplyVelocityHumanize(newNote, config.HumanizeVelocity, velocityRng);
                    }
                    harmonyNotes[voiceIdx][idx] = newNote;
                }

                for (int voiceIdx = 0; voiceIdx < config.NumVoices; voiceIdx++) {
                    var h = harmonyNotes[voiceIdx][idx];
                    if (h != null) {
                        prevPitches[voiceIdx] = h.tone;
                    }
                }
                previousNote = note;
            }

            return harmonyNotes;
        }

        internal static List<List<UNote>> GenerateSustainedHarmonies(
            List<NoteEvent> targetNotes,
            List<ChordWindow> chords,
            HarmonyConfig config,
            string lyric) {
            var result = Enumerable.Range(0, config.NumVoices)
                .Select(_ => new List<UNote>())
                .ToList();
            if (targetNotes.Count == 0 || chords.Count == 0) {
                return result;
            }

            int targetStart = targetNotes.Min(note => note.AbsPosition);
            int targetEnd = targetNotes.Max(note => note.AbsPosition + note.Duration);
            int fallbackAnchor = targetNotes.Select(note => note.Tone).OrderBy(tone => tone)
                .ElementAt(targetNotes.Count / 2);
            string padLyric = config.BlankLyrics
                ? string.Empty
                : (string.IsNullOrWhiteSpace(lyric) ? "a" : lyric.Trim());
            var previousPitches = Enumerable.Repeat<int?>(null, config.NumVoices).ToArray();
            var voiceOrder = ChooseVoiceOrder(config.VoiceRanges);
            List<int>? stableChordTones = null;

            foreach (var chordWindow in chords.Where(chord => chord.End > targetStart && chord.Start < targetEnd)) {
                int start = Math.Max(targetStart, chordWindow.Start);
                int end = Math.Min(targetEnd, chordWindow.End);
                if (end - start < 10) {
                    continue;
                }
                // Keep the pad's register stable even when the lead melody moves between windows.
                int anchor = fallbackAnchor;
                var chordTones = chordWindow.LowConfidence
                    ? (stableChordTones ?? GetTonicChordPcs(config.KeyIndex, config.Mode))
                    : chordWindow.Chord.Tones;
                if (!chordWindow.LowConfidence) {
                    stableChordTones = chordWindow.Chord.Tones;
                }
                var chosen = new Dictionary<int, int>();
                foreach (int voiceIndex in voiceOrder) {
                    var range = config.VoiceRanges[voiceIndex];
                    int minPitch = config.RangesRelative ? anchor + range.Min : range.Min;
                    int maxPitch = config.RangesRelative ? anchor + range.Max : range.Max;
                    var candidates = BuildCandidates(chordTones, minPitch, maxPitch);
                    if (config.AvoidCrossing && chosen.Count > 0) {
                        int lowerPitch = chosen.Values.Max();
                        var orderedCandidates = candidates.Where(candidate => candidate > lowerPitch).ToList();
                        if (orderedCandidates.Count > 0) {
                            candidates = orderedCandidates;
                        }
                    }

                    double center = (minPitch + maxPitch) / 2.0;
                    double bestCost = double.PositiveInfinity;
                    int bestPitch = candidates[0];
                    foreach (int candidate in candidates) {
                        double cost = Math.Abs(candidate - center) * 0.08;
                        if (previousPitches[voiceIndex].HasValue) {
                            cost += Math.Abs(candidate - previousPitches[voiceIndex]!.Value)
                                * config.Style.MovementWeight;
                        }
                        cost += IntervalCost(candidate - anchor, config.Style) * 0.35;
                        if (chosen.Values.Contains(candidate)) {
                            cost += 8.0;
                        }
                        int pitchClass = HarmonyAnalysis.PositiveMod(candidate, 12);
                        int duplicatePitchClasses = chosen.Values.Count(
                            other => HarmonyAnalysis.PositiveMod(other, 12) == pitchClass);
                        cost += duplicatePitchClasses * 0.30;
                        if (cost < bestCost) {
                            bestCost = cost;
                            bestPitch = candidate;
                        }
                    }
                    chosen[voiceIndex] = bestPitch;
                }

                foreach (var item in chosen) {
                    int voiceIndex = item.Key;
                    int pitch = item.Value;
                    var previous = result[voiceIndex].LastOrDefault();
                    if (previous != null && previous.End == start && previous.tone == pitch) {
                        previous.duration += end - start;
                    } else {
                        var note = CreateDefaultHarmonyNote();
                        note.position = start;
                        note.duration = end - start;
                        note.tone = pitch;
                        note.lyric = padLyric;
                        result[voiceIndex].Add(note);
                    }
                    previousPitches[voiceIndex] = pitch;
                }
            }
            return result;
        }

        private static UNote CreateDefaultHarmonyNote() {
            var note = UNote.Create();
            int start = NotePresets.Default.DefaultPortamento.PortamentoStart;
            int length = NotePresets.Default.DefaultPortamento.PortamentoLength;
            var shape = NotePresets.Default.DefaultPitchShape;
            note.pitch.AddPoint(new PitchPoint(start, 0, shape));
            note.pitch.AddPoint(new PitchPoint(start + length, 0, shape));
            return note;
        }

        private static void ApplyVelocityHumanize(UNote note, int maxDelta, Random rng) {
            if (note.phonemeExpressions == null || note.phonemeExpressions.Count == 0) {
                return;
            }
            foreach (var exp in note.phonemeExpressions) {
                if (exp.descriptor?.abbr != Format.Ustx.VEL) {
                    continue;
                }
                int delta = rng.Next(-maxDelta, maxDelta + 1);
                exp.value = Math.Clamp(exp.value + delta, exp.descriptor.min, exp.descriptor.max);
            }
        }

        internal static List<int> BuildCandidates(List<int> candidatePcs, int minPitch, int maxPitch) {
            if (minPitch > maxPitch) {
                (minPitch, maxPitch) = (maxPitch, minPitch);
            }
            var candidates = new List<int>();
            for (int pitch = minPitch; pitch <= maxPitch; pitch++) {
                if (candidatePcs.Contains(HarmonyAnalysis.PositiveMod(pitch, 12))) {
                    candidates.Add(pitch);
                }
            }
            if (candidates.Count == 0) {
                for (int pitch = minPitch; pitch <= maxPitch; pitch++) {
                    candidates.Add(pitch);
                }
            }
            return candidates;
        }

        internal static double IntervalCost(int interval, StyleProfile style) {
            int absoluteInterval = Math.Abs(interval);
            if (style.PreferredIntervals.Contains(absoluteInterval)) {
                return 0.0;
            }
            int intervalClass = absoluteInterval % 12;
            if (absoluteInterval > 12 && style.PreferredIntervals.Contains(intervalClass)) {
                return 0.2;
            }
            if (intervalClass == 0) {
                return absoluteInterval == 0 ? 0.8 : 0.35;
            }
            if (intervalClass == 1 || intervalClass == 2 || intervalClass == 6 || intervalClass == 11) {
                return 2.5;
            }
            return 0.8;
        }

        private static List<int> ChooseVoiceOrder(List<(int Min, int Max)> voiceRanges) {
            var centers = new List<(double center, int idx)>();
            for (int i = 0; i < voiceRanges.Count; i++) {
                var r = voiceRanges[i];
                centers.Add(((r.Min + r.Max) / 2.0, i));
            }
            return centers.OrderBy(c => c.center).Select(c => c.idx).ToList();
        }

        private static string? ExpectedSide(int rangeMin, int rangeMax) {
            if (rangeMax < 0) {
                return "below";
            }
            if (rangeMin > 0) {
                return "above";
            }
            return null;
        }

        private static bool ShouldSkipNote(NoteEvent note, List<NoteEvent> notes, int index, HarmonyConfig config) {
            if (config.DensityMinDuration > 0 && note.Duration < config.DensityMinDuration) {
                return true;
            }
            if (config.MelismaNoteDuration <= 0 || config.MelismaRunLen <= 1) {
                return false;
            }
            if (note.Duration > config.MelismaNoteDuration) {
                return false;
            }
            int runLen = 1;
            var current = note;
            for (int i = index - 1; i >= 0; i--) {
                var previous = notes[i];
                int gap = current.AbsPosition - (previous.AbsPosition + previous.Duration);
                if (previous.VoicePart != note.VoicePart
                    || previous.Duration > config.MelismaNoteDuration
                    || gap > config.MelismaNoteDuration) {
                    break;
                }
                runLen += 1;
                current = previous;
            }
            current = note;
            for (int i = index + 1; i < notes.Count; i++) {
                var next = notes[i];
                int gap = next.AbsPosition - (current.AbsPosition + current.Duration);
                if (next.VoicePart != note.VoicePart
                    || next.Duration > config.MelismaNoteDuration
                    || gap > config.MelismaNoteDuration) {
                    break;
                }
                runLen += 1;
                current = next;
            }
            return runLen >= config.MelismaRunLen;
        }

        private static ChordWindow? GetChordForPosition(List<ChordWindow> chords, int position) {
            if (chords == null || chords.Count == 0) {
                return null;
            }
            foreach (var chord in chords) {
                if (chord.Start <= position && position < chord.End) {
                    return chord;
                }
            }
            return chords[^1];
        }

        private static double StrongBeatFactor(int position, int ticksPerBeat, int beatsPerBar, int beatUnit) {
            int beatTicks = (int)(ticksPerBeat * (4.0 / beatUnit));
            int barTicks = beatTicks * beatsPerBar;
            if (barTicks > 0 && HarmonyAnalysis.PositiveMod(position, barTicks) == 0) {
                return 1.0;
            }
            if (beatTicks > 0 && HarmonyAnalysis.PositiveMod(position, beatTicks) == 0) {
                return 0.7;
            }
            return 0.4;
        }

        private static List<(int Min, int Max)> DefaultVoiceRanges(
            string styleName,
            int numVoices,
            bool sustainedChoir) {
            if (sustainedChoir) {
                var choirPool = new List<(int Min, int Max)> {
                    (-24, -13), (-19, -10), (-14, -7), (-9, -3),
                    (3, 9), (7, 14), (10, 19), (15, 24),
                };
                if (numVoices == 1) {
                    return new List<(int, int)> { choirPool[3] };
                }
                return Enumerable.Range(0, numVoices)
                    .Select(index => choirPool[(int)Math.Round(index * (choirPool.Count - 1.0) / (numVoices - 1))])
                    .ToList();
            }
            List<(int Min, int Max)> presets = styleName switch {
                "Cinematic" => new List<(int, int)> { (-12, -5), (7, 14), (12, 19), (-19, -12) },
                "Choir" => new List<(int, int)> { (-8, -3), (3, 8), (10, 16), (-16, -10) },
                "Randomized Human" => new List<(int, int)> { (-8, -3), (3, 9), (10, 17), (-16, -10) },
                _ => new List<(int, int)> { (-8, -3), (3, 8), (10, 16), (-16, -10) },
            };
            var result = presets.Take(numVoices).ToList();
            var wider = new List<(int Min, int Max)> {
                (-21, -14), (17, 23), (-26, -22), (24, 28),
            };
            result.AddRange(wider.Take(Math.Max(0, numVoices - result.Count)));
            return result;
        }

        private static Phonemizer ClonePhonemizer(Phonemizer source) {
            if (source == null) {
                return PhonemizerFactory.Get(typeof(DefaultPhonemizer))?.Create();
            }
            var factory = PhonemizerFactory.Get(source.GetType());
            return factory?.Create() ?? source;
        }

        private void AppendSustainedTracks(
            UProject project,
            int baseTrackIndex,
            List<List<UNote>> harmonyNotes,
            HarmonyConfig config,
            HarmGenConfig uiConfig,
            DocManager docManager) {
            var baseTrack = project.tracks[baseTrackIndex];
            var labels = BuildHarmonyLabels(config.Style.Name, config.NumVoices, sustainedChoir: true);
            for (int voiceIndex = 0; voiceIndex < config.NumVoices; voiceIndex++) {
                var newTrack = new UTrack(project) {
                    TrackNo = project.tracks.Count,
                    TrackName = $"{baseTrack.TrackName} - {labels[voiceIndex]}",
                    TrackColor = baseTrack.TrackColor,
                    Singer = baseTrack.Singer,
                    RendererSettings = baseTrack.RendererSettings.Clone(),
                    Volume = uiConfig.HarmonyVolume,
                };
                if (!string.IsNullOrWhiteSpace(uiConfig.PhonemizerFullName)) {
                    var factory = PhonemizerFactory.Get(uiConfig.PhonemizerFullName);
                    newTrack.Phonemizer = factory?.Create() ?? ClonePhonemizer(baseTrack.Phonemizer);
                } else {
                    newTrack.Phonemizer = ClonePhonemizer(baseTrack.Phonemizer);
                }
                docManager.ExecuteCmd(new AddTrackCommand(project, newTrack));

                var sourceNotes = harmonyNotes[voiceIndex];
                if (sourceNotes.Count == 0) {
                    continue;
                }
                int partPosition = sourceNotes.Min(note => note.position);
                var newNotes = new SortedSet<UNote>();
                foreach (var sourceNote in sourceNotes) {
                    var newNote = sourceNote.Clone();
                    newNote.position -= partPosition;
                    newNotes.Add(newNote);
                }
                var newPart = new UVoicePart {
                    name = $"{newTrack.TrackName} Part",
                    trackNo = newTrack.TrackNo,
                    position = partPosition,
                    notes = newNotes,
                    curves = new List<UCurve>(),
                };
                newPart.Duration = Math.Max(10, newNotes.Max(note => note.position + note.duration));
                docManager.ExecuteCmd(new AddPartCommand(project, newPart));
            }
        }

        private void AppendTracks(
            UProject project,
            int baseTrackIndex,
            List<List<UNote?>> harmonyNotes,
            List<NoteEvent> notes,
            HarmonyConfig config,
            HarmGenConfig uiConfig,
            DocManager docManager) {
            var baseTrack = project.tracks[baseTrackIndex];
            var labels = BuildHarmonyLabels(config.Style.Name, config.NumVoices, sustainedChoir: false);
            var voiceParts = project.parts.OfType<UVoicePart>()
                .Where(p => p.trackNo == baseTrackIndex)
                .OrderBy(p => p.position)
                .ToList();

            for (int voiceIdx = 0; voiceIdx < config.NumVoices; voiceIdx++) {
                var newTrack = new UTrack(project) {
                    TrackNo = project.tracks.Count,
                    TrackName = $"{baseTrack.TrackName} - {labels[voiceIdx]}",
                    TrackColor = baseTrack.TrackColor,
                    Singer = baseTrack.Singer,
                    RendererSettings = baseTrack.RendererSettings.Clone(),
                    Volume = uiConfig.HarmonyVolume,
                };
                if (!string.IsNullOrWhiteSpace(uiConfig.PhonemizerFullName)) {
                    var factory = PhonemizerFactory.Get(uiConfig.PhonemizerFullName);
                    if (factory != null) {
                        newTrack.Phonemizer = factory.Create();
                    } else {
                        newTrack.Phonemizer = ClonePhonemizer(baseTrack.Phonemizer);
                    }
                } else {
                    newTrack.Phonemizer = ClonePhonemizer(baseTrack.Phonemizer);
                }
                docManager.ExecuteCmd(new AddTrackCommand(project, newTrack));

                foreach (var voicePart in voiceParts) {
                    var newNotes = new SortedSet<UNote>();
                    for (int i = 0; i < notes.Count; i++) {
                        if (notes[i].VoicePart != voicePart) {
                            continue;
                        }
                        var hNote = harmonyNotes[voiceIdx][i];
                        if (hNote != null) {
                            newNotes.Add(hNote);
                        }
                    }
                    if (newNotes.Count == 0) {
                        continue;
                    }

                    var newPart = new UVoicePart {
                        name = $"{newTrack.TrackName} Part",
                        trackNo = newTrack.TrackNo,
                        position = voicePart.position,
                        notes = newNotes,
                        curves = new List<UCurve>(),
                    };
                    newPart.Duration = ComputeVoicePartDuration(voicePart.duration, newNotes);
                    docManager.ExecuteCmd(new AddPartCommand(project, newPart));
                }
            }
        }

        private static int ComputeVoicePartDuration(int baseDuration, SortedSet<UNote> notes) {
            if (notes.Count == 0) {
                return baseDuration;
            }
            int maxEnd = notes.Max(n => n.position + n.duration);
            return Math.Max(baseDuration, maxEnd);
        }

        private static List<string> BuildHarmonyLabels(string styleName, int numVoices, bool sustainedChoir) {
            if (sustainedChoir) {
                var choirLabels = new List<string> {
                    "Bass 2", "Bass", "Tenor", "Alto", "Mezzo", "Soprano", "High Soprano", "Air",
                };
                return Enumerable.Range(0, numVoices)
                    .Select(index => index < choirLabels.Count ? choirLabels[index] : $"Voice {index + 1}")
                    .ToList();
            }
            List<string> labels = styleName switch {
                "Cinematic" => new List<string> { "Low Oct", "High Fifth", "High Oct", "Low Oct 2" },
                "Choir" => new List<string> { "Alto", "Tenor", "Soprano", "Bass" },
                _ => new List<string> { "Harmony 1", "Harmony 2", "Harmony 3", "Harmony 4" },
            };
            return Enumerable.Range(0, numVoices)
                .Select(index => index < labels.Count ? labels[index] : $"Harmony {index + 1}")
                .ToList();
        }
    }
}
