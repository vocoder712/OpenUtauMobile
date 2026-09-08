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
        readonly HashSet<string> warnedMissingSpeakerSuffixes = new();

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
                    if (dsConfig.speakers.Count == 0)
                    {
                        throw new InvalidOperationException("\"speakers\" is empty in dsconfig.yaml.");
                    }
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
            if (abbr.StartsWith(VoiceColorHeader) && int.TryParse(abbr.Substring(2), out subBankId)) {;
                subBankId -= 1;
                return true;
            } else {
                return false;
            }
        }

        public int getSpeakerIndexBySuffix(string suffix) {
            if (dsConfig.speakers == null || dsConfig.speakers.Count == 0)
            {
                throw new InvalidOperationException("\"speakers\" is missing or empty in dsconfig.yaml.");
            }
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
            lock (warnedMissingSpeakerSuffixes)
            {
                if (warnedMissingSpeakerSuffixes.Add(suffix))
                {
                    Log.Warning(
                        $"Speaker suffix \"{suffix}\" not found in dsConfig.speakers, falling back to first speaker. " +
                        $"Candidates: {string.Join(',', dsConfig.speakers)}.");
                }
            }
            return 0;
        }

        //used by phonemizer (duration model)
        public Tensor<float> PhraseSpeakerEmbedByPhone(string[] speakerByPhone){
            var hiddenSize = dsConfig.hiddenSize;
            var speakerEmbeds = getSpeakerEmbeds();
            var totalPhones = speakerByPhone.Length;
            float[][] embeddings = new float[dsConfig.speakers.Count][];
            for (int speakerId = 0; speakerId < embeddings.Length; speakerId++)
            {
                embeddings[speakerId] = speakerEmbeds[":", speakerId].ToArray<float>();
            }
            float[] result = new float[totalPhones * hiddenSize];
            for (int phoneId = 0; phoneId < totalPhones; phoneId++)
            {
                int speakerId = getSpeakerIndexBySuffix(speakerByPhone[phoneId]);
                Array.Copy(embeddings[speakerId], 0, result, phoneId * hiddenSize, hiddenSize);
            }
            var spkEmbedTensor = new DenseTensor<float>(result,
                new int[] { totalPhones, hiddenSize })
                .Reshape(new int[] { 1, totalPhones, hiddenSize });
            return spkEmbedTensor;
        }

        //used by variance, pitch and acoustic
        public Tensor<float> PhraseSpeakerEmbedByFrame(RenderPhrase phrase, IList<int> durations, float frameMs, int totalFrames, int headFrames, int tailFrames){
            var singer = phrase.singer;
            var hiddenSize = dsConfig.hiddenSize;
            var speakerEmbeds = getSpeakerEmbeds();
            //get default speaker for each padded segment
            var segments = DiffSingerUtils.PaddedSegments(phrase, frameMs, headFrames, tailFrames);
            var defaultSpkByFrame = new List<int>();
            var currentSpk = getSpeakerIndexBySuffix(phrase.phones[0].suffix);
            for (int i = 0; i < segments.Count; ++i) {
                if (segments[i].PhoneIndex >= 0) {
                    currentSpk = getSpeakerIndexBySuffix(phrase.phones[segments[i].PhoneIndex].suffix);
                }
                defaultSpkByFrame.AddRange(Enumerable.Repeat(currentSpk, durations[i]));
            }
            //get speaker curves
            NDArray spkCurves = np.zeros<float>(totalFrames, dsConfig.speakers.Count);
            foreach(var curve in phrase.curves) {
                if(IsVoiceColorCurve(curve.Item1,out int subBankId) && subBankId < singer.Subbanks.Count) {
                    var spkId = getSpeakerIndexBySuffix(singer.Subbanks[subBankId].Suffix);
                    spkCurves[":", spkId] += DiffSingerUtils.SampleCurve(phrase, curve.Item2, 0, 
                        frameMs, totalFrames, headFrames, tailFrames, x => x * 0.01f)
                        .Select(f => (float)f).ToArray();
                }
            }
            foreach(int frameId in Enumerable.Range(0,totalFrames)) {
                //standarization
                var spkSum = spkCurves[frameId, ":"].ToArray<float>().Sum();
                if (spkSum > 1) {
                    spkCurves[frameId, ":"] /= spkSum;
                } else {
                    spkCurves[frameId, defaultSpkByFrame[frameId]] += 1 - spkSum;
                }
            }
            float[][] embeddings = new float[dsConfig.speakers.Count][];
            for (int speakerId = 0; speakerId < embeddings.Length; speakerId++)
            {
                embeddings[speakerId] = speakerEmbeds[":", speakerId].ToArray<float>();
            }
            float[] result = new float[totalFrames * hiddenSize];
            for (int frameId = 0; frameId < totalFrames; frameId++)
            {
                float[] weights = spkCurves[frameId, ":"].ToArray<float>();
                for (int speakerId = 0; speakerId < embeddings.Length; speakerId++)
                {
                    for (int dimension = 0; dimension < hiddenSize; dimension++)
                    {
                        result[frameId * hiddenSize + dimension] +=
                            weights[speakerId] * embeddings[speakerId][dimension];
                    }
                }
            }
            var spkEmbedTensor = new DenseTensor<float>(result,
                new int[] { totalFrames, hiddenSize })
                .Reshape(new int[] { 1, totalFrames, hiddenSize });
            return spkEmbedTensor;
        }
    }
}
