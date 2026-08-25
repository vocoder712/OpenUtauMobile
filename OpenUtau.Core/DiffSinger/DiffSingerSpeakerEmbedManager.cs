using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Microsoft.ML.OnnxRuntime.Tensors;
using NumSharp;
using Serilog;

using OpenUtau.Core.Render;

namespace OpenUtau.Core.DiffSinger
{
    public class DiffSingerSpeakerEmbedManager
    {
        DsConfig dsConfig;
        string rootPath;
        public NDArray speakerEmbeds = null;
        const string VoiceColorHeader = DiffSingerUtils.VoiceColorHeader;

        public DiffSingerSpeakerEmbedManager(DsConfig dsConfig, string rootPath) {
            this.dsConfig = dsConfig;
            this.rootPath = rootPath;
        }

        public NDArray loadSpeakerEmbed(string speaker) {
            string path = Path.Join(rootPath, speaker + ".emb");
            if(File.Exists(path)) {
                using var reader = new BinaryReader(File.OpenRead(path));
                return np.array<float>(Enumerable.Range(0, dsConfig.hiddenSize)
                    .Select(i => reader.ReadSingle()));
            } else {
                throw new Exception($"Speaker embed file {path} not found");
            }
        }

        public NDArray getSpeakerEmbeds() {
            if(speakerEmbeds == null) {
                if(dsConfig.speakers == null) {
                    return null;
                } else {
                    var embeds = np.zeros<float>(dsConfig.hiddenSize, dsConfig.speakers.Count);
                    foreach(var spkId in Enumerable.Range(0, dsConfig.speakers.Count)) {
                        embeds[":", spkId] = loadSpeakerEmbed(dsConfig.speakers[spkId]);
                    }
                    speakerEmbeds = embeds;
                }
            }
            return speakerEmbeds;
        }

        public bool IsVoiceColorCurve(string abbr, out int subBankId) {
            subBankId = 0;
            if (abbr.StartsWith(VoiceColorHeader) && int.TryParse(abbr.Substring(2), out subBankId)) {
                subBankId -= 1;
                return true;
            } else {
                return false;
            }
        }

        static readonly HashSet<string> warnedMissingSpeakerSuffixes = new();

        public int getSpeakerIndexBySuffix(string suffix) {
            var speakerIndex = dsConfig.speakers.IndexOf(suffix);
            if (speakerIndex >= 0) {
                return speakerIndex;
            }
            speakerIndex = dsConfig.speakers.FindIndex(s => {
                var spSegs = s.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var sfSegs = suffix.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return sfSegs.Length <= spSegs.Length
                    && spSegs[^sfSegs.Length..].SequenceEqual(sfSegs);
            });
            if (speakerIndex >= 0) {
                return speakerIndex;
            }
            if (dsConfig.speakers == null || dsConfig.speakers.Count == 0) {
                throw new InvalidOperationException(
                    "Subbanks are defined in character.yaml but \"speakers\" is empty in dsconfig.yaml.");
            }
            var fallback = dsConfig.speakers[0];
            var warnKey = $"{rootPath}|{suffix}|{fallback}";
            lock (warnedMissingSpeakerSuffixes) {
                if (warnedMissingSpeakerSuffixes.Add(warnKey)) {
                    Log.Warning(
                        "Speaker suffix \"{Suffix}\" not found in dsConfig.speakers ({Candidates}). Falling back to \"{Fallback}\".",
                        suffix,
                        string.Join(", ", dsConfig.speakers),
                        fallback);
                }
            }
            return 0;
        }

