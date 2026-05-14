// This file contains code adapted from the hifisampler project.
// It was rewritten and modified for OpenUtauMobile2.
// See THIRD_PARTY_NOTICES.md and licenses/HifiSampler.Apache-2.0.txt.

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Format;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtauMobile.Plugin.Renderers.HifiSampler {
    [Renderer("HIFISAMPLER", USingerType.Classic)]
    public class HifiSamplerRenderer : IRenderer {
        const string VocoderPkg = "pc_nsf_hifigan_44.1k_hop512_128bin_2025.02";
        const double FrameMs = HifiSamplerDsp.HopSize * 1000.0 / HifiSamplerDsp.SampleRate; // ~11.61ms per mel frame
        const int FillFrames = 6;
        const bool LoopMode = false;
        const int MaxSourceAudioCacheEntries = 32;
        const int MaxFeatureCacheEntries = 128;

        volatile InferenceSession vocoderSession;
        InferenceSession hnsepSession;
        readonly object modelsLock = new object();

        static readonly object sourceCacheLock = new object();
        static readonly Dictionary<string, float[]> sourceAudioCache = new Dictionary<string, float[]>();
        static readonly Queue<string> sourceAudioCacheOrder = new Queue<string>();
        static readonly object featureCacheLock = new object();
        static readonly Dictionary<string, CachedFeatures> featureCache = new Dictionary<string, CachedFeatures>();
        static readonly Queue<string> featureCacheOrder = new Queue<string>();

        sealed class CachedFeatures {
            public float[,] MelOrigin;
            public float FeatureScale;
        }

        static readonly HashSet<string> supportedExp = new HashSet<string> {
            Ustx.DYN, Ustx.PITD, Ustx.CLR, Ustx.SHFT,
            Ustx.VEL, Ustx.VOL, Ustx.MOD,
            Ustx.GENC, Ustx.TENC, Ustx.BREC,
            Ustx.DIR,
        };

        public USingerType SingerType => USingerType.Classic;
        public bool SupportsRenderPitch => false;

        public bool SupportsExpression(UExpressionDescriptor descriptor) {
            return supportedExp.Contains(descriptor.abbr);
        }

        public RenderResult Layout(RenderPhrase phrase) {
            return new RenderResult {
                leadingMs = phrase.leadingMs,
                positionMs = phrase.positionMs,
                estimatedLengthMs = phrase.durationMs + phrase.leadingMs,
            };
        }

        public Task<RenderResult> Render(RenderPhrase phrase, Progress progress, int trackNo,
            CancellationTokenSource cancellation, bool isPreRender) {
            var resamplerItems = new List<ResamplerItem>();
            foreach (var phone in phrase.phones) {
                resamplerItems.Add(new ResamplerItem(phrase, phone));
            }
            var task = Task.Run(() => {
                var result = Layout(phrase);
                var wavPath = Path.Join((string?)PathManager.Inst.CachePath, $"hfs-{phrase.hash:x16}.wav");
                phrase.AddCacheFile(wavPath);
                string progressInfo = $"Track {trackNo + 1}: {this} " +
                    string.Join((string?)" ", (IEnumerable<string?>)phrase.phones.Select(p => p.phoneme));
                progress.Complete(0, progressInfo);

                // Return cached result if available
                if (File.Exists(wavPath)) {
                    try {
                        using var waveStream = Wave.OpenFile(wavPath);
                        result.samples = Wave.GetSamples(WaveExtensionMethods.ToSampleProvider(waveStream).ToMono(1, 0));
                    } catch (Exception e) {
                        Log.Warning(e, "Failed to read cached wav, re-rendering.");
                        result.samples = null;
                    }
                }

                if (result.samples == null) {
                    EnsureModelsLoaded();
                    RenderPhonesToCache(phrase, resamplerItems, cancellation);
                    if (cancellation.IsCancellationRequested) {
                        return result;
                    }

                    var wavtool = new SharpWavtool(true);
                    result.samples = wavtool.Concatenate(resamplerItems, string.Empty, cancellation);

                    if (result.samples != null) {
                        // Ease in/out to avoid clicks
                        int easeLen = Math.Min((int)result.samples.Length, 512);
                        for (int i = 0; i < easeLen; i++) {
                            result.samples[i] *= (float)i / easeLen;
                        }
                        for (int i = 0; i < easeLen; i++) {
                            int idx = result.samples.Length - easeLen + i;
                            result.samples[idx] *= (float)(easeLen - i) / easeLen;
                        }

                        // Cache to disk
                        try {
                            var source = new WaveSource(0, 0, 0, 1);
                            source.SetSamples(result.samples);
                            WaveFileWriter.CreateWaveFile16(wavPath, WaveExtensionMethods.ToMono(new ExportAdapter(source), 1, 0));
                        } catch (Exception e) {
                            Log.Warning(e, "Failed to cache rendered wav.");
                        }
                    }
                }

                progress.Complete(phrase.phones.Length, progressInfo);
                if (result.samples != null) {
                    OpenUtau.Core.Render.Renderers.ApplyDynamics(phrase, result);
                }
                return result;
            });
            return task;
        }

        void EnsureModelsLoaded() {
            if (vocoderSession != null) return; // Fast path without lock
            lock (modelsLock) {
                if (vocoderSession != null) return; // Double-check inside lock

                string basePath = PackageManager.Inst.GetInstalledPath(VocoderPkg) ?? string.Empty;
                if (string.IsNullOrEmpty(basePath)) {
                    throw new MessageCustomizableException(
                        $"Error loading package \"{VocoderPkg}\"",
                        $"<translate:packages.errors.missing>",
                        new Exception($"HifiSampler package \"{VocoderPkg}\" not installed."),
                        true,
                        new[] { VocoderPkg });
                }

                string configPath = Path.Combine(basePath, "vocoder.yaml");
                string modelPath = Path.Combine(basePath, "hifigan.onnx");
                if (File.Exists(configPath)) {
                    var config = Yaml.DefaultDeserializer.Deserialize<OpenUtau.Core.DiffSinger.DsVocoderConfig>(
                        File.ReadAllText(configPath, System.Text.Encoding.UTF8));
                    if (config != null && !string.IsNullOrEmpty(config.model)) {
                        modelPath = Path.Combine(basePath, config.model);
                    }
                }

                string vocoderPath = modelPath;
                if (!File.Exists(vocoderPath)) {
                    throw new FileNotFoundException($"HifiSampler vocoder model not found at {vocoderPath}");
                }
                byte[] vocoderBytes = File.ReadAllBytes(vocoderPath);

                string hnsepPath = Path.Combine(basePath, "hnsep.onnx");
                if (File.Exists(hnsepPath)) {
                    byte[] hnsepBytes = File.ReadAllBytes(hnsepPath);
                    hnsepSession = Onnx.getInferenceSession(hnsepBytes, OnnxRunnerChoice.Default);
                }

                // Assign vocoderSession last so the fast-path null check is only true when fully initialized
                vocoderSession = Onnx.getInferenceSession(vocoderBytes, OnnxRunnerChoice.Default);
            }
        }

        void RenderPhonesToCache(RenderPhrase phrase, List<ResamplerItem> items, CancellationTokenSource cancellation) {
            foreach (var item in items) {
                item.outputFile = GetPhoneCachePath(item);
                phrase.AddCacheFile(item.outputFile);
            }

            var errors = new ConcurrentQueue<Exception>();
            Parallel.For((int)0, items.Count, new ParallelOptions {
                MaxDegreeOfParallelism = GetPhoneParallelism(),
            }, (int i) => {
                if (cancellation.IsCancellationRequested) {
                    return;
                }
                var item = items[i];
                if (item.phone.direct) {
                    return;
                }

                if (File.Exists(item.outputFile)) {
                    return;
                }

                try {
                    float[] phoneSamples = RenderPhone(phrase, item);
                    if (phoneSamples == null || phoneSamples.Length == 0 || !HasNonZeroSamples(phoneSamples)) {
                        errors.Enqueue(BuildRenderFailure(item));
                        return;
                    }

                    var source = new WaveSource(0, 0, 0, 1);
                    source.SetSamples(phoneSamples);
                    lock (OpenUtau.Core.Render.Renderers.GetCacheLock(item.outputFile)) {
                        if (!File.Exists(item.outputFile)) {
                            WaveFileWriter.CreateWaveFile16(item.outputFile, WaveExtensionMethods.ToMono(new ExportAdapter(source), 1, 0));
                        }
                    }
                } catch (Exception e) {
                    errors.Enqueue(BuildRenderFailure(item, e));
                }
            });

            if (cancellation.IsCancellationRequested) {
                return;
            }
            if (!errors.IsEmpty) {
                throw new AggregateException(errors);
            }
        }

        float[] RenderPhone(RenderPhrase phrase, ResamplerItem item) {
            Stopwatch stopwatch = null;
            if (Preferences.Default.ResamplerLogging) {
                stopwatch = Stopwatch.StartNew();
            }

            float[] sourceAudio = GetSourceAudio(item.phone.oto.File);
            if (sourceAudio == null || sourceAudio.Length == 0) {
                return null;
            }

            double sourceStart = Math.Max(0, item.offset / 1000.0);
            double sourceEnd = item.cutoff < 0
                ? sourceStart - item.cutoff / 1000.0
                : sourceAudio.Length / (double)HifiSamplerDsp.SampleRate - item.cutoff / 1000.0;
            int sliceStart = Math.Clamp((int)Math.Round(sourceStart * HifiSamplerDsp.SampleRate), 0, sourceAudio.Length);
            int sliceEnd = Math.Clamp((int)Math.Round(sourceEnd * HifiSamplerDsp.SampleRate), sliceStart, sourceAudio.Length);
            if (sliceEnd <= sliceStart) {
                return null;
            }

            double phoneStartMs = item.phone.positionMs - item.phone.leadingMs;
            double phoneEndMs = phoneStartMs + item.durRequired;

            // Parse flags
            float genderShift = GetFlagValue(item.phone.flags, "g", 0) / 100f
                + SampleCurveAverage(phrase, phrase.gender, phoneStartMs, phoneEndMs, value => value * 12f / 100f);
            float breath = GetFlagValue(item.phone.flags, "Hb", 100f);
            float voicing = GetFlagValue(item.phone.flags, "Hv", 100f);
            float tension = GetFlagValue(item.phone.flags, "Ht", 0)
                + SampleCurveAverage(phrase, phrase.tension, phoneStartMs, phoneEndMs);
            float growlStrength = GetFlagValue(item.phone.flags, "HG", 0);
            float aFlag = GetFlagValue(item.phone.flags, "A", 0);
            float toneShift = GetFlagValue(item.phone.flags, "t", 0) * 0.01f;
            var loudnessFlag = Enumerable.FirstOrDefault<Tuple<string, int?, string>>(item.phone.flags, f => f.Item3 == "P" || f.Item1 == "P");
            bool useDefaultLoudnessNorm = loudnessFlag == null;
            float loudnessStrength = loudnessFlag != null && loudnessFlag.Item2.HasValue
                ? loudnessFlag.Item2.Value
                : 100f;

            bool needsHnsep = MathF.Abs(tension) > 0.01f || MathF.Abs(breath - voicing) > 0.01f;
            bool canUseHnsep = needsHnsep && hnsepSession != null;
            string featureKey = BuildFeatureCacheKey(item.phone.oto.File, sliceStart, sliceEnd, genderShift, breath, voicing, tension, canUseHnsep);

            float[,] melOrigin;
            float featureScale;
            if (!TryGetCachedFeatures(featureKey, out melOrigin, out featureScale)) {
                float[] audio = new float[sliceEnd - sliceStart];
                Array.Copy(sourceAudio, sliceStart, audio, 0, audio.Length);
                featureScale = 1f;

                if (canUseHnsep) {
                    float[] harmonic = ApplyHnsep(audio, false, true);
                    float[] noise = new float[audio.Length];
                    int len = Math.Min(audio.Length, harmonic.Length);
                    for (int i = 0; i < len; i++) {
                        noise[i] = audio[i] - harmonic[i];
                    }
                    if (MathF.Abs(voicing - 100f) > 0.01f) {
                        float voicedScale = voicing / 100f;
                        for (int i = 0; i < harmonic.Length; i++) {
                            harmonic[i] *= voicedScale;
                        }
                    }
                    if (MathF.Abs(tension) > 0.01f) {
                        harmonic = HifiSamplerDsp.ApplyTensionFilter(harmonic, tension);
                    }
                    if (MathF.Abs(breath - 100f) > 0.01f) {
                        float breathScale = breath / 100f;
                        for (int i = 0; i < noise.Length; i++) {
                            noise[i] *= breathScale;
                        }
                    }
                    float[] mixed = new float[Math.Max(harmonic.Length, noise.Length)];
                    for (int i = 0; i < mixed.Length; i++) {
                        float harmonicSample = i < harmonic.Length ? harmonic[i] : 0;
                        float noiseSample = i < noise.Length ? noise[i] : 0;
                        mixed[i] = harmonicSample + noiseSample;
                    }
                    audio = mixed;
                } else {
                    if (MathF.Abs(tension) > 0.01f) {
                        audio = HifiSamplerDsp.ApplyTensionFilter(audio, tension);
                    }
                    if (MathF.Abs(breath - voicing) < 0.01f && MathF.Abs(breath - 100f) > 0.01f) {
                        float scale = breath / 100f;
                        for (int i = 0; i < audio.Length; i++) {
                            audio[i] *= scale;
                        }
                    }
                }

                float maxAbs = 0;
                for (int i = 0; i < audio.Length; i++) {
                    maxAbs = MathF.Max(maxAbs, MathF.Abs(audio[i]));
                }
                if (maxAbs >= 0.5f && maxAbs > 1e-8f) {
                    featureScale = 0.5f / maxAbs;
                    for (int i = 0; i < audio.Length; i++) {
                        audio[i] *= featureScale;
                    }
                }

                melOrigin = HifiSamplerDsp.ComputeMelSpectrogram(audio, genderShift);
                StoreCachedFeatures(featureKey, melOrigin, featureScale);
            }

            int originFrames = melOrigin.GetLength(1);
            if (originFrames <= 0) return null;

            double thopOrigin = (double)HifiSamplerDsp.OriginHopSize / HifiSamplerDsp.SampleRate;
            double thop = (double)HifiSamplerDsp.HopSize / HifiSamplerDsp.SampleRate;
            double[] tAreaOrigin = new double[originFrames];
            for (int i = 0; i < originFrames; i++) {
                tAreaOrigin[i] = i * thopOrigin + thopOrigin / 2;
            }
            double totalTime = tAreaOrigin[^1] + thopOrigin / 2;
            if (totalTime <= 0) {
                return null;
            }

            double vel = Math.Pow(2.0, 1.0 - item.velocity / 100.0);
            double start = 0.0;
            double end = totalTime;
            double consonant = Math.Clamp(item.consonant / 1000.0, 0, Math.Max(0, end - thopOrigin));
            double lengthReq = item.durRequired / 1000.0;
            double stretchLength = Math.Max(end - consonant, thopOrigin);
            if (lengthReq <= 0) {
                return null;
            }

            bool forceLoop = Enumerable.Any<Tuple<string, int?, string>>(item.phone.flags, f => f.Item3 == "He" || f.Item1 == "He");
            bool shouldLoop = forceLoop || (LoopMode && lengthReq > stretchLength + thopOrigin);
            if (shouldLoop) {
                int conFrame = Math.Clamp((int)Math.Floor((consonant + thopOrigin / 2) / thopOrigin), 0, melOrigin.GetLength(1) - 1);
                int endFrame = Math.Clamp((int)Math.Floor((end + thopOrigin / 2) / thopOrigin), conFrame + 1, melOrigin.GetLength(1));
                float[,] melLoop = SliceMel(melOrigin, conFrame, endFrame);
                int padLoopFrames = (int)Math.Floor(lengthReq / thopOrigin) + 1;
                float[,] paddedLoop = PadMelRightReflect(melLoop, padLoopFrames);
                melOrigin = ConcatMel(SliceMel(melOrigin, 0, conFrame), paddedLoop);
                originFrames = melOrigin.GetLength(1);
                tAreaOrigin = new double[originFrames];
                for (int i = 0; i < originFrames; i++) {
                    tAreaOrigin[i] = i * thopOrigin + thopOrigin / 2;
                }
                totalTime = tAreaOrigin[^1] + thopOrigin / 2;
                stretchLength = padLoopFrames * thopOrigin;
            }

            double scalingRatio = stretchLength < lengthReq ? lengthReq / stretchLength : 1.0;
            int stretchedFrames = (int)Math.Floor((consonant * vel + (totalTime - consonant) * scalingRatio) / thop) + 1;
            if (stretchedFrames <= 0) {
                return null;
            }

            int startLeftFrames = (int)Math.Floor((start * vel + thop / 2) / thop);
            int cutLeftFrames = startLeftFrames > FillFrames ? startLeftFrames - FillFrames : 0;
            int endRightFrames = stretchedFrames - (int)Math.Floor((lengthReq + consonant * vel + thop / 2) / thop);
            int cutRightFrames = endRightFrames > FillFrames ? endRightFrames - FillFrames : 0;
            int keptStartFrame = cutLeftFrames;
            int keptEndFrame = stretchedFrames - cutRightFrames;
            if (keptEndFrame <= keptStartFrame) {
                return null;
            }

            double[] stretchedTimes = new double[keptEndFrame - keptStartFrame];
            for (int frame = keptStartFrame; frame < keptEndFrame; frame++) {
                double time = frame * thop + thop / 2;
                double stretchedTime = time < vel * consonant
                    ? time / vel
                    : consonant + (time - vel * consonant) / scalingRatio;
                stretchedTimes[frame - keptStartFrame] = Math.Clamp(stretchedTime, 0, tAreaOrigin[^1]);
            }

            double newStart = start * vel - cutLeftFrames * thop;
            double newEnd = (lengthReq + consonant * vel) - cutLeftFrames * thop;
            if (newEnd <= newStart) {
                return null;
            }

            float[,] melRender = HifiSamplerDsp.InterpolateMel(melOrigin, tAreaOrigin, stretchedTimes);
            int vocoderFrames = melRender.GetLength(1);
            if (vocoderFrames <= 0) {
                return null;
            }

            float[] pitchMidi = BuildRenderedPitchMidi(item, newStart, vocoderFrames, thop, toneShift);
            float[] f0 = new float[vocoderFrames];
            for (int i = 0; i < vocoderFrames; i++) {
                f0[i] = HifiSamplerDsp.MidiToHz(pitchMidi[i]);
            }

            // Create ONNX tensors
            float[] melFlat = new float[vocoderFrames * HifiSamplerDsp.NMels];
            float[] f0Flat = new float[vocoderFrames];
            for (int t = 0; t < vocoderFrames; t++) {
                f0Flat[t] = f0[t];
                for (int m = 0; m < HifiSamplerDsp.NMels; m++) {
                    melFlat[t * HifiSamplerDsp.NMels + m] = melRender[m, t];
                }
            }

            var melTensor = new DenseTensor<float>(melFlat, new[] { 1, vocoderFrames, HifiSamplerDsp.NMels });
            var f0Tensor = new DenseTensor<float>(f0Flat, new[] { 1, vocoderFrames });
            var inputs = new List<NamedOnnxValue> {
                NamedOnnxValue.CreateFromTensor("mel", melTensor),
                NamedOnnxValue.CreateFromTensor("f0", f0Tensor),
            };

            float[] render;
            using (var results = vocoderSession.Run(inputs)) {
                var audioOut = results.First().AsTensor<float>();
                float[] vocoderOutput = audioOut.ToArray();
                int renderStart = Math.Clamp((int)Math.Round(newStart * HifiSamplerDsp.SampleRate), 0, vocoderOutput.Length);
                int renderEnd = Math.Clamp((int)Math.Round(newEnd * HifiSamplerDsp.SampleRate), renderStart, vocoderOutput.Length);
                render = new float[renderEnd - renderStart];
                Array.Copy(vocoderOutput, renderStart, render, 0, render.Length);
            }

            // Post-processing: amplitude modulation (A flag)
            if (MathF.Abs(aFlag) > 0.01f) {
                double[] tFrames = new double[vocoderFrames];
                for (int i = 0; i < vocoderFrames; i++) {
                    tFrames[i] = i * thop;
                }
                HifiSamplerDsp.ApplyAmplitudeModulation(render, pitchMidi, tFrames,
                    newStart, newEnd, (int)aFlag);
            }

            if (MathF.Abs(featureScale - 1f) > 1e-6f) {
                for (int i = 0; i < render.Length; i++) {
                    render[i] /= featureScale;
                }
            }

            // Post-processing: growl (HG flag)
            if (growlStrength > 0) {
                render = HifiSamplerDsp.ApplyGrowl(render, growlStrength / 100f);
            }

            // Upstream hifisampler normalizes by default and lets P adjust strength.
            if (useDefaultLoudnessNorm || MathF.Abs(loudnessStrength) > 0.01f) {
                HifiSamplerDsp.LoudnessNormalize(render, -16f, loudnessStrength);
            }

            float renderPeak = 0;
            for (int i = 0; i < render.Length; i++) {
                renderPeak = MathF.Max(renderPeak, MathF.Abs(render[i]));
            }
            if (renderPeak > 1f) {
                for (int i = 0; i < render.Length; i++) {
                    render[i] /= renderPeak;
                }
            }

            // Apply volume
            float vol = item.phone.volume;
            if (MathF.Abs(vol - 1f) > 0.001f) {
                for (int i = 0; i < render.Length; i++) render[i] *= vol;
            }

            if (stopwatch != null) {
                stopwatch.Stop();
                Log.Information<string, long>("HifiSampler rendered {Phoneme} in {ElapsedMs} ms", item.phone.phoneme, stopwatch.ElapsedMilliseconds);
            }

            return render;
        }

        static int GetPhoneParallelism() {
            return Math.Max(1, Math.Min((int)Preferences.Default.NumRenderThreads, 2));
        }

        static string GetPhoneCachePath(ResamplerItem item) {
            return Path.Join((string?)PathManager.Inst.CachePath, $"hfs-phone-{item.hash:x16}.wav");
        }

        static bool HasNonZeroSamples(float[] samples) {
            for (int i = 0; i < samples.Length; i++) {
                if (MathF.Abs(samples[i]) > 1e-6f) {
                    return true;
                }
            }
            return false;
        }

        static InvalidDataException BuildRenderFailure(ResamplerItem item, Exception inner = null) {
            item.phrase.timeAxis.TickPosToBarBeat(item.phrase.position + item.phone.position, out int bar, out int beat, out int tick);
            string message = $"HifiSampler failed to render \"{item.phone.phoneme}\" at {bar}:{beat}.{tick:000}";
            return inner == null ? new InvalidDataException(message) : new InvalidDataException(message, inner);
        }

        static float[] GetSourceAudio(string path) {
            lock (sourceCacheLock) {
                if (sourceAudioCache.TryGetValue(path, out var cached)) {
                    return cached;
                }
            }

            float[] samples;
            using (var waveStream = Wave.OpenFile(path)) {
                if (waveStream == null) {
                    return null;
                }
                samples = Wave.GetSamples(WaveExtensionMethods.ToSampleProvider(waveStream).ToMono(1, 0));
            }

            lock (sourceCacheLock) {
                if (sourceAudioCache.TryGetValue(path, out var cached)) {
                    return cached;
                }
                sourceAudioCache[path] = samples;
                sourceAudioCacheOrder.Enqueue(path);
                while (sourceAudioCache.Count > MaxSourceAudioCacheEntries) {
                    string oldest = sourceAudioCacheOrder.Dequeue();
                    sourceAudioCache.Remove(oldest);
                }
                return samples;
            }
        }

        static string BuildFeatureCacheKey(string path, int sliceStart, int sliceEnd, float genderShift, float breath, float voicing, float tension, bool canUseHnsep) {
            int genderKey = (int)Math.Round(genderShift * 1000f);
            int breathKey = (int)Math.Round(breath * 100f);
            int voicingKey = (int)Math.Round(voicing * 100f);
            int tensionKey = (int)Math.Round(tension * 100f);
            return $"{path}|{sliceStart}|{sliceEnd}|{genderKey}|{breathKey}|{voicingKey}|{tensionKey}|{(canUseHnsep ? 1 : 0)}";
        }

        static bool TryGetCachedFeatures(string key, out float[,] melOrigin, out float featureScale) {
            lock (featureCacheLock) {
                if (featureCache.TryGetValue(key, out var cached)) {
                    melOrigin = cached.MelOrigin;
                    featureScale = cached.FeatureScale;
                    return true;
                }
            }
            melOrigin = null;
            featureScale = 1f;
            return false;
        }

        static void StoreCachedFeatures(string key, float[,] melOrigin, float featureScale) {
            lock (featureCacheLock) {
                if (featureCache.ContainsKey(key)) {
                    return;
                }
                featureCache[key] = new CachedFeatures {
                    MelOrigin = melOrigin,
                    FeatureScale = featureScale,
                };
                featureCacheOrder.Enqueue(key);
                while (featureCache.Count > MaxFeatureCacheEntries) {
                    string oldest = featureCacheOrder.Dequeue();
                    featureCache.Remove(oldest);
                }
            }
        }

        float[] BuildRenderedPitchMidi(ResamplerItem item, double newStart, int frameCount, double frameHop, float toneShift) {
            int pitchCount = Math.Max((int)item.pitches.Length, 1);
            double secondsPerPitch = 60.0 / (item.tempo * 96.0);
            double[] tPitch = new double[pitchCount];
            float[] pitchMidi = new float[pitchCount];
            for (int i = 0; i < pitchCount; i++) {
                tPitch[i] = secondsPerPitch * i + newStart;
                float cents = i < item.pitches.Length ? item.pitches[i] : 0;
                pitchMidi[i] = item.tone + toneShift + cents * 0.01f;
            }

            float[] result = new float[frameCount];
            for (int i = 0; i < frameCount; i++) {
                double t = Math.Clamp(i * frameHop, newStart, tPitch[^1]);
                if (t <= tPitch[0]) {
                    result[i] = pitchMidi[0];
                    continue;
                }
                if (t >= tPitch[^1]) {
                    result[i] = pitchMidi[^1];
                    continue;
                }
                int lo = 0;
                int hi = tPitch.Length - 1;
                while (hi - lo > 1) {
                    int mid = (lo + hi) >> 1;
                    if (tPitch[mid] <= t) {
                        lo = mid;
                    } else {
                        hi = mid;
                    }
                }
                result[i] = (float)MusicMath.Linear(tPitch[lo], tPitch[hi], pitchMidi[lo], pitchMidi[hi], t);
            }
            return result;
        }

        float[] ApplyHnsep(float[] audio, bool useBreath, bool useVoice) {
            var (specRe, specIm, nFrames) = HifiSamplerDsp.Stft(audio);
            if (nFrames == 0) return audio;

            float[] packed = HifiSamplerDsp.PackStftForHnsep(specRe, specIm, out int useBins);
            var inputTensor = new DenseTensor<float>(packed, new[] { 1, 2, useBins, nFrames });
            var inputs = new List<NamedOnnxValue> {
                NamedOnnxValue.CreateFromTensor("input", inputTensor),
            };

            float[] maskData;
            using (var results = hnsepSession.Run(inputs)) {
                maskData = results.First().AsTensor<float>().ToArray();
            }

            float[] separated = HifiSamplerDsp.ApplyHnsepMask(maskData, useBins, nFrames,
                specRe, specIm, audio.Length);

            // HN-SEP outputs harmonic component; if useBreath, subtract to get noise
            if (useBreath && !useVoice) {
                float[] noise = new float[audio.Length];
                int len = Math.Min(audio.Length, separated.Length);
                for (int i = 0; i < len; i++) noise[i] = audio[i] - separated[i];
                return noise;
            }
            return separated; // harmonic/voiced component
        }

        float[] BuildF0(RenderPhrase phrase, ResamplerItem item, int vocoderFrames) {
            float[] f0 = new float[vocoderFrames];
            const int pitchInterval = 5;
            double phraseStartMs = phrase.positionMs - phrase.leadingMs;
            double phoneStartMs = item.phone.positionMs - item.phone.leadingMs;
            double phoneEndMs = phoneStartMs + item.durRequired;

            for (int i = 0; i < vocoderFrames; i++) {
                double frameMs = phoneStartMs + (phoneEndMs - phoneStartMs) * i / vocoderFrames;
                int ticks = phrase.timeAxis.MsPosToTickPos(frameMs) - (phrase.position - phrase.leading);
                int pitchIdx = Math.Clamp(ticks / pitchInterval, 0, phrase.pitches.Length - 1);
                float cents = phrase.pitches[pitchIdx];
                f0[i] = HifiSamplerDsp.MidiToHz(cents * 0.01f);
            }
            return f0;
        }

        float[] BuildPitchMidi(RenderPhrase phrase, ResamplerItem item, int vocoderFrames) {
            float[] midi = new float[vocoderFrames];
            const int pitchInterval = 5;
            double phoneStartMs = item.phone.positionMs - item.phone.leadingMs;
            double phoneEndMs = phoneStartMs + item.durRequired;

            for (int i = 0; i < vocoderFrames; i++) {
                double frameMs = phoneStartMs + (phoneEndMs - phoneStartMs) * i / vocoderFrames;
                int ticks = phrase.timeAxis.MsPosToTickPos(frameMs) - (phrase.position - phrase.leading);
                int pitchIdx = Math.Clamp(ticks / pitchInterval, 0, phrase.pitches.Length - 1);
                midi[i] = phrase.pitches[pitchIdx] * 0.01f;
            }
            return midi;
        }

        float SampleCurveAverage(RenderPhrase phrase, float[] curve, double startMs, double endMs) {
            return SampleCurveAverage(phrase, curve, startMs, endMs, value => value);
        }

        float SampleCurveAverage(RenderPhrase phrase, float[] curve, double startMs, double endMs, Func<float, float> convert) {
            if (curve == null || curve.Length == 0) {
                return 0;
            }
            if (endMs < startMs) {
                (startMs, endMs) = (endMs, startMs);
            }
            const int curveInterval = 5;
            int startTick = phrase.timeAxis.MsPosToTickPos(startMs) - (phrase.position - phrase.leading);
            int endTick = phrase.timeAxis.MsPosToTickPos(endMs) - (phrase.position - phrase.leading);
            int startIndex = Math.Clamp((int)Math.Floor((double)startTick / curveInterval), 0, curve.Length - 1);
            int endIndex = Math.Clamp((int)Math.Ceiling((double)endTick / curveInterval), 0, curve.Length - 1);

            float sum = 0;
            int count = 0;
            for (int i = startIndex; i <= endIndex; i++) {
                sum += convert(curve[i]);
                count++;
            }
            return count > 0 ? sum / count : convert(curve[startIndex]);
        }

        static float GetFlagValue(Tuple<string, int?, string>[] flags, string abbr, float defaultValue) {
            var flag = flags.FirstOrDefault(f => f.Item3 == abbr || f.Item1 == abbr);
            if (flag != null && flag.Item2.HasValue) return flag.Item2.Value;
            return defaultValue;
        }

        static float[,] SliceMel(float[,] mel, int startFrame, int endFrame) {
            startFrame = Math.Clamp(startFrame, 0, mel.GetLength(1));
            endFrame = Math.Clamp(endFrame, startFrame, mel.GetLength(1));
            int frameCount = endFrame - startFrame;
            float[,] result = new float[mel.GetLength(0), frameCount];
            for (int m = 0; m < mel.GetLength(0); m++) {
                for (int t = 0; t < frameCount; t++) {
                    result[m, t] = mel[m, startFrame + t];
                }
            }
            return result;
        }

        static float[,] PadMelRightReflect(float[,] mel, int padFrames) {
            int melBins = mel.GetLength(0);
            int frameCount = mel.GetLength(1);
            float[,] result = new float[melBins, frameCount + padFrames];
            for (int m = 0; m < melBins; m++) {
                for (int t = 0; t < frameCount; t++) {
                    result[m, t] = mel[m, t];
                }
                for (int t = 0; t < padFrames; t++) {
                    int src;
                    if (frameCount <= 1) {
                        src = 0;
                    } else {
                        int period = 2 * frameCount - 2;
                        int reflected = t % period;
                        src = reflected < frameCount - 1
                            ? frameCount - 2 - reflected
                            : reflected - frameCount + 2;
                    }
                    result[m, frameCount + t] = mel[m, src];
                }
            }
            return result;
        }

        static float[,] ConcatMel(float[,] left, float[,] right) {
            int melBins = left.GetLength(0);
            int leftFrames = left.GetLength(1);
            int rightFrames = right.GetLength(1);
            float[,] result = new float[melBins, leftFrames + rightFrames];
            for (int m = 0; m < melBins; m++) {
                for (int t = 0; t < leftFrames; t++) {
                    result[m, t] = left[m, t];
                }
                for (int t = 0; t < rightFrames; t++) {
                    result[m, leftFrames + t] = right[m, t];
                }
            }
            return result;
        }

        public RenderPitchResult LoadRenderedPitch(RenderPhrase phrase) => null;

        public UExpressionDescriptor[] GetSuggestedExpressions(USinger singer, URenderSettings renderSettings) {
            return new UExpressionDescriptor[] { };
        }

        public override string ToString() => "HIFISAMPLER";
    }
}
