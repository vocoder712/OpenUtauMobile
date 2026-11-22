using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Editing {
    public class AutoPitchEdit : BatchEdit {
        public string Name => "Auto Pitch";
        public bool IsAsync => true;

        private const string DefaultModelName = "autopitch.onnx";
        private const string DefaultConfigName = "autopitch.onnx.config.json";
        private const string DefaultVocabName = "autopitch.onnx.vocab.json";

        private static readonly object AssetLock = new object();
        private static AutoPitchAssets cachedAssets;

        public static void ClearCache() {
            lock (AssetLock) {
                if (cachedAssets != null) {
                    try {
                        cachedAssets.Session?.Dispose();
                    } catch (Exception ex) {
                        Log.Warning(ex, "Failed to dispose AutoPitch session.");
                    }
                    cachedAssets = null;
                }
            }
        }

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            RunAsync(project, part, selectedNotes, docManager, (_, __) => { }, CancellationToken.None);
        }

        public void RunAsync(
            UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager,
            Action<int, int> setProgressCallback, CancellationToken cancellationToken) {
            var assets = LoadAssets();
            if (assets == null) {
                return;
            }
            RefreshConfigAndVocab(assets);
            var notesAll = part.notes.OrderBy(n => n.position).ToList();
            if (notesAll.Count == 0) {
                return;
            }
            var selected = selectedNotes.Count > 0 ? selectedNotes.OrderBy(n => n.position).ToList() : null;
            int? rangeStart = null;
            int? rangeEnd = null;
            if (selected != null && selected.Count > 0) {
                rangeStart = selected.Min(n => n.position);
                rangeEnd = selected.Max(n => n.position + n.duration);
            }
            var cfg = assets.Config ?? new AutoPitchConfig();
            string lyricConditioning = ResolveLyricConditioning(cfg);
            bool useLyric = lyricConditioning != "none";
            if (lyricConditioning == "vocab" && (assets.Vocab == null || assets.Vocab.Count == 0)) {
                useLyric = false;
                lyricConditioning = "none";
            }
            int channels = cfg.model?.in_channels ?? 10;

            int padLeft = cfg.infer.pad_ticks;
            int padRight = cfg.infer.pad_ticks;
            if (rangeStart.HasValue && rangeEnd.HasValue) {
                padLeft = Math.Max(padLeft, cfg.selection_pad_left);
                padRight = Math.Max(padRight, cfg.selection_pad_right);
            }

            // Extract BPM from project
            double projectBpm = 120.0;
            if (project.tempos != null && project.tempos.Count > 0) {
                projectBpm = project.tempos[0].bpm;
            }
            if (projectBpm <= 0) {
                projectBpm = 120.0;
            }

            setProgressCallback(0, 3);
            var grid = BuildTickGrid(notesAll, cfg.data.dt, padLeft, padRight, rangeStart, rangeEnd);
            int gridStart = grid.gridStart;
            int[] ticks = grid.ticks;
            if (ticks.Length == 0 || cancellationToken.IsCancellationRequested) {
                return;
            }

            int conditioningClassCount = GetConditioningClassCount(cfg, lyricConditioning);
            var features = BuildFeatures(
                notesAll,
                gridStart,
                ticks,
                cfg.data,
                channels,
                lyricConditioning,
                assets.Vocab,
                conditioningClassCount,
                (float)projectBpm);
            if (features.mask.All(m => m <= 0.5f)) {
                return;
            }
            if (cancellationToken.IsCancellationRequested) {
                return;
            }

            ApplyInferenceNoise(features.features, cfg);
            setProgressCallback(1, 3);

            float styleId = 0.0f;
            float residualStrength = 0.0f;
            if (cfg.model != null && cfg.model.style_conditioning) {
                bool manualStyle = string.Equals(cfg.style, "manual", StringComparison.OrdinalIgnoreCase);
                styleId = manualStyle ? 1.0f : 0.0f;
                residualStrength = manualStyle ? cfg.style_strength : 0.0f;
            }

            float[] predNorm = PredictFull(
                assets.Session,
                features.features,
                features.lyricIds,
                channels,
                ticks.Length,
                cfg.infer.chunk_size,
                cfg.infer.chunk_overlap,
                lyricConditioning,
                cfg.model != null && cfg.model.style_conditioning ? styleId : (float?)null);
            if (predNorm.Length == 0 || cancellationToken.IsCancellationRequested) {
                return;
            }
            setProgressCallback(2, 3);

            Log.Information("[AutoPitch] predNorm length={len} residualStrength={rs} pitd_clamp={clamp} smooth_window={sw} rdp_epsilon={rdp}",
                predNorm.Length, residualStrength, cfg.data.pitd_clamp, cfg.infer.smooth_window, cfg.infer.rdp_epsilon);
            Log.Information("[AutoPitch] predNorm base range=[{min:F5},{max:F5}]",
                predNorm.Take(ticks.Length).Min(), predNorm.Take(ticks.Length).Max());

            float[] predTotal = new float[ticks.Length];
            for (int t = 0; t < ticks.Length; t++) {
                predTotal[t] = predNorm[t] + predNorm[ticks.Length + t] * residualStrength;
            }
            predTotal = MovingAverage(predTotal, cfg.infer.smooth_window);
            for (int t = 0; t < predTotal.Length; t++) {
                predTotal[t] = Math.Clamp(predTotal[t], -1.0f, 1.0f);
            }
            float[] predCents = new float[predTotal.Length];
            for (int t = 0; t < predTotal.Length; t++) {
                float val = predTotal[t] * cfg.data.pitd_clamp;
                predCents[t] = Math.Clamp(val, -cfg.infer.clamp_output, cfg.infer.clamp_output);
            }
            Log.Information("[AutoPitch] predCents range=[{min:F1},{max:F1}] non_zero={nz}/{total}",
                predCents.Min(), predCents.Max(),
                predCents.Count(c => Math.Abs(c) > 1.0f), predCents.Length);

            var forcedTicks = new HashSet<int>();
            foreach (var note in notesAll) {
                forcedTicks.Add(note.position);
                forcedTicks.Add(note.position + note.duration);
            }
            if (rangeStart.HasValue && rangeEnd.HasValue) {
                int minTick = rangeStart.Value - padLeft;
                int maxTick = rangeEnd.Value + padRight;
                forcedTicks.RemoveWhere(t => t < minTick || t > maxTick);
            }

            var simplified = SimplifyCurve(ticks, predCents, features.mask, cfg.infer.rdp_epsilon, forcedTicks);
            if (simplified.xs.Count == 0) {
                return;
            }
            if (rangeStart.HasValue && rangeEnd.HasValue) {
                int anchorStart = rangeStart.Value - padLeft;
                int anchorEnd = rangeEnd.Value + padRight;
                int minTick = ticks[0];
                int maxTick = ticks[^1];
                anchorStart = Math.Clamp(anchorStart, minTick, maxTick);
                anchorEnd = Math.Clamp(anchorEnd, minTick, maxTick);
                AddBoundaryAnchors(simplified.xs, simplified.ys, anchorStart, anchorEnd);
            }

            int[] oldXs = null;
            int[] oldYs = null;
            var curve = part.curves.FirstOrDefault(c => c.abbr == Format.Ustx.PITD);
            if (curve != null) {
                oldXs = curve.xs.ToArray();
                oldYs = curve.ys.ToArray();
            }

            List<int> xsFinal;
            List<int> ysFinal;
            if (rangeStart.HasValue && rangeEnd.HasValue) {
                int mergeStart = rangeStart.Value - padLeft;
                int mergeEnd = rangeEnd.Value + padRight;
                var merged = MergeCurves(oldXs, oldYs, simplified.xs, simplified.ys, mergeStart, mergeEnd, cfg.infer.clamp_output);
                xsFinal = merged.xs;
                ysFinal = merged.ys;
            } else {
                var validated = ValidateCurve(simplified.xs, simplified.ys, cfg.infer.clamp_output);
                xsFinal = validated.xs;
                ysFinal = validated.ys;
            }
            if (xsFinal.Count == 0 || cancellationToken.IsCancellationRequested) {
                return;
            }

            DocManager.Inst.PostOnUIThread(() => {
                docManager.StartUndoGroup(null, true);
                docManager.ExecuteCmd(new MergedSetCurveCommand(
                    project, part, Format.Ustx.PITD,
                    oldXs, oldYs, xsFinal.ToArray(), ysFinal.ToArray()));
                docManager.EndUndoGroup();
            });
            setProgressCallback(3, 3);
        }

        private static AutoPitchAssets LoadAssets() {
            lock (AssetLock) {
                if (cachedAssets != null) {
                    return cachedAssets;
                }
                string basePath = Path.Combine(PathManager.Inst.DependencyPath, "autopitchcurve");
                string modelPath = Path.Combine(basePath, DefaultModelName);
                string configPath = Path.Combine(basePath, DefaultConfigName);
                string vocabPath = Path.Combine(basePath, DefaultVocabName);
                if (!File.Exists(modelPath)) {
                    var e = new MessageCustomizableException(
                        "AutoPitch model not found",
                        $"AutoPitch model not found at {modelPath}. Install model files via the Package Manager or place them manually.",
                        new FileNotFoundException(modelPath));
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                    return null;
                }

                InferenceSession session = null;
                try {
                    // Force CPU: DirectML produces NaN on every chunk due to
                    // structural incompatibility with the NoteAttentionLayer ops
                    // (scatter_add / CumSum in _note_idx_from_features).
                    // AutoPitch runs once per part, so CPU performance is fine.
                    session = Onnx.getInferenceSession(modelPath, OnnxRunnerChoice.CPU);
                } catch (Exception ex) {
                    Log.Error(ex, "Failed to load AutoPitch ONNX session.");
                    var e = new MessageCustomizableException(
                        "AutoPitch model failed to load",
                        "Failed to load AutoPitch ONNX model.",
                        ex);
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                    return null;
                }

                AutoPitchConfig config = new AutoPitchConfig();
                if (File.Exists(configPath)) {
                    Log.Information("[AutoPitch] loading config from sidecar file: {path}", configPath);
                    try {
                        var json = File.ReadAllText(configPath, Encoding.UTF8);
                        config = Json.Deserialize<AutoPitchConfig>(json) ?? new AutoPitchConfig();
                    } catch (Exception ex) {
                        Log.Warning(ex, "Failed to parse AutoPitch config. Using defaults.");
                    }
                } else {
                    Log.Information("[AutoPitch] no sidecar config, loading from ONNX metadata");
                    try {
                        var meta = session.ModelMetadata.CustomMetadataMap;
                        if (meta != null && meta.TryGetValue("autopitch_config_json", out var metaJson)) {
                            config = Json.Deserialize<AutoPitchConfig>(metaJson) ?? new AutoPitchConfig();
                        }
                    } catch (Exception ex) {
                        Log.Warning(ex, "Failed to load AutoPitch config from model metadata.");
                    }
                }

                Dictionary<string, int> vocab = new Dictionary<string, int>();
                if (File.Exists(vocabPath)) {
                    try {
                        var json = File.ReadAllText(vocabPath, Encoding.UTF8);
                        vocab = Json.Deserialize<Dictionary<string, int>>(json) ?? new Dictionary<string, int>();
                    } catch (Exception ex) {
                        Log.Warning(ex, "Failed to parse AutoPitch vocab.");
                    }
                } else {
                    try {
                        var meta = session.ModelMetadata.CustomMetadataMap;
                        if (meta != null && meta.TryGetValue("lyric_vocab_json", out var vocabJson)) {
                            vocab = Json.Deserialize<Dictionary<string, int>>(vocabJson) ?? new Dictionary<string, int>();
                        }
                    } catch (Exception ex) {
                        Log.Warning(ex, "Failed to load AutoPitch vocab from model metadata.");
                    }
                }

                Log.Information("[AutoPitch] loaded config: dt={dt} chunk_size={cs} chunk_overlap={co} rdp_eps={rdp} smooth={sm} pitd_clamp={pc} bpm_scale={bs} in_channels={ch}",
                    config.data.dt, config.infer.chunk_size, config.infer.chunk_overlap,
                    config.infer.rdp_epsilon, config.infer.smooth_window, config.data.pitd_clamp,
                    config.data.bpm_scale, config.model?.in_channels ?? -1);
                Log.Information("[AutoPitch] vocab size={vs} OnnxRunner={runner}",
                    vocab.Count, Preferences.Default.OnnxRunner ?? "(default)");

                cachedAssets = new AutoPitchAssets {
                    BasePath = basePath,
                    Session = session,
                    Config = config,
                    Vocab = vocab,
                    ConfigPath = configPath,
                    VocabPath = vocabPath,
                };
                return cachedAssets;
            }
        }

        private static void RefreshConfigAndVocab(AutoPitchAssets assets) {
            if (assets == null) {
                return;
            }
            if (!string.IsNullOrEmpty(assets.ConfigPath) && File.Exists(assets.ConfigPath)) {
                try {
                    var json = File.ReadAllText(assets.ConfigPath, Encoding.UTF8);
                    assets.Config = Json.Deserialize<AutoPitchConfig>(json) ?? assets.Config;
                } catch (Exception ex) {
                    Log.Warning(ex, "Failed to reload AutoPitch config.");
                }
            }
            if (!string.IsNullOrEmpty(assets.VocabPath) && File.Exists(assets.VocabPath)) {
                try {
                    var json = File.ReadAllText(assets.VocabPath, Encoding.UTF8);
                    assets.Vocab = Json.Deserialize<Dictionary<string, int>>(json) ?? assets.Vocab;
                } catch (Exception ex) {
                    Log.Warning(ex, "Failed to reload AutoPitch vocab.");
                }
            }
        }

        private static (int gridStart, int[] ticks) BuildTickGrid(
            List<UNote> notes, int dt, int padLeft, int padRight, int? rangeStart, int? rangeEnd) {
            if (notes.Count == 0) {
                return (0, Array.Empty<int>());
            }
            int minPos = rangeStart ?? notes.Min(n => n.position);
            int maxEnd = rangeEnd ?? notes.Max(n => n.position + n.duration);
            minPos = Math.Max(0, minPos - padLeft);
            maxEnd = Math.Max(minPos + dt, maxEnd + padRight);
            int gridStart = (minPos / dt) * dt;
            int gridEnd = (int)Math.Ceiling(maxEnd / (double)dt) * dt;
            int length = Math.Max(0, (gridEnd - gridStart) / dt);
            int[] ticks = new int[length];
            for (int i = 0; i < length; i++) {
                ticks[i] = gridStart + i * dt;
            }
            return (gridStart, ticks);
        }

        private static FeatureBundle BuildFeatures(
            List<UNote> notes,
            int gridStart,
            int[] ticks,
            DataConfig cfg,
            int channels,
            string lyricConditioning,
            Dictionary<string, int> vocab,
            int conditioningClassCount,
            float bpm = 120.0f) {
            int tLen = ticks.Length;
            float[] features = new float[channels * tLen];
            float[] mask = new float[tLen];
            bool useLyric = lyricConditioning != "none";
            bool useClass = lyricConditioning == "class";
            bool useClassEdge = lyricConditioning == "class_edge";
            bool useEdge = lyricConditioning == "edge";
            bool useEdgeClass = lyricConditioning == "edge_class";
            // Rest frames must carry the conditioning mode's rest sentinel, not 0.
            // For the class-like modes ID 0 is a real sound class ("vowel"), so a
            // zero-filled array told the model a vowel was being sung during every
            // silence. Rest frames are excluded from the loss during training, but
            // the TCN's receptive field spans them, so this contaminates the
            // context for the voiced frames on either side. For "vocab" mode 0 is
            // "<pad>", which is already correct.
            long[]? lyricIds = useLyric ? new long[tLen] : null;
            if (lyricIds != null) {
                long restId = ConditioningRestId(lyricConditioning);
                if (restId != 0) {
                    for (int t = 0; t < tLen; t++) {
                        lyricIds[t] = restId;
                    }
                }
            }
            float pitchScale = cfg.pitch_scale != 0.0f ? cfg.pitch_scale : 1.0f;
            float intervalScale = cfg.interval_scale != 0.0f ? cfg.interval_scale : 1.0f;
            float noteLenScale = cfg.note_len_scale != 0.0f ? cfg.note_len_scale : 1.0f;
            float noteLenNorm = cfg.note_len_norm != 0.0f ? cfg.note_len_norm : 1.0f;
            // BPM-derived constants (only used when channels >= 12)
            float bpmClamped = Math.Max(1.0f, bpm);
            float secondsPerTick = 60.0f / (bpmClamped * cfg.ticks_per_beat);
            float bpmScale = cfg.bpm_scale > 0 ? cfg.bpm_scale : 120.0f;
            float noteDurSecScale = cfg.note_dur_sec_scale > 0 ? cfg.note_dur_sec_scale : 1.0f;
            float timeScale = Math.Clamp(bpmClamped / bpmScale, 0.0f, 4.0f);
            if (notes.Count == 0 || tLen == 0) {
                if (tLen > 0 && channels > 1) {
                    for (int t = 0; t < tLen; t++) {
                        features[tLen + t] = 1.0f;
                    }
                }
                // Fill ch11 (time_scale) even for empty notes
                if (tLen > 0 && channels >= 12) {
                    for (int t = 0; t < tLen; t++) {
                        features[11 * tLen + t] = timeScale;
                    }
                }
                return new FeatureBundle(features, mask, lyricIds);
            }
            int[]? edgeIds = (useEdge || useClassEdge) ? BuildEdgeIdsForNotes(notes) : null;
            int[]? edgeClassIds = useEdgeClass ? BuildEdgeClassIdsForNotes(notes) : null;

            for (int i = 0; i < notes.Count; i++) {
                var note = notes[i];
                int pos = note.position;
                int dur = note.duration;
                if (dur <= 0) {
                    continue;
                }
                int tone = note.tone;
                int startIdx = (int)Math.Ceiling((pos - gridStart) / (double)cfg.dt);
                int endIdx = (int)Math.Ceiling((pos + dur - gridStart) / (double)cfg.dt);
                startIdx = Math.Clamp(startIdx, 0, tLen);
                endIdx = Math.Clamp(endIdx, 0, tLen);
                if (endIdx <= startIdx) {
                    continue;
                }

                var prev = i > 0 ? notes[i - 1] : null;
                var next = i + 1 < notes.Count ? notes[i + 1] : null;
                int intervalPrev = prev != null ? tone - prev.tone : 0;
                int intervalNext = next != null ? next.tone - tone : 0;
                intervalPrev = Math.Clamp(intervalPrev, -cfg.interval_clip, cfg.interval_clip);
                intervalNext = Math.Clamp(intervalNext, -cfg.interval_clip, cfg.interval_clip);
                float legatoPrev = prev != null && prev.position + prev.duration == pos ? 1.0f : 0.0f;
                float legatoNext = next != null && pos + dur == next.position ? 1.0f : 0.0f;

                if (pos >= ticks[0]) {
                    features[2 * tLen + startIdx] = 1.0f;
                }
                int noteEnd = pos + dur;
                if (noteEnd <= ticks[^1] + cfg.dt) {
                    features[3 * tLen + Math.Max(startIdx, endIdx - 1)] = 1.0f;
                }

                float noteLen = dur / noteLenNorm;
                noteLen = Math.Clamp(noteLen, 0.0f, cfg.note_len_clip) / noteLenScale;
                float basePitch = (tone - cfg.pitch_center) / pitchScale;
                float intervalPrevScaled = intervalPrev / intervalScale;
                float intervalNextScaled = intervalNext / intervalScale;

                int tokenId = 0;
                if (useLyric && lyricIds != null) {
                    string token = NormalizePhoneme(note.lyric);
                    if (useClass) {
                        tokenId = ClassifyPhoneme(token);
                    } else if (useClassEdge && edgeIds != null && i < edgeIds.Length) {
                        int classId = ClassifyPhoneme(token);
                        tokenId = classId * EDGE_CLASS_COUNT + edgeIds[i];
                    } else if (useEdge && edgeIds != null && i < edgeIds.Length) {
                        tokenId = edgeIds[i];
                    } else if (useEdgeClass && edgeClassIds != null && i < edgeClassIds.Length) {
                        tokenId = edgeClassIds[i];
                    } else if (vocab != null && vocab.Count > 0) {
                        if (vocab.TryGetValue(token, out int id)) {
                            tokenId = id;
                        }
                    }
                    if ((useClass || useClassEdge || useEdge || useEdgeClass) && conditioningClassCount > 0) {
                        tokenId = Math.Clamp(tokenId, 0, conditioningClassCount - 1);
                    }
                }

                // BPM features for this note (only when channels >= 12)
                float noteDurSec = 0.0f;
                float noteDurSecFeat = 0.0f;
                if (channels >= 12) {
                    noteDurSec = dur * secondsPerTick;
                    noteDurSec = Math.Min(noteDurSec, cfg.note_dur_sec_clip);
                    noteDurSecFeat = noteDurSec / noteDurSecScale;
                }

                for (int t = startIdx; t < endIdx; t++) {
                    mask[t] = 1.0f;
                    features[0 * tLen + t] = basePitch;
                    float posInNote = (ticks[t] - pos) / (float)dur;
                    features[4 * tLen + t] = Math.Clamp(posInNote, 0.0f, 1.0f);
                    features[5 * tLen + t] = noteLen;
                    features[6 * tLen + t] = intervalPrevScaled;
                    features[7 * tLen + t] = intervalNextScaled;
                    features[8 * tLen + t] = legatoPrev;
                    features[9 * tLen + t] = legatoNext;
                    if (channels >= 12) {
                        features[10 * tLen + t] = noteDurSecFeat;
                        features[11 * tLen + t] = timeScale;
                    }
                    if (useLyric && lyricIds != null) {
                        lyricIds[t] = tokenId;
                    }
                }

                int tailTicks = Math.Max(0, cfg.post_note_tail_ticks);
                int tailMaxGap = Math.Max(0, cfg.post_note_tail_max_gap_ticks);
                if (tailTicks > 0) {
                    int maxTailTicks = tailTicks;
                    if (next != null) {
                        int gapTicks = Math.Max(0, next.position - noteEnd);
                        maxTailTicks = Math.Min(maxTailTicks, gapTicks);
                    }
                    if (tailMaxGap > 0) {
                        maxTailTicks = Math.Min(maxTailTicks, tailMaxGap);
                    }
                    if (maxTailTicks > 0) {
                        int tailStartIdx = endIdx;
                        int tailEndIdx = (int)Math.Ceiling((noteEnd + maxTailTicks - gridStart) / (double)cfg.dt);
                        tailStartIdx = Math.Clamp(tailStartIdx, 0, tLen);
                        tailEndIdx = Math.Clamp(tailEndIdx, 0, tLen);
                        for (int t = tailStartIdx; t < tailEndIdx; t++) {
                            mask[t] = 1.0f;
                            features[0 * tLen + t] = basePitch;
                            float posInTail = (ticks[t] - noteEnd) / (float)Math.Max(1, maxTailTicks);
                            features[4 * tLen + t] = 1.0f + Math.Clamp(posInTail, 0.0f, 1.0f);
                            features[5 * tLen + t] = noteLen;
                            features[6 * tLen + t] = intervalPrevScaled;
                            features[7 * tLen + t] = intervalNextScaled;
                            features[8 * tLen + t] = legatoPrev;
                            features[9 * tLen + t] = legatoNext;
                            if (channels >= 12) {
                                features[10 * tLen + t] = noteDurSecFeat;
                                features[11 * tLen + t] = timeScale;
                            }
                            if (useLyric && lyricIds != null) {
                                lyricIds[t] = tokenId;
                            }
                        }
                    }
                }
            }

            if (channels > 1) {
                for (int t = 0; t < tLen; t++) {
                    features[tLen + t] = 1.0f - mask[t];
                }
            }
            return new FeatureBundle(features, mask, lyricIds);
        }

        private static void ApplyInferenceNoise(float[] features, AutoPitchConfig cfg) {
            float dropoutP = cfg.dropout_p ?? (cfg.model?.dropout ?? 0.0f);
            bool useDropout = cfg.stochastic_dropout && dropoutP > 0.0f;
            float noiseStd = cfg.feat_noise_std ?? 0.0f;
            float tinyNoiseStd = cfg.feat_noise_std_tiny ?? 0.0f;
            if (!useDropout && noiseStd <= 0.0f && tinyNoiseStd <= 0.0f) {
                return;
            }
            int seed = cfg.always_random_seed ? RandomNumberGenerator.GetInt32(int.MaxValue) : 1337;
            var rng = new Random(seed);
            for (int i = 0; i < features.Length; i++) {
                if (useDropout && rng.NextDouble() < dropoutP) {
                    features[i] = 0.0f;
                    continue;
                }
            }
            if (noiseStd > 0.0f || tinyNoiseStd > 0.0f) {
                int channels = cfg.model?.in_channels ?? 10;
                int tLen = features.Length / channels;
                int window = 50;
                float scale = (float)Math.Sqrt(window);

                for (int c = 0; c < channels; c++) {
                    float[] channelNoise = new float[tLen];
                    for (int t = 0; t < tLen; t++) {
                        float std = noiseStd;
                        if (tinyNoiseStd > 0.0f && (c == 0 || c == 6 || c == 7)) {
                            std = tinyNoiseStd;
                        }
                        channelNoise[t] = std > 0.0f ? NextGaussian(rng, std) : 0.0f;
                    }
                    float[] smoothedNoise = MovingAverage(channelNoise, window);
                    int offset = c * tLen;
                    for (int t = 0; t < tLen; t++) {
                        features[offset + t] += smoothedNoise[t] * scale;
                    }
                }
            }
            if (tinyNoiseStd > 0.0f && cfg.model != null && cfg.model.in_channels > 0) {
                int channels = cfg.model.in_channels;
                int tLen = features.Length / channels;
                // Apply tiny noise only to tone (0), interval_prev (6), interval_next (7).
                int[] targetChannels = new int[] { 0, 6, 7 };
                foreach (int c in targetChannels) {
                    if (c < 0 || c >= channels) {
                        continue;
                    }
                    int baseIdx = c * tLen;
                    for (int t = 0; t < tLen; t++) {
                        features[baseIdx + t] += NextGaussian(rng, tinyNoiseStd);
                    }
                }
            }
        }

        private static float NextGaussian(Random rng, float std) {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
            return (float)(std * randStdNormal);
        }

        private static float[] PredictFull(
            InferenceSession session,
            float[] features,
            long[]? lyricIds,
            int channels,
            int tLen,
            int chunkSize,
            int chunkOverlap,
            string lyricConditioning,
            float? styleId) {
            if (tLen == 0) {
                return Array.Empty<float>();
            }
            bool useLyric = lyricConditioning != "none";
            // Only feed inputs the graph actually declares. Sending an undeclared
            // input makes onnxruntime throw "Input name: X is not in the metadata",
            // which is what happened with models exported before the conditioning
            // inputs were named consistently: the sidecar config claimed a
            // class-like mode while the graph had no phoneme_class_ids input.
            var declaredInputs = new HashSet<string>(session.InputMetadata.Keys, StringComparer.Ordinal);
            Log.Information("[AutoPitch] tLen={tLen} chunkSize={chunkSize} chunkOverlap={chunkOverlap} channels={channels} lyricCond={lyric} declaredInputs=[{inputs}]",
                tLen, chunkSize, chunkOverlap, channels, lyricConditioning, string.Join(",", declaredInputs));
            int step = Math.Max(1, chunkSize - chunkOverlap);
            float[] output = new float[2 * tLen];
            float[] weight = new float[tLen];

            for (int start = 0; start < tLen; start += step) {
                int end = Math.Min(tLen, start + chunkSize);
                int winLen = end - start;
                int padLen = Math.Max(0, 2048 - winLen);
                int modelLen = winLen + padLen;

                float[] featChunk = new float[channels * modelLen];
                for (int c = 0; c < channels; c++) {
                    Array.Copy(features, c * tLen + start, featChunk, c * modelLen, winLen);
                }
                // CRITICAL FIX: padding frames must have ch1=1.0 (is_rest), not 0.0 (voiced).
                // With ch1=0, _note_idx_from_features treats padding as voiced frames,
                // polluting note_emb with zeros → NaN in attention.
                int padLen_actual = modelLen - winLen;
                if (channels > 1 && padLen_actual > 0) {
                    for (int t = winLen; t < modelLen; t++) {
                        featChunk[1 * modelLen + t] = 1.0f;  // ch1 = is_rest for padding
                    }
                }
                var inputs = new List<NamedOnnxValue> {
                    NamedOnnxValue.CreateFromTensor("features",
                        new DenseTensor<float>(featChunk, new int[] { 1, channels, modelLen }))
                };
                if (useLyric && lyricIds != null) {
                    bool classLike = lyricConditioning == "class" || lyricConditioning == "edge" || lyricConditioning == "edge_class" || lyricConditioning == "class_edge";
                    string preferred = classLike ? "phoneme_class_ids" : "lyric_ids";
                    string fallback = classLike ? "lyric_ids" : "phoneme_class_ids";
                    string? inputName =
                        declaredInputs.Contains(preferred) ? preferred :
                        declaredInputs.Contains(fallback) ? fallback : null;
                    if (inputName != null) {
                        long[] lyricChunk = new long[modelLen];
                        Array.Copy(lyricIds, start, lyricChunk, 0, winLen);
                        inputs.Add(NamedOnnxValue.CreateFromTensor(inputName,
                            new DenseTensor<long>(lyricChunk, new int[] { 1, modelLen })));
                    }
                }
                // note_idx is optional: when the graph does not declare it, the model
                // reconstructs note segmentation from features ch1/ch2 internally.
                if (declaredInputs.Contains("note_idx")) {
                    long[] noteIdxChunk = BuildNoteIdxChunk(featChunk, channels, modelLen, winLen);
                    inputs.Add(NamedOnnxValue.CreateFromTensor("note_idx",
                        new DenseTensor<long>(noteIdxChunk, new int[] { 1, modelLen })));
                }
                if (styleId.HasValue && declaredInputs.Contains("style_id")) {
                    float[] styleArr = new float[] { styleId.Value };
                    inputs.Add(NamedOnnxValue.CreateFromTensor("style_id",
                        new DenseTensor<float>(styleArr, new int[] { 1 })));
                }
                Log.Information("[AutoPitch] chunk start={start} winLen={winLen} padLen={padLen} modelLen={modelLen} inputs=[{inp}]",
                    start, winLen, padLen, modelLen, string.Join(",", inputs.Select(i => i.Name)));
                // Log a sample of the feature tensor to verify data is non-zero
                int voicedCount = 0;
                for (int t = 0; t < winLen; t++) { if (featChunk[1 * modelLen + t] < 0.5f) voicedCount++; }
                float featMin = featChunk.Take(channels * winLen).Min();
                float featMax = featChunk.Take(channels * winLen).Max();
                Log.Information("[AutoPitch] featChunk voiced_frames={voiced}/{total} feat_range=[{min:F4},{max:F4}]",
                    voicedCount, winLen, featMin, featMax);
                using var results = session.Run(inputs);
                var outputTensor = results.First(r => r.Name == "pitd_base_residual").AsTensor<float>();
                float[] pred = outputTensor.ToArray();
                // Log raw model output stats
                float predMin = pred.Take(winLen).Where(v => !float.IsNaN(v)).DefaultIfEmpty(0f).Min();
                float predMax = pred.Take(winLen).Where(v => !float.IsNaN(v)).DefaultIfEmpty(0f).Max();
                bool hasNaN = pred.Any(float.IsNaN);
                bool hasInf = pred.Any(float.IsInfinity);
                Log.Information("[AutoPitch] raw pred len={len} base_range=[{min:F5},{max:F5}] hasNaN={nan} hasInf={inf}",
                    pred.Length, predMin, predMax, hasNaN, hasInf);
                if (hasNaN) {
                    Log.Warning("[AutoPitch] NaN in model output (chunk start={start}, winLen={winLen}) — replacing with 0. Likely DirectML numerical instability with long sequences or many notes.",
                        start, winLen);
                    for (int i = 0; i < pred.Length; i++) {
                        if (float.IsNaN(pred[i]) || float.IsInfinity(pred[i])) {
                            pred[i] = 0f;
                        }
                    }
                }
                for (int t = 0; t < winLen; t++) {
                    output[start + t] += pred[t];
                    output[tLen + start + t] += pred[modelLen + t];
                    weight[start + t] += 1.0f;
                }
            }

            for (int t = 0; t < tLen; t++) {
                float w = Math.Max(weight[t], 1.0f);
                output[t] /= w;
                output[tLen + t] /= w;
            }
            return output;
        }

        /// <summary>
        /// Per-frame note indices derived from the feature channels, matching
        /// PitchTCN._note_idx_from_features on the Python side.
        ///
        /// Channel 1 is is_rest (1 - voiced) and channel 2 is a one-hot note_onset,
        /// so the note segmentation is already present in the features. Padding
        /// frames past winLen stay at -1 so they are excluded from attention.
        /// </summary>
        private static long[] BuildNoteIdxChunk(float[] featChunk, int channels, int modelLen, int winLen) {
            var noteIdx = new long[modelLen];
            long current = -1;
            bool prevVoiced = false;
            for (int t = 0; t < modelLen; t++) {
                if (t >= winLen || channels < 3) {
                    noteIdx[t] = -1;
                    prevVoiced = false;
                    continue;
                }
                bool voiced = featChunk[1 * modelLen + t] < 0.5f;
                bool onset = featChunk[2 * modelLen + t] > 0.5f;
                if (voiced && (onset || !prevVoiced)) {
                    current++;
                }
                noteIdx[t] = voiced ? Math.Max(current, 0) : -1;
                prevVoiced = voiced;
            }
            return noteIdx;
        }

        private static float[] MovingAverage(float[] data, int window) {
            if (window <= 1 || data.Length == 0) {
                return data;
            }
            float[] outData = new float[data.Length];
            int radius = window / 2;
            for (int i = 0; i < data.Length; i++) {
                int start = Math.Max(0, i - radius);
                int end = Math.Min(data.Length, i - radius + window);
                float sum = 0.0f;
                for (int j = start; j < end; j++) {
                    sum += data[j];
                }
                outData[i] = sum / Math.Max(1, end - start);
            }
            return outData;
        }

        private static SimplifiedCurve SimplifyCurve(
            int[] ticks,
            float[] pitd,
            float[] mask,
            float epsilon,
            HashSet<int> forcedTicks) {
            var xsOut = new List<int>();
            var ysOut = new List<float>();
            foreach (var segment in MaskToSegments(mask)) {
                int start = segment.start;
                int end = segment.end;
                if (end <= start) {
                    continue;
                }
                int len = end - start;
                int[] xsSeg = new int[len];
                float[] ysSeg = new float[len];
                Array.Copy(ticks, start, xsSeg, 0, len);
                Array.Copy(pitd, start, ysSeg, 0, len);
                var simplified = RdpSimplify(xsSeg, ysSeg, epsilon);
                xsOut.AddRange(simplified.xs);
                ysOut.AddRange(simplified.ys);
            }

            if (forcedTicks != null && forcedTicks.Count > 0) {
                foreach (int tick in forcedTicks.OrderBy(t => t)) {
                    if (tick < ticks[0] || tick > ticks[^1]) {
                        continue;
                    }
                    float y = Interp(ticks, pitd, tick);
                    xsOut.Add(tick);
                    ysOut.Add(y);
                }
            }

            return new SimplifiedCurve(xsOut, ysOut);
        }

        private static void AddBoundaryAnchors(
            List<int> xs, List<float> ys, int startTick, int endTick) {
            if (xs == null || ys == null || xs.Count == 0 || ys.Count == 0) {
                return;
            }
            if (startTick > endTick) {
                return;
            }
            RemoveTick(xs, ys, startTick);
            RemoveTick(xs, ys, endTick);
            xs.Add(startTick);
            ys.Add(0.0f);
            if (endTick != startTick) {
                xs.Add(endTick);
                ys.Add(0.0f);
            }
        }

        private static void RemoveTick(List<int> xs, List<float> ys, int tick) {
            for (int i = xs.Count - 1; i >= 0; i--) {
                if (xs[i] == tick) {
                    xs.RemoveAt(i);
                    ys.RemoveAt(i);
                }
            }
        }

        private static IEnumerable<(int start, int end)> MaskToSegments(float[] mask) {
            int start = -1;
            for (int i = 0; i < mask.Length; i++) {
                bool voiced = mask[i] > 0.5f;
                if (voiced && start < 0) {
                    start = i;
                } else if (!voiced && start >= 0) {
                    yield return (start, i);
                    start = -1;
                }
            }
            if (start >= 0) {
                yield return (start, mask.Length);
            }
        }

        private static SimplifiedCurve RdpSimplify(int[] xs, float[] ys, float epsilon) {
            int n = xs.Length;
            if (n <= 2) {
                return new SimplifiedCurve(xs.ToList(), ys.ToList());
            }
            bool[] keep = new bool[n];
            keep[0] = true;
            keep[n - 1] = true;
            var stack = new Stack<(int start, int end)>();
            stack.Push((0, n - 1));

            while (stack.Count > 0) {
                var (start, end) = stack.Pop();
                if (end - start <= 1) {
                    continue;
                }
                int x1 = xs[start];
                float y1 = ys[start];
                int x2 = xs[end];
                float y2 = ys[end];
                double dx = x2 - x1;
                double dy = y2 - y1;
                double maxDist = -1.0;
                int maxIdx = -1;
                for (int i = start + 1; i < end; i++) {
                    double dist;
                    if (dx == 0 && dy == 0) {
                        dist = Math.Abs(ys[i] - y1);
                    } else {
                        double numer = Math.Abs(dy * xs[i] - dx * ys[i] + x2 * y1 - y2 * x1);
                        double denom = Math.Sqrt(dx * dx + dy * dy);
                        dist = numer / (denom + 1e-8);
                    }
                    if (dist > maxDist) {
                        maxDist = dist;
                        maxIdx = i;
                    }
                }
                if (maxIdx >= 0 && maxDist > epsilon) {
                    keep[maxIdx] = true;
                    stack.Push((start, maxIdx));
                    stack.Push((maxIdx, end));
                }
            }

            var outXs = new List<int>();
            var outYs = new List<float>();
            for (int i = 0; i < n; i++) {
                if (keep[i]) {
                    outXs.Add(xs[i]);
                    outYs.Add(ys[i]);
                }
            }
            return new SimplifiedCurve(outXs, outYs);
        }

        private static float Interp(int[] xs, float[] ys, int x) {
            int idx = Array.BinarySearch(xs, x);
            if (idx >= 0) {
                return ys[idx];
            }
            idx = ~idx;
            if (idx <= 0) {
                return ys[0];
            }
            if (idx >= xs.Length) {
                return ys[^1];
            }
            int x0 = xs[idx - 1];
            int x1 = xs[idx];
            float y0 = ys[idx - 1];
            float y1 = ys[idx];
            if (x1 == x0) {
                return y0;
            }
            return y0 + (x - x0) * (y1 - y0) / (x1 - x0);
        }

        private static (List<int> xs, List<int> ys) ValidateCurve(
            List<int> xs, List<float> ys, float clamp) {
            if (xs.Count == 0 || ys.Count == 0) {
                return (new List<int>(), new List<int>());
            }
            var order = xs
                .Select((x, i) => new { x, i })
                .OrderBy(p => p.x)
                .Select(p => p.i)
                .ToArray();
            var xsOut = new List<int>();
            var ysOut = new List<int>();
            int lastX = int.MinValue;
            foreach (int idx in order) {
                int x = xs[idx];
                if (x <= lastX) {
                    continue;
                }
                float y = Math.Clamp(ys[idx], -clamp, clamp);
                xsOut.Add(x);
                ysOut.Add((int)MathF.Round(y));
                lastX = x;
            }
            return (xsOut, ysOut);
        }

        private static (List<int> xs, List<int> ys) MergeCurves(
            int[] existingXs, int[] existingYs,
            List<int> newXs, List<float> newYs,
            int rangeStart, int rangeEnd,
            float clamp) {
            if (existingXs == null || existingYs == null || existingXs.Length == 0 || existingYs.Length == 0) {
                return ValidateCurve(newXs, newYs, clamp);
            }
            var xs = new List<int>();
            var ys = new List<float>();
            for (int i = 0; i < existingXs.Length; i++) {
                int x = existingXs[i];
                if (x < rangeStart || x > rangeEnd) {
                    xs.Add(x);
                    ys.Add(existingYs[i]);
                }
            }
            xs.AddRange(newXs);
            ys.AddRange(newYs);
            return ValidateCurve(xs, ys, clamp);
        }

        private static int[] BuildEdgeIdsForNotes(List<UNote> notes) {
            // Default EDGE_REST, not 0: ID 0 is "vowel_start_consonant_end".
            var parsed = notes.Select(n => ParseSyllableMarker(n.lyric)).ToList();
            var groups = new List<List<int>>();
            var current = new List<int>();
            for (int i = 0; i < parsed.Count; i++) {
                var marker = parsed[i].marker;
                if (marker == "continue") {
                    if (current.Count > 0) {
                        current.Add(i);
                    } else if (groups.Count > 0) {
                        groups[^1].Add(i);
                    } else {
                        current.Add(i);
                    }
                    continue;
                }
                if (marker == "new") {
                    if (current.Count > 0) {
                        groups.Add(new List<int>(current));
                    }
                    current.Clear();
                    current.Add(i);
                    continue;
                }
                if (current.Count > 0) {
                    groups.Add(new List<int>(current));
                    current.Clear();
                }
                groups.Add(new List<int> { i });
            }
            if (current.Count > 0) {
                groups.Add(current);
            }
            // Default to the rest edge class: ID 0 is vowel_start_consonant_end, a real
            // sound, so notes yielding no usable token would look like sung vowels.
            var edgeIds = new int[notes.Count];
            Array.Fill(edgeIds, EDGE_REST);
            foreach (var group in groups) {
                string? startToken = null;
                string? endToken = null;
                foreach (int idx in group) {
                    var entry = parsed[idx];
                    string source = string.IsNullOrWhiteSpace(entry.core)
                        ? ExtractPrimaryLyricToken(notes[idx].lyric)
                        : entry.core;
                    string token = NormalizePhoneme(source);
                    if (string.IsNullOrWhiteSpace(token)) {
                        continue;
                    }
                    if (startToken == null) {
                        startToken = token;
                    }
                    endToken = token;
                }
                startToken ??= "R";
                endToken ??= startToken;
                int edgeId = ClassifyNoteEdge(startToken, endToken);
                foreach (int idx in group) {
                    edgeIds[idx] = edgeId;
                }
            }
            return edgeIds;
        }

        private static int[] BuildEdgeClassIdsForNotes(List<UNote> notes) {
            var parsed = notes.Select(n => ParseSyllableMarker(n.lyric)).ToList();
            var groups = new List<List<int>>();
            var current = new List<int>();
            for (int i = 0; i < parsed.Count; i++) {
                var marker = parsed[i].marker;
                if (marker == "continue") {
                    if (current.Count > 0) {
                        current.Add(i);
                    } else if (groups.Count > 0) {
                        groups[^1].Add(i);
                    } else {
                        current.Add(i);
                    }
                    continue;
                }
                if (marker == "new") {
                    if (current.Count > 0) {
                        groups.Add(new List<int>(current));
                    }
                    current.Clear();
                    current.Add(i);
                    continue;
                }
                if (current.Count > 0) {
                    groups.Add(new List<int>(current));
                    current.Clear();
                }
                groups.Add(new List<int> { i });
            }
            if (current.Count > 0) {
                groups.Add(current);
            }
            // Default to rest/rest in the class-pair space rather than 0 (vowel/vowel).
            var edgeIds = new int[notes.Count];
            Array.Fill(edgeIds, ClassifyNoteEdgeClass("R", "R"));
            foreach (var group in groups) {
                string? startToken = null;
                string? endToken = null;
                foreach (int idx in group) {
                    var entry = parsed[idx];
                    string source = string.IsNullOrWhiteSpace(entry.core)
                        ? ExtractPrimaryLyricToken(notes[idx].lyric)
                        : entry.core;
                    string token = NormalizePhoneme(source);
                    if (string.IsNullOrWhiteSpace(token)) {
                        continue;
                    }
                    if (startToken == null) {
                        startToken = token;
                    }
                    endToken = token;
                }
                startToken ??= "R";
                endToken ??= startToken;
                int edgeId = ClassifyNoteEdgeClass(startToken, endToken);
                foreach (int idx in group) {
                    edgeIds[idx] = edgeId;
                }
            }
            return edgeIds;
        }

        private static (string marker, string core) ParseSyllableMarker(string lyric) {
            string token = ExtractPrimaryLyricToken(lyric);
            if (token.StartsWith("+~", StringComparison.Ordinal)) {
                return ("continue", token.Substring(2).Trim());
            }
            if (token.StartsWith("-", StringComparison.Ordinal)) {
                return ("continue", token.Substring(1).Trim());
            }
            if (token.StartsWith("+", StringComparison.Ordinal)) {
                if (token.Length == 1) {
                    return ("new", "+");
                }
                return ("new", token.Substring(1).Trim());
            }
            return ("single", token);
        }

        private static string ExtractPrimaryLyricToken(string lyric) {
            if (string.IsNullOrWhiteSpace(lyric)) {
                return string.Empty;
            }
            string token = lyric.Trim();
            if (token.Contains(' ')) {
                var parts = token.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 1) {
                    token = parts[1];
                }
            }
            return token.Trim();
        }

        private static string NormalizePhoneme(string lyric) {
            if (string.IsNullOrWhiteSpace(lyric)) {
                return "R";
            }
            string token = ExtractPrimaryLyricToken(lyric);
            if (token.StartsWith("+~", StringComparison.Ordinal)) {
                token = token.Substring(2).Trim();
            } else if (token.StartsWith("-", StringComparison.Ordinal)) {
                token = token.Substring(1).Trim();
            } else if (token.StartsWith("+", StringComparison.Ordinal)) {
                if (token.Length > 1) {
                    token = token.Substring(1).Trim();
                }
            }
            if (string.IsNullOrWhiteSpace(token)) {
                return "R";
            }
            token = PhoneticHintPattern.Replace(token, string.Empty);
            token = ParenPattern.Replace(token, string.Empty);
            token = token.Trim();
            if (ProtectedTokens.Contains(token)) {
                return token;
            }
            if (token == ".hh" || token == "hh") {
                return "hh";
            }
            if (ContainsKatakana(token)) {
                token = KatakanaToHiragana(token);
            }
            if (TryRomajiToHiragana(token, out string kana)) {
                return kana;
            }
            token = StripBracketsQuotesPattern.Replace(token, string.Empty);
            token = AllowedCharsPattern.Replace(token, string.Empty);
            token = token.Trim();
            if (TryRomajiToHiragana(token, out kana)) {
                return kana;
            }
            return token;
        }

        private static string ResolveLyricConditioning(AutoPitchConfig cfg) {
            string mode = cfg.lyric_conditioning ?? cfg.model?.lyric_conditioning ?? string.Empty;
            if (mode == "vocab" || mode == "class" || mode == "edge" || mode == "edge_class" || mode == "class_edge" || mode == "none") {
                return mode;
            }
            bool useLyric = cfg.use_lyric || (cfg.model?.use_lyric ?? false);
            return useLyric ? "vocab" : "none";
        }

        private static int GetConditioningClassCount(AutoPitchConfig cfg, string lyricConditioning) {
            if (cfg?.phoneme_class_names != null && cfg.phoneme_class_names.Count > 0) {
                return cfg.phoneme_class_names.Count;
            }
            return lyricConditioning switch {
                "class" => CLASS_COUNT,
                "edge" => 7,
                "edge_class" => CLASS_COUNT * CLASS_COUNT,
                "class_edge" => CLASS_COUNT * EDGE_CLASS_COUNT,
                _ => 0,
            };
        }

        /// <summary>
        /// Prefix -> class, longest prefix first. Must mirror
        /// phoneme_classes._PREFIX_LOOKUP on the Python side.
        ///
        /// The previous if-chain tested whole groups in order, so a short prefix in
        /// an earlier group masked a longer prefix in a later one: "ts" and "ky"
        /// both classified as stop (via "t"/"k") instead of affricate/glide, and
        /// the multi-char kana in AffricatePrefixes lost to single-char stops.
        /// Ordering by descending prefix length fixes that; the original group
        /// order is preserved only as the tie-break between equal-length prefixes.
        /// </summary>
        // Lazily built: static field initializers run in declaration order, and the
        // prefix arrays below are declared AFTER this field. Eager initialization
        // therefore read them while they were still null and threw
        // NullReferenceException from the static constructor.
        private static (string prefix, int cls)[]? _prefixLookup;

        private static (string prefix, int cls)[] PrefixLookup =>
            _prefixLookup ??= BuildPrefixLookup();

        private static (string prefix, int cls)[] BuildPrefixLookup() {
            var groups = new (int cls, string[] prefixes)[] {
                (1, NasalPrefixes),
                (2, LiquidPrefixes),
                (3, SibilantPrefixes),
                (6, AffricatePrefixes),
                (4, StopPrefixes),
                (5, FricativePrefixes),
                (7, GlidePrefixes),
            };
            var seen = new HashSet<string>();
            var entries = new List<(string prefix, int cls, int order)>();
            int order = 0;
            foreach (var (cls, prefixes) in groups) {
                foreach (var prefix in prefixes) {
                    if (string.IsNullOrEmpty(prefix) || !seen.Add(prefix)) {
                        continue;  // first group wins an exact duplicate
                    }
                    entries.Add((prefix, cls, order++));
                }
            }
            return entries
                .OrderByDescending(e => e.prefix.Length)
                .ThenBy(e => e.order)
                .Select(e => (e.prefix, e.cls))
                .ToArray();
        }

        private static int ClassifyPhoneme(string token) {
            if (IsRestToken(token)) {
                return 9;
            }
            if (IsNoiseToken(token)) {
                return 8;
            }
            if (VowelTokens.Contains(token)) {
                return 0;
            }
            if (token == "ん") {
                return 1;
            }
            if (!string.IsNullOrEmpty(token)) {
                foreach (var (prefix, cls) in PrefixLookup) {
                    if (token.StartsWith(prefix, StringComparison.Ordinal)) {
                        return cls;
                    }
                }
            }
            return 10;
        }

        /// <summary>
        /// Class ID meaning "no note sounding here", per conditioning mode.
        /// Must match phoneme_classes.conditioning_rest_id() on the Python side:
        /// a mismatch feeds the model conditioning it was never trained on.
        /// </summary>
        private static long ConditioningRestId(string lyricConditioning) {
            const int REST = 9;
            switch (lyricConditioning) {
                case "class":
                    return REST;
                case "edge":
                    return EDGE_REST;
                case "edge_class":
                    return REST * CLASS_COUNT + REST;
                case "class_edge":
                    return REST * EDGE_CLASS_COUNT + EDGE_REST;
                default:
                    return 0;  // "vocab" -> <pad>, "none" -> unused
            }
        }

        private static int ClassifyNoteEdge(string startToken, string endToken) {
            string s = startToken?.Trim() ?? string.Empty;
            string e = endToken?.Trim() ?? s;
            if (IsRestToken(s) && IsRestToken(e)) {
                return EDGE_REST;
            }
            if (IsNoiseToken(s) || IsNoiseToken(e)) {
                return EDGE_NOISE;
            }
            int startKind = GetEdgeStartKind(s);
            int endKind = GetEdgeEndKind(e);
            if (startKind == SymVowel && endKind == SymConsonant) {
                return EDGE_VOWEL_START_CONSONANT_END;
            }
            if (startKind == SymVowel && endKind == SymVowel) {
                return EDGE_VOWEL_START_VOWEL_END;
            }
            if (startKind == SymConsonant && endKind == SymConsonant) {
                return EDGE_CONSONANT_START_CONSONANT_END;
            }
            if (startKind == SymConsonant && endKind == SymVowel) {
                return EDGE_CONSONANT_START_VOWEL_END;
            }
            return EDGE_OTHER;
        }

        private static int ClassifyNoteEdgeClass(string startToken, string endToken) {
            int startClass = ClassifyPhoneme(startToken);
            int endClass = ClassifyPhoneme(endToken);
            return startClass * CLASS_COUNT + endClass;
        }

        private static int GetEdgeStartKind(string token) {
            if (TryArpabetKinds(token, out int first, out _)) {
                return first;
            }
            if (TryKanaKinds(token, out first, out _)) {
                return first;
            }
            var letters = token.Where(c => char.IsLetter(c) || IsKana(c) || c == '+').ToArray();
            if (letters.Length == 0) {
                return SymUnknown;
            }
            return CharKind(letters[0]);
        }

        private static int GetEdgeEndKind(string token) {
            if (TryArpabetKinds(token, out _, out int last)) {
                return last;
            }
            if (TryKanaKinds(token, out _, out last)) {
                return last;
            }
            var letters = token.Where(c => char.IsLetter(c) || IsKana(c) || c == '+').ToArray();
            if (letters.Length == 0) {
                return SymUnknown;
            }
            return CharKind(letters[^1]);
        }

        private static bool TryArpabetKinds(string token, out int startKind, out int endKind) {
            startKind = SymUnknown;
            endKind = SymUnknown;
            var parts = Regex.Split(token ?? string.Empty, @"[\s/_\-]+")
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToArray();
            if (parts.Length == 0) {
                return false;
            }
            int[] kinds = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++) {
                string phone = ArpabetStressPattern.Replace(parts[i], string.Empty).ToUpperInvariant();
                if (ArpabetVowels.Contains(phone)) {
                    kinds[i] = SymVowel;
                } else if (ArpabetConsonants.Contains(phone)) {
                    kinds[i] = SymConsonant;
                } else {
                    return false;
                }
            }
            startKind = kinds[0];
            endKind = kinds[^1];
            return true;
        }

        private static bool TryKanaKinds(string token, out int startKind, out int endKind) {
            startKind = SymUnknown;
            endKind = SymUnknown;
            var kana = (token ?? string.Empty).Where(IsKana).ToArray();
            if (kana.Length == 0) {
                return false;
            }
            startKind = KanaVowels.Contains(kana[0]) ? SymVowel : SymConsonant;
            endKind = KanaConsonantEnd.Contains(kana[^1]) ? SymConsonant : SymVowel;
            return true;
        }

        private static bool IsRestToken(string token) {
            string t = token?.Trim() ?? string.Empty;
            return t.Length == 0 || RestTokens.Contains(t) || RestTokensLower.Contains(t.ToLowerInvariant());
        }

        private static bool IsNoiseToken(string token) {
            string t = token?.Trim() ?? string.Empty;
            return t.Length > 0 && (NoiseTokens.Contains(t) || NoiseTokensLower.Contains(t.ToLowerInvariant()));
        }

        private static int CharKind(char ch) {
            char lower = char.ToLowerInvariant(ch);
            if (EnVowels.Contains(lower) || RuVowels.Contains(lower) || KanaVowels.Contains(ch) || ch == '+') {
                return SymVowel;
            }
            return (char.IsLetter(ch) || IsKana(ch)) ? SymConsonant : SymUnknown;
        }

        private static bool IsKana(char ch) {
            int code = ch;
            return (code >= 0x3040 && code <= 0x309F) || (code >= 0x30A0 && code <= 0x30FF);
        }

        private static bool StartsWithAny(string token, string[] prefixes) {
            if (string.IsNullOrEmpty(token)) {
                return false;
            }
            foreach (var prefix in prefixes) {
                if (token.StartsWith(prefix, StringComparison.Ordinal)) {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsKatakana(string text) {
            foreach (char ch in text) {
                int code = ch;
                if (code >= 0x30A1 && code <= 0x30F6) {
                    return true;
                }
            }
            return false;
        }

        private static string KatakanaToHiragana(string text) {
            var chars = text.ToCharArray();
            for (int i = 0; i < chars.Length; i++) {
                int code = chars[i];
                if (code >= 0x30A1 && code <= 0x30F6) {
                    chars[i] = (char)(code - 0x60);
                }
            }
            return new string(chars);
        }

        private static bool TryRomajiToHiragana(string token, out string converted) {
            converted = string.Empty;
            if (string.IsNullOrWhiteSpace(token)) {
                return false;
            }
            if (token.Any(char.IsUpper)) {
                return false;
            }
            string lower = token.ToLowerInvariant();
            if (!RomajiToHiragana.TryGetValue(lower, out converted!)) {
                return false;
            }
            return true;
        }

        private const int EDGE_VOWEL_START_CONSONANT_END = 0;
        private const int EDGE_VOWEL_START_VOWEL_END = 1;
        private const int EDGE_CONSONANT_START_CONSONANT_END = 2;
        private const int EDGE_CONSONANT_START_VOWEL_END = 3;
        private const int EDGE_REST = 4;
        private const int EDGE_NOISE = 5;
        private const int EDGE_OTHER = 6;
        private const int EDGE_CLASS_COUNT = 7;

        private const int SymUnknown = 0;
        private const int SymVowel = 1;
        private const int SymConsonant = 2;
        private const int CLASS_COUNT = 11;

        private static readonly Regex PhoneticHintPattern = new Regex(@"\[(.*?)\]", RegexOptions.Compiled);
        private static readonly Regex ParenPattern = new Regex(@"\(.*?\)", RegexOptions.Compiled);
        private static readonly Regex StripBracketsQuotesPattern = new Regex(@"[\[\]""'`]", RegexOptions.Compiled);
        private static readonly Regex AllowedCharsPattern = new Regex(@"[^\u0400-\u04FF\u3040-\u309F\u30A0-\u30FF\u4E00-\u9FFF\u3000-\u303Fa-zA-Z]", RegexOptions.Compiled);
        private static readonly Regex ArpabetStressPattern = new Regex(@"\d+$", RegexOptions.Compiled);

        private static readonly HashSet<string> ProtectedTokens = new HashSet<string>(StringComparer.Ordinal) {
            "<pad>", "R", "br", "bre", "cl", "vf", "hh", "iR", "ahh", "ihh", "uhh", "ohh", "ehh", "+"
        };
        private static readonly HashSet<string> RestTokens = new HashSet<string>(StringComparer.Ordinal) {
            "", "R", "SP", "R2", "R吸", "R裏", "R・", ".hh", "hh", ".h", "・",
        };
        private static readonly HashSet<string> RestTokensLower =
            new HashSet<string>(RestTokens.Select(t => t.ToLowerInvariant()), StringComparer.Ordinal);
        private static readonly HashSet<string> NoiseTokens = new HashSet<string>(StringComparer.Ordinal) {
            "br", "bre", "cl", "vf", "iR", "ahh", "ihh", "uhh", "ohh", "ehh", "AP", "吸", "R囁",
        };
        private static readonly HashSet<string> NoiseTokensLower =
            new HashSet<string>(NoiseTokens.Select(t => t.ToLowerInvariant()), StringComparer.Ordinal);
        private static readonly HashSet<string> VowelTokens = new HashSet<string>(StringComparer.Ordinal) {
            "あ", "い", "う", "え", "お", "ぁ", "ぃ", "ぅ", "ぇ", "ぉ", "を", "うぉ", "うぃ", "うぇ", "いぇ", "+"
        };
        private static readonly string[] NasalPrefixes = new string[] {
            "ん", "な", "に", "ぬ", "ね", "の", "ま", "み", "む", "め", "も", "n", "m",
        };
        private static readonly string[] LiquidPrefixes = new string[] { "ら", "り", "る", "れ", "ろ", "l", "r" };
        private static readonly string[] SibilantPrefixes = new string[] {
            "さ", "し", "す", "せ", "そ", "ざ", "じ", "ず", "ぜ", "ぞ",
            "しゃ", "しゅ", "しょ", "じゃ", "じゅ", "じょ", "すぃ", "ずぃ",
            "s", "S", "sh", "SH",
        };
        private static readonly string[] AffricatePrefixes = new string[] {
            "ち", "つ", "ちゃ", "ちゅ", "ちょ", "ちぇ", "てぃ", "とぅ", "てゅ",
            "つぁ", "つぃ", "つぇ", "つぉ", "ぢ", "づ", "ぢゃ", "ぢゅ", "ぢょ", "っ", "ts", "CH",
        };
        private static readonly string[] StopPrefixes = new string[] {
            "か", "き", "く", "け", "こ", "が", "ぎ", "ぐ", "げ", "ご",
            "た", "て", "と", "だ", "で", "ど", "てぃ", "とぅ", "でぃ", "どぅ",
            "ぱ", "ぴ", "ぷ", "ぺ", "ぽ", "ば", "び", "ぶ", "べ", "ぼ",
            "t", "k", "p", "b", "d", "g", "T", "K", "P", "B", "D", "G",
        };
        private static readonly string[] FricativePrefixes = new string[] {
            "は", "ひ", "ふ", "へ", "ほ", "ふぁ", "ふぃ", "ふぇ", "ふぉ", "f", "h", "F", "H", "ゔ", "ゔぃ",
        };
        private static readonly string[] GlidePrefixes = new string[] {
            "や", "ゆ", "よ", "わ", "ゐ", "ゑ", "ゃ", "ゅ", "ょ", "ゎ", "ky", "KY",
        };

        private static readonly HashSet<char> EnVowels = new HashSet<char>("aeiouy");
        private static readonly HashSet<char> RuVowels = new HashSet<char>("аеёиоуыэюя");
        private static readonly HashSet<char> KanaVowels = new HashSet<char>("あいうえおぁぃぅぇぉをゐゑアイウエオ+");
        private static readonly HashSet<char> KanaConsonantEnd = new HashSet<char>("んっンッ");

        private static readonly HashSet<string> ArpabetVowels = new HashSet<string>(StringComparer.Ordinal) {
            "AA", "AE", "AH", "AO", "AW", "AX", "AXR", "AY", "EH", "ER", "EY", "IH", "IX", "IY", "OW", "OY", "UH", "UW", "UX",
        };
        private static readonly HashSet<string> ArpabetConsonants = new HashSet<string>(StringComparer.Ordinal) {
            "B", "CH", "D", "DH", "DX", "EL", "EM", "EN", "F", "G", "HH", "JH", "K", "L", "M", "N", "NG", "NX", "P", "Q", "R", "S", "SH", "T", "TH", "V", "W", "WH", "Y", "Z", "ZH",
        };
        private static readonly Dictionary<string, string> RomajiToHiragana = new Dictionary<string, string>(StringComparer.Ordinal) {
            {"a", "あ"}, {"i", "い"}, {"u", "う"}, {"e", "え"}, {"o", "お"},
            {"ka", "か"}, {"ki", "き"}, {"ku", "く"}, {"ke", "け"}, {"ko", "こ"},
            {"sa", "さ"}, {"shi", "し"}, {"su", "す"}, {"se", "せ"}, {"so", "そ"},
            {"ta", "た"}, {"chi", "ち"}, {"tsu", "つ"}, {"te", "て"}, {"to", "と"},
            {"na", "な"}, {"ni", "に"}, {"nu", "ぬ"}, {"ne", "ね"}, {"no", "の"},
            {"ha", "は"}, {"hi", "ひ"}, {"fu", "ふ"}, {"he", "へ"}, {"ho", "ほ"},
            {"ma", "ま"}, {"mi", "み"}, {"mu", "む"}, {"me", "め"}, {"mo", "も"},
            {"ya", "や"}, {"yu", "ゆ"}, {"yo", "よ"},
            {"ra", "ら"}, {"ri", "り"}, {"ru", "る"}, {"re", "れ"}, {"ro", "ろ"},
            {"wa", "わ"}, {"wo", "を"}, {"n", "ん"},
            {"ga", "が"}, {"gi", "ぎ"}, {"gu", "ぐ"}, {"ge", "げ"}, {"go", "ご"},
            {"za", "ざ"}, {"ji", "じ"}, {"zu", "ず"}, {"ze", "ぜ"}, {"zo", "ぞ"},
            {"da", "だ"}, {"de", "で"}, {"do", "ど"},
            {"ba", "ば"}, {"bi", "び"}, {"bu", "ぶ"}, {"be", "べ"}, {"bo", "ぼ"},
            {"pa", "ぱ"}, {"pi", "ぴ"}, {"pu", "ぷ"}, {"pe", "ぺ"}, {"po", "ぽ"},
            {"kya", "きゃ"}, {"kyu", "きゅ"}, {"kyo", "きょ"},
            {"gya", "ぎゃ"}, {"gyu", "ぎゅ"}, {"gyo", "ぎょ"},
            {"sha", "しゃ"}, {"shu", "しゅ"}, {"sho", "しょ"},
            {"cha", "ちゃ"}, {"chu", "ちゅ"}, {"cho", "ちょ"},
            {"ja", "じゃ"}, {"ju", "じゅ"}, {"jo", "じょ"},
            {"nya", "にゃ"}, {"nyu", "にゅ"}, {"nyo", "にょ"},
            {"rya", "りゃ"}, {"ryu", "りゅ"}, {"ryo", "りょ"},
            {"ye", "いぇ"}, {"tei", "てい"}, {"ti", "てぃ"}, {"tu", "とぅ"},
            {"fa", "ふぁ"}, {"fi", "ふぃ"}, {"fe", "ふぇ"}, {"fo", "ふぉ"},
            {"+", "+"},
        };
        private class AutoPitchAssets {
            public string BasePath;
            public InferenceSession Session;
            public AutoPitchConfig Config;
            public Dictionary<string, int> Vocab;
            public string ConfigPath;
            public string VocabPath;
        }

        private class FeatureBundle {
            public float[] features;
            public float[] mask;
            public long[]? lyricIds;

            public FeatureBundle(float[] features, float[] mask, long[]? lyricIds) {
                this.features = features;
                this.mask = mask;
                this.lyricIds = lyricIds;
            }
        }

        private class SimplifiedCurve {
            public List<int> xs;
            public List<float> ys;

            public SimplifiedCurve(List<int> xs, List<float> ys) {
                this.xs = xs;
                this.ys = ys;
            }
        }

        private class AutoPitchConfig {
            public DataConfig data = new DataConfig();
            public ModelConfig model = new ModelConfig();
            public InferConfig infer = new InferConfig();
            public bool always_random_seed = true;
            public bool stochastic_dropout = false;
            public float? dropout_p = null;
            public float? feat_noise_std = null;
            public float? feat_noise_std_tiny = null;
            public bool use_lyric = false;
            public string lyric_conditioning = "vocab";
            public int selection_pad_left = 100;
            public int selection_pad_right = 50;
            public string style = "base";
            public float style_strength = 1.0f;
            public List<string> phoneme_class_names = new List<string>();
        }

        private class DataConfig {
            public int dt = 2;
            public int window_steps = 4096;
            public int window_stride = 512;
            public float note_len_norm = 480.0f;
            public float note_len_clip = 4.0f;
            public float note_len_scale = 4.0f;
            public float pitch_center = 60.0f;
            public float pitch_scale = 24.0f;
            public int interval_clip = 24;
            public float interval_scale = 24.0f;
            public float pitd_clamp = 1200.0f;
            public int post_note_tail_ticks = 24;
            public int post_note_tail_max_gap_ticks = 96;
            // BPM-derived feature config (used when in_channels >= 12)
            public float bpm_default = 120.0f;
            public float bpm_scale = 120.0f;
            public int ticks_per_beat = 480;
            public float note_dur_sec_clip = 2.0f;
            public float note_dur_sec_scale = 1.0f;
        }

        private class ModelConfig {
            public int in_channels = 10;
            public int channels = 64;
            public int num_blocks = 8;
            public int kernel_size = 7;
            public float dropout = 0.1f;
            public bool use_lyric = false;
            public string lyric_conditioning = "vocab";
            public int lyric_embed_dim = 16;
            public bool style_conditioning = false;
        }

        private class InferConfig {
            public int smooth_window = 2;
            public float rdp_epsilon = 12.0f;
            public int chunk_size = 2048;
            public int chunk_overlap = 128;
            public int pad_ticks = 10;
            public float clamp_output = 1200.0f;
        }
    }

    public class AutoPitchUnload : BatchEdit {
        public string Name => "AutoPitch: unload model";

        public void Run(UProject project, UVoicePart part, List<UNote> selectedNotes, DocManager docManager) {
            AutoPitchEdit.ClearCache();
        }
    }
}
    
