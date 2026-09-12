using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Cross-lingual VCV phonemizer that converts Chinese pinyin to Japanese
    /// VCV (renzokuon / continuous-sound) aliases.
    ///
    /// Each pinyin syllable is split into weighted Japanese morae.  For each
    /// mora the phonemizer builds VCV-format candidates (VCV-kana → bare-kana
    /// → VCV-romaji → bare-romaji) and picks the first one that exists in the
    /// singer's OTO — the same strategy used by the built-in Japanese VCV
    /// phonemizer.  No up-front kana/romaji format detection is needed.
    /// </summary>
    [Phonemizer("Chinese to Japanese VCV Phonemizer", "ZH to JA VCV", language: "ZH")]
    public class ChineseToJapaneseVCVPhonemizer : BaseChineseToJapanesePhonemizer {

        public ChineseToJapaneseVCVPhonemizer() {
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

            // ── Look up mapping (first scheme only) ──────────────────
            WeightedOption[] scheme;
            if (!mapping.TryGetValue(lyric, out var schemes) || schemes.Length == 0) {
                // No mapping – pass through with VCV alias resolution
                string vcvPrefix = GetLastVowelOfNote(prevNeighbour) ?? "-";
                string alias = ResolveVcvAlias(lyric, note.tone, vcvPrefix);
                return MakeSimpleResult(alias);
            }

            scheme = schemes[0].Options;
            int totalRatio = scheme.Sum(o => o.Ratio);
            int totalDuration = notes.Sum(n => n.duration);
            if (totalDuration <= 0) totalDuration = 480;

            double bpm = timeAxis.GetBpmAtTick(note.position);
            double msPerTick = 60000.0 / (bpm * 480);
            int overlapTicks = (int)(OverlapMs / msPerTick);
            if (overlapTicks < 0) overlapTicks = 0;

            // ── VCV linking vowel ────────────────────────────────────
            string? linkVowel = GetLastVowelOfNote(prevNeighbour);

            // ── Build phonemes ───────────────────────────────────────
            var phonemes = new List<Phoneme>();
            int cumulativePos = 0;

            for (int i = 0; i < scheme.Length; i++) {
                var opt = scheme[i];
                int phonemeDuration = totalDuration * opt.Ratio / totalRatio;
                if (phonemeDuration <= 0) phonemeDuration = 1;

                // VCV prefix for this sub-phoneme:
                //   first  → linkVowel (from prev note) or "-"
                //   others → vowel of previous sub-phoneme
                string prefix;
                if (i == 0) {
                    prefix = linkVowel ?? "-";
                } else {
                    prefix = ExtractVowel(scheme[i - 1].Romaji);
                }

                // OTO-driven: tries VCV-kana → bare-kana → VCV-romaji → bare-romaji
                string alias = ResolveVcvAlias(opt.Romaji, note.tone, prefix);

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

        public override string ToString() => "[ZH to JA VCV] Chinese to Japanese VCV Phonemizer";
    }
}
