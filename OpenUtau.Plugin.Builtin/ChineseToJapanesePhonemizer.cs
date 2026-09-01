using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Cross-lingual phonemizer that converts Chinese pinyin lyrics to Japanese
    /// CV (standalone) aliases.
    ///
    /// Each pinyin syllable is split into weighted Japanese morae, then each
    /// mora is resolved against the singer's OTO: kana is tried first, then
    /// romaji — the OTO itself determines the voicebank's format.
    /// </summary>
    [Phonemizer("Chinese to Japanese Phonemizer", "ZH to JA", language: "ZH")]
    public class ChineseToJapanesePhonemizer : BaseChineseToJapanesePhonemizer {

        public ChineseToJapanesePhonemizer() {
            try {
                LoadMapping();
            } catch (Exception e) {
                Serilog.Log.Error(e, "Failed to load pinyin mapping");
                mapping = new Dictionary<string, WeightedScheme[]>();
            }
        }

        public override Result Process(Note[] notes, Note? prev, Note? next,
            Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {

            var note = notes[0];
            string lyric = note.lyric.Normalize();

            var specialResult = HandleSpecialLyric(lyric);
            if (specialResult != null)
                return MakeSimpleResult(specialResult);

            // Look up mapping → use first scheme
            if (!mapping.TryGetValue(lyric, out var schemes) || schemes.Length == 0) {
                var fallback = ResolveAlias(lyric, note.tone);
                return MakeSimpleResult(fallback);
            }

            var scheme = schemes[0].Options;
            int totalRatio = scheme.Sum(o => o.Ratio);
            int totalDuration = notes.Sum(n => n.duration);
            if (totalDuration <= 0) totalDuration = 480;

            double bpm = timeAxis.GetBpmAtTick(note.position);
            double msPerTick = 60000.0 / (bpm * 480);
            int overlapTicks = (int)(OverlapMs / msPerTick);
            if (overlapTicks < 0) overlapTicks = 0;

            var phonemes = new List<Phoneme>();
            int cumulativePos = 0;

            for (int i = 0; i < scheme.Length; i++) {
                var opt = scheme[i];
                int phonemeDuration = totalDuration * opt.Ratio / totalRatio;
                if (phonemeDuration <= 0) phonemeDuration = 1;

                // OTO-driven: tries kana first, falls back to romaji
                string alias = ResolveAlias(opt.Romaji, note.tone);

                int position = cumulativePos;
                if (i > 0) position -= overlapTicks;

                phonemes.Add(new Phoneme {
                    phoneme = alias,
                    position = position,
                });

                cumulativePos += phonemeDuration;
            }

            return new Result { phonemes = phonemes.ToArray() };
        }

        public override string ToString() => "[ZH to JA] Chinese to Japanese Phonemizer";
    }
}