        //used by phonemizer (duration model)
        public Tensor<float> PhraseSpeakerEmbedByPhone(string[] speakerByPhone) {
            var hiddenSize = dsConfig.hiddenSize;
            var speakerEmbeds = getSpeakerEmbeds();
            var totalPhones = speakerByPhone.Length;
            var result = new float[totalPhones * hiddenSize];

            for (int phoneId = 0; phoneId < totalPhones; phoneId++) {
                var spkId = getSpeakerIndexBySuffix(speakerByPhone[phoneId]);
                var embed = speakerEmbeds[":", spkId].ToArray<float>();
                var dest = result.AsSpan(phoneId * hiddenSize, hiddenSize);
                embed.AsSpan().CopyTo(dest);
            }

            return new DenseTensor<float>(result, new int[] { totalPhones, hiddenSize })
                .Reshape(new int[] { 1, totalPhones, hiddenSize });
        }

        //used by variance, pitch and acoustic
        public Tensor<float> PhraseSpeakerEmbedByFrame(RenderPhrase phrase, IList<int> durations, float frameMs, int totalFrames, int headFrames, int tailFrames) {
            var singer = phrase.singer;
            var hiddenSize = dsConfig.hiddenSize;
            var speakerEmbeds = getSpeakerEmbeds();
            // Per-frame CLR / phoneme suffix is always weight 1.0 ("100%").
            // Voice-color curves add on top; then weights are normalized to a convex mix.
            // Example: CLR=A and cl_B=100% → A:B = 1:1 (not pure B).
            var headDefaultSpk = getSpeakerIndexBySuffix(phrase.phones[0].suffix);
            var tailDefaultSpk = getSpeakerIndexBySuffix(phrase.phones[^1].suffix);
            var defaultSpkByFrame = Enumerable.Repeat(headDefaultSpk, headFrames).ToList();
            defaultSpkByFrame.AddRange(Enumerable.Range(0, phrase.phones.Length)
                .SelectMany(phIndex => Enumerable.Repeat(
                    getSpeakerIndexBySuffix(phrase.phones[phIndex].suffix),
                    durations[phIndex + 1])));
            defaultSpkByFrame.AddRange(Enumerable.Repeat(tailDefaultSpk, tailFrames));
            //get speaker curves
            NDArray spkCurves = np.zeros<float>(totalFrames, dsConfig.speakers.Count);
            foreach (var curve in phrase.curves) {
                if (IsVoiceColorCurve(curve.Item1, out int subBankId) && subBankId < singer.Subbanks.Count) {
                    var spkId = getSpeakerIndexBySuffix(singer.Subbanks[subBankId].Suffix);
                    spkCurves[":", spkId] += DiffSingerUtils.SampleCurve(phrase, curve.Item2, 0, 
                        frameMs, totalFrames, headFrames, tailFrames, x => x * 0.01f)
                        .Select(f => (float)f).ToArray();
                }
            }

            int speakerCount = dsConfig.speakers.Count;
            var result = new float[totalFrames * hiddenSize];
            var weights = new float[speakerCount];
            for (int frameId = 0; frameId < totalFrames; frameId++) {
                Array.Clear(weights, 0, speakerCount);
                int clrSpkId = defaultSpkByFrame[frameId];
                weights[clrSpkId] = 1f;
                for (int spk = 0; spk < speakerCount; spk++) {
                    weights[spk] += (float)spkCurves[frameId, spk];
                }

                float weightSum = 0f;
                for (int spk = 0; spk < speakerCount; spk++) {
                    if (weights[spk] < 0f) {
                        weights[spk] = 0f;
                    }
                    weightSum += weights[spk];
                }
                if (weightSum < 1e-8f) {
                    weights[clrSpkId] = 1f;
                    weightSum = 1f;
                }

                var dest = result.AsSpan(frameId * hiddenSize, hiddenSize);
                dest.Clear();
                for (int spk = 0; spk < speakerCount; spk++) {
                    float w = weights[spk] / weightSum;
                    if (w < 1e-8f) {
                        continue;
                    }
                    var embed = speakerEmbeds[":", spk].ToArray<float>();
                    for (int j = 0; j < dest.Length; j++) {
                        dest[j] += w * embed[j];
                    }
                }
            }
            return new DenseTensor<float>(result, new int[] { totalFrames, hiddenSize })
                .Reshape(new int[] { 1, totalFrames, hiddenSize });
        }
    }
}
