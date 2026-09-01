using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core.G2p;
using OpenUtau.Core.Ustx;
using Serilog;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Cross-lingual phonemizer that converts English lyrics to Chinese pinyin
    /// using a two-dimensional consonant×vowel mapping:
    ///   1. CMUdict → ARPAbet phonemes
    ///   2. Parse ARPAbet into (onset, nucleus) syllable pairs
    ///   3. Consonant initial × vowel final → Chinese syllable
    ///   4. Validate against known Chinese syllables; fall back gracefully
    ///   5. Merge bare finals with preceding syllables
    ///
    /// Example: "hello" → [HH,AH, L,OW] → [ha, lou]
    /// </summary>
    [Phonemizer("English to Chinese Phonemizer", "EN to ZH", language: "EN")]
    public class EnglishToChinesePhonemizer : Phonemizer {

        // ── mapping tables ────────────────────────────────────────────

        private USinger? singer;

        /// <summary>ARPAbet consonant → Chinese initials (shengmu), ranked by preference.</summary>
        private Dictionary<string, string[]> consonantInitials = null!;

        /// <summary>ARPAbet vowel → Chinese finals (yunmu), ranked by preference.</summary>
        private Dictionary<string, string[]> vowelFinals = null!;

        /// <summary>Set of valid Chinese syllables for validation / fallback.</summary>
        private HashSet<string> validSyllables = null!;

        /// <summary>Cache: final part of a pinyin string.</summary>
        private Dictionary<string, string> pinyinFinalCache = new();

        private ArpabetG2p? arpabetG2p;

        // ── ARPAbet vowel set ─────────────────────────────────────────

        private static readonly HashSet<string> ArpabetVowelSet = new() {
            "AA","AE","AH","AO","AW","AY",
            "EH","ER","EY",
            "IH","IY",
            "OW","OY",
            "UH","UW"
        };

        // ── Chinese initial consonants (shengmu), longest-match first ─
        private static readonly string[] ChineseInitials = {
            "zh","ch","sh",
            "b","p","m","f","d","t","n","l",
            "g","k","h","j","q","x",
            "r","z","c","s",
            "y","w"
        };

        // ── ctor ─────────────────────────────────────────────────────

        public EnglishToChinesePhonemizer() {
            try {
                LoadMappingTables();
                arpabetG2p = new ArpabetG2p();
            } catch (Exception e) {
                Log.Error(e, "Failed to initialize English→Chinese phonemizer");
                consonantInitials = new Dictionary<string, string[]>();
                vowelFinals = new Dictionary<string, string[]>();
                validSyllables = new HashSet<string>();
            }
        }

        // ── mapping loader ───────────────────────────────────────────

        private void LoadMappingTables() {
            consonantInitials = new Dictionary<string, string[]>();
            vowelFinals       = new Dictionary<string, string[]>();
            validSyllables    = new HashSet<string>();

            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(
                "OpenUtau.Plugin.Builtin.Data.arpabet_to_pinyin_enhanced.txt");
            if (stream == null) {
                Log.Error("Embedded resource arpabet_to_pinyin_enhanced.txt not found");
                return;
            }
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) != null) {
                line = line.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line.StartsWith("C:") || line.StartsWith("V:")) {
                    // Format: C:ARPABET=initial1|initial2   or   V:ARPABET=final1|final2
                    int eq = line.IndexOf('=');
                    if (eq < 3) continue; // at least "C:X=" or "V:X="
                    string key  = line.Substring(2, eq - 2).Trim();  // between "C:" / "V:" and "="
                    string body = line.Substring(eq + 1).Trim();
                    var values = body.Split('|')
                        .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
                    if (values.Length == 0) continue;

                    if (line[0] == 'C')
                        consonantInitials[key] = values;
                    else
                        vowelFinals[key] = values;

                } else if (line.StartsWith("S:")) {
                    // Format: S:syl1,syl2,...
                    string body = line.Substring(2).Trim();
                    foreach (var syl in body.Split(',')) {
                        var s = syl.Trim();
                        if (s.Length > 0) validSyllables.Add(s);
                    }
                }
            }
        }

        // ── Phonemizer API ───────────────────────────────────────────

        public override void SetSinger(USinger singer) {
            this.singer = singer;
        }

        public override Result Process(Note[] notes, Note? prev, Note? next,
            Note? prevNeighbour, Note? nextNeighbour, Note[] prevs) {

            var note = notes[0];
            string lyric = note.lyric.Normalize();

            // Forced alias
            if (lyric.Length > 0 && lyric[0] == '?')
                return MakeSimpleResult(lyric.Substring(1));

            // Extension / rest / breath
            if (lyric == "+" || lyric.StartsWith("+~") || lyric.StartsWith("+*"))
                return MakeSimpleResult(lyric);
            if (lyric == "R") return MakeSimpleResult("R");
            if (lyric == "-") return MakeSimpleResult("SP");

            // Phonetic hint bypass
            if (!string.IsNullOrEmpty(note.phoneticHint)) {
                var hintPhonemes = note.phoneticHint.Split()
                    .Where(s => s.Length > 0)
                    .Select(s => TryMapPinyinToOto(s, note.tone))
                    .ToArray();
                return DistributePhonemes(notes, hintPhonemes);
            }

            // ── Stage 1: CMUdict → ARPAbet ─────────────────────────
            string[]? arpa = arpabetG2p?.Query(lyric.ToLowerInvariant())
                          ?? arpabetG2p?.Query(lyric);

            if (arpa == null || arpa.Length == 0) {
                return DistributePhonemes(notes, new[] {
                    TryMapPinyinToOto(lyric.ToLowerInvariant(), note.tone)
                });
            }

            // Normalise to uppercase
            arpa = arpa.Select(p => p.ToUpperInvariant()).ToArray();

            // ── Stage 2: syllable-based ARPAbet → Pinyin ────────────
            var rawPinyins = SyllableMap(arpa);

            // ── Stage 3a: merge bare vowels with same final ─────────
            var merged = MergeByFinal(rawPinyins);

            // ── Stage 3b: merge overlapping finals (la+ai→lai) ──────
            merged = MergeByFinalOverlap(merged, note.tone);

            // ── Stage 4: OTO lookup & distribute ────────────────────
            var mapped = merged.Select(p => TryMapPinyinToOto(p, note.tone)).ToArray();
            return DistributePhonemes(notes, mapped);
        }

        // ── Stage 2: syllable-based mapping ─────────────────────────

        /// <summary>
        /// Parses the ARPAbet sequence into (onset, nucleus) syllable pairs.
        /// Each vowel is a syllable nucleus; consonants before it form
        /// the onset; trailing consonants after the last vowel become
        /// standalone coda syllables.
        ///
        /// For each pair: initial = consonant_mapping[onset] × vowel_mapping[nucleus]
        /// The combination is validated against the known-Chinese-syllable set.
        /// </summary>
        private string[] SyllableMap(string[] arpa) {
            // 1) Find vowel positions
            var vowelIdx = new List<int>();
            for (int i = 0; i < arpa.Length; i++) {
                if (ArpabetVowelSet.Contains(arpa[i]))
                    vowelIdx.Add(i);
            }
            if (vowelIdx.Count == 0) {
                // No vowels – map each consonant directly as a standalone
                return arpa.Select(a => MapConsonantStandalone(a)).ToArray();
            }

            var result = new List<string>();

            // 2) First syllable: onset = everything before first vowel
            int firstV = vowelIdx[0];
            if (firstV > 0) {
                // onset consonants → Chinese initial
                string initials = MapOnsetCluster(arpa.Take(firstV));
                string finals   = MapNucleusVowel(arpa[firstV], initials);
                string syllable = MakeSyllable(initials, finals);
                result.Add(syllable);
            } else {
                // Syllable starts with a vowel (no onset consonant)
                result.Add(MapNucleusVowel(arpa[0], ""));
            }

            // 3) Remaining syllables
            for (int vi = 1; vi < vowelIdx.Count; vi++) {
                int prevV = vowelIdx[vi - 1];
                int thisV = vowelIdx[vi];
                int onsetStart = prevV + 1;
                int onsetCount = thisV - onsetStart;

                if (onsetCount > 0) {
                    string initials = MapOnsetCluster(arpa.Skip(onsetStart).Take(onsetCount));
                    string finals   = MapNucleusVowel(arpa[thisV], initials);
                    result.Add(MakeSyllable(initials, finals));
                } else {
                    // Back-to-back vowels
                    result.Add(MapNucleusVowel(arpa[thisV], ""));
                }
            }

            // 4) Coda: consonants after the last vowel
            //    Nasal codas (NG, N, M) try to fold into the last syllable's final.
            int lastV = vowelIdx[^1];
            if (lastV + 1 < arpa.Length) {
                string codaArpa = arpa[lastV + 1];
                if (IsNasalCoda(codaArpa)) {
                    // Try to absorb the nasal into the preceding syllable
                    string? folded = FoldNasalIntoSyllable(result[^1], codaArpa);
                    if (folded != null) {
                        result[^1] = folded;
                        // Skip this coda; process remaining codas normally
                        for (int i = lastV + 2; i < arpa.Length; i++)
                            result.Add(MapConsonantStandalone(arpa[i]));
                        return result.ToArray();
                    }
                }
            }
            // Normal coda processing
            for (int i = lastV + 1; i < arpa.Length; i++) {
                result.Add(MapConsonantStandalone(arpa[i]));
            }

            return result.ToArray();
        }

        // ── onset / nucleus mappers ──────────────────────────────────

        /// <summary>Maps a single onset consonant to its Chinese initial.</summary>
        private string MapOnsetConsonant(string arpaConsonant) {
            if (consonantInitials.TryGetValue(arpaConsonant, out var initials) && initials.Length > 0)
                return initials[0];
            return arpaConsonant.ToLowerInvariant(); // fallback
        }

        /// <summary>
        /// Maps an onset consonant cluster to a Chinese initial.
        /// For a single consonant, uses the consonant→initial map directly.
        /// For clusters (e.g. "S T" in "stop"), takes the first consonant
        /// as the primary initial (clusters don't exist in Chinese).
        /// </summary>
        private string MapOnsetCluster(IEnumerable<string> consonants) {
            var list = consonants.ToList();
            if (list.Count == 0) return "";
            // Use the primary (first) consonant of the cluster
            return MapOnsetConsonant(list[0]);
        }

        /// <summary>
        /// Maps a nucleus vowel to a Chinese final.
        /// When a preceding initial is known, prefers a final that
        /// forms a valid syllable with it.
        /// </summary>
        private string MapNucleusVowel(string arpaVowel, string precedingInitial) {
            if (!vowelFinals.TryGetValue(arpaVowel, out var finals) || finals.Length == 0)
                return arpaVowel.ToLowerInvariant();

            // If we have a preceding initial, try finals in order until
            // one produces a valid Chinese syllable
            if (!string.IsNullOrEmpty(precedingInitial)) {
                foreach (var f in finals) {
                    if (IsValidSyllable(precedingInitial + f))
                        return f;
                }
            }
            // Fallback: return the first (default) final
            return finals[0];
        }

        /// <summary>Maps a coda consonant to a standalone Chinese syllable.</summary>
        private string MapConsonantStandalone(string arpaConsonant) {
            if (consonantInitials.TryGetValue(arpaConsonant, out var initials) && initials.Length > 0) {
                // Try attaching each common final until we get a valid syllable
                foreach (var init in initials) {
                    foreach (var final in new[] { "e", "a", "u", "i", "o", "ou", "ei" }) {
                        string candidate = init + final;
                        if (IsValidSyllable(candidate))
                            return candidate;
                    }
                }
                // Fallback: just the initial + "e" (most neutral)
                return initials[0] + "e";
            }
            return arpaConsonant.ToLowerInvariant();
        }

        /// <summary>Builds a Chinese syllable from initial + final, validated.</summary>
        private string MakeSyllable(string initial, string final) {
            string candidate = initial + final;
            if (IsValidSyllable(candidate))
                return candidate;

            // If the direct combination is invalid, try the final alone
            if (IsValidSyllable(final))
                return final;

            // Last resort
            return candidate;
        }

        private bool IsValidSyllable(string pinyin) {
            return validSyllables.Contains(pinyin.ToLowerInvariant());
        }

        private static bool IsNasalCoda(string arpa) => arpa is "NG" or "N" or "M";

        /// <summary>
        /// Tries to fold a nasal coda into the preceding syllable by
        /// appending the nasal to each possible final of the syllable
        /// and checking whether the result is a valid Chinese syllable.
        /// Returns the folded syllable or null.
        /// </summary>
        private string? FoldNasalIntoSyllable(string syllable, string nasalArpa) {
            string nasalSuffix = nasalArpa switch {
                "NG" => "ng",
                "N"  => "n",
                "M"  => "m",  // "m" coda is rare in Chinese but can map to "n"
                _    => ""
            };
            if (string.IsNullOrEmpty(nasalSuffix)) return null;

            string init = GetInitial(syllable);
            string final = GetFinal(syllable);

            // Try: final + nasal
            string candidate = init + final + nasalSuffix;
            if (IsValidSyllable(candidate))
                return candidate;

            // Try alternate nasals (e.g., N might fold better as "ng")
            foreach (var altNasal in new[] { "n", "ng" }) {
                if (altNasal == nasalSuffix) continue;
                candidate = init + final + altNasal;
                if (IsValidSyllable(candidate))
                    return candidate;
            }

            // Try: replace the final entirely with a known nasal-final
            // e.g., AO→"o" + NG → "ong" is valid
            candidate = init + "ong";
            if (nasalArpa == "NG" && IsValidSyllable(candidate))
                return candidate;
            candidate = init + "an";
            if (nasalArpa == "N" && IsValidSyllable(candidate))
                return candidate;
            candidate = init + "en";
            if (nasalArpa == "N" && IsValidSyllable(candidate))
                return candidate;

            return null;
        }

        // ── Stage 3a: merge bare vowels with same final ──────────────

        /// <summary>
        /// Absorbs consecutive bare-vowel entries into the preceding
        /// syllable when they share the same final.
        /// Example: [ha, a, lou, ou] → [ha, lou]
        /// </summary>
        private string[] MergeByFinal(string[] pinyins) {
            if (pinyins.Length <= 1) return pinyins;

            var merged = new List<string> { pinyins[0] };

            for (int i = 1; i < pinyins.Length; i++) {
                string prev = merged[merged.Count - 1];
                string curr = pinyins[i];

                if (GetFinal(prev) == GetFinal(curr) && !HasChineseInitial(curr)) {
                    // curr is a bare vowel tail → absorbed by prev
                } else {
                    merged.Add(curr);
                }
            }

            return merged.ToArray();
        }

        // ── Stage 3b: merge overlapping finals ──────────────────────

        /// <summary>
        /// When prev-final matches the START of curr AND curr has no
        /// initial consonant, fuse them: prev-initial + curr → one syllable.
        /// Only fuses when the result exists in the singer's OTO.
        /// Example: la + ai → lai
        /// </summary>
        private string[] MergeByFinalOverlap(string[] pinyins, int tone) {
            if (pinyins.Length <= 1) return pinyins;

            var merged = new List<string>(pinyins);

            for (int i = 1; i < merged.Count; i++) {
                string prev = merged[i - 1];
                string curr = merged[i];
                string prevFinal = GetFinal(prev);

                if (!HasChineseInitial(curr) && HasChineseInitial(prev)
                    && prevFinal.Length > 0 && curr.StartsWith(prevFinal)) {

                    string prevInit = GetInitial(prev);
                    string candidate = prevInit + curr;

                    if (singer != null && singer.Found
                        && singer.TryGetMappedOto(candidate, tone, out _)) {
                        merged[i - 1] = candidate;
                        merged.RemoveAt(i);
                        i--;
                    }
                }
            }

            return merged.ToArray();
        }

        // ── Stage 4: distribute phonemes ─────────────────────────────

        private Result DistributePhonemes(Note[] notes, string[] syllables) {
            int totalDuration = notes.Sum(n => n.duration);
            if (totalDuration <= 0) totalDuration = 480;
            if (syllables.Length == 0)
                return MakeSimpleResult("");

            int count = syllables.Length;
            int baseLen = totalDuration / count;
            int remainder = totalDuration % count;

            var phonemes = new Phoneme[count];
            int pos = 0;
            for (int i = 0; i < count; i++) {
                int dur = baseLen + (i < remainder ? 1 : 0);
                phonemes[i] = new Phoneme {
                    phoneme = syllables[i],
                    position = pos,
                };
                pos += dur;
            }

            return new Result { phonemes = phonemes };
        }

        // ── helpers ──────────────────────────────────────────────────

        private string GetFinal(string pinyin) {
            if (pinyinFinalCache.TryGetValue(pinyin, out var cached))
                return cached;
            string final = ComputeFinal(pinyin);
            pinyinFinalCache[pinyin] = final;
            return final;
        }

        private static string ComputeFinal(string pinyin) {
            if (string.IsNullOrEmpty(pinyin)) return "";
            pinyin = pinyin.ToLowerInvariant();
            foreach (var init in ChineseInitials.OrderByDescending(i => i.Length)) {
                if (pinyin.StartsWith(init) && pinyin.Length > init.Length)
                    return pinyin.Substring(init.Length);
            }
            return pinyin;
        }

        private static bool HasChineseInitial(string pinyin) {
            if (string.IsNullOrEmpty(pinyin)) return false;
            pinyin = pinyin.ToLowerInvariant();
            foreach (var init in ChineseInitials.OrderByDescending(i => i.Length)) {
                if (pinyin.StartsWith(init) && pinyin.Length > init.Length)
                    return true;
            }
            return false;
        }

        private static string GetInitial(string pinyin) {
            if (string.IsNullOrEmpty(pinyin)) return "";
            pinyin = pinyin.ToLowerInvariant();
            foreach (var init in ChineseInitials.OrderByDescending(i => i.Length)) {
                if (pinyin.StartsWith(init) && pinyin.Length > init.Length)
                    return init;
            }
            return "";
        }

        private string TryMapPinyinToOto(string pinyin, int tone) {
            if (singer == null || !singer.Found) return pinyin;
            if (singer.TryGetMappedOto(pinyin, tone, out _)) return pinyin;
            string stripped = ArpabetG2p.RemoveTailDigits(pinyin);
            if (stripped != pinyin && singer.TryGetMappedOto(stripped, tone, out _))
                return stripped;
            return pinyin;
        }

        public override string ToString() => "[EN to ZH] English to Chinese Phonemizer";
    }
}
