using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using Serilog;
using WanaKanaNet;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Shared base for cross-lingual Chinese→Japanese phonemizers (CV, VCV, etc.).
    ///
    /// Provides:
    /// - Weighted pinyin→romaji mapping (loaded from embedded pinyin_zh_to_ja.txt)
    /// - OTO-driven alias resolution: tries kana first, then romaji — the
    ///   voicebank's OTO itself tells us which format to use (no format detection)
    /// - Vowel extraction for VCV-style linking
    ///
    /// Subclasses only need to implement <see cref="Process"/> with their specific
    /// phoneme-linking strategy.
    /// </summary>
    public abstract class BaseChineseToJapanesePhonemizer : Phonemizer {

        // ── shared types ──────────────────────────────────────────────

        /// <summary>(ratio, romaji) pair used in weighted mapping.</summary>
        protected readonly record struct WeightedOption(int Ratio, string Romaji);

        /// <summary>One scheme = an array of weighted romaji options.</summary>
        protected readonly record struct WeightedScheme(WeightedOption[] Options);

        // ── shared fields ─────────────────────────────────────────────

        protected USinger? singer;
        protected Dictionary<string, WeightedScheme[]> mapping = null!;

        // ── constants ─────────────────────────────────────────────────

        protected const double OverlapMs = 80;
        protected const string VcvPad = " ";

        // ── mapping loader ────────────────────────────────────────────

        protected void LoadMapping() {
            mapping = new Dictionary<string, WeightedScheme[]>();
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(
                "OpenUtau.Plugin.Builtin.Data.pinyin_zh_to_ja.txt");
            if (stream == null) {
                Log.Error("Embedded resource pinyin_zh_to_ja.txt not found");
                return;
            }
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) != null) {
                line = line.Trim();
                if (line.Length == 0 || line[0] == '#' || !line.Contains(';'))
                    continue;

                var parts = line.Split(';', 2);
                if (parts.Length != 2) continue;

                string pinyin = parts[0].Trim();
                if (pinyin.Length == 0) continue;

                var schemeStrs = parts[1].Trim().Split('_');
                var schemes = new List<WeightedScheme>();

                foreach (var schemeStr in schemeStrs) {
                    var tokens = schemeStr.Split(',');
                    var opts = new List<WeightedOption>();
                    bool valid = true;

                    foreach (var token in tokens) {
                        var dot = token.IndexOf('.');
                        if (dot <= 0) { valid = false; break; }
                        if (!int.TryParse(token.AsSpan(0, dot), out int ratio) || ratio <= 0)
                            { valid = false; break; }
                        string romaji = token.Substring(dot + 1).Trim();
                        if (romaji.Length == 0) { valid = false; break; }
                        opts.Add(new WeightedOption(ratio, romaji));
                    }

                    if (valid && opts.Count > 0)
                        schemes.Add(new WeightedScheme(opts.ToArray()));
                }

                if (schemes.Count > 0)
                    mapping[pinyin] = schemes.ToArray();
            }
        }

        // ── Phonemizer API ────────────────────────────────────────────

        public override void SetSinger(USinger singer) {
            this.singer = singer;
        }

        /// <summary>
        /// Early-return handling for special lyrics (forced alias, extension,
        /// rest, breath). Returns null if the lyric should be processed normally.
        /// </summary>
        protected string? HandleSpecialLyric(string lyric) {
            if (lyric.Length > 0 && lyric[0] == '?')
                return lyric.Substring(1);
            if (lyric == "+" || lyric.StartsWith("+~") || lyric.StartsWith("+*"))
                return lyric;
            if (lyric == "R" || lyric == "-")
                return lyric;
            return null;
        }

        // ── OTO-driven alias resolution ───────────────────────────────

        /// <summary>Safely converts romaji to hiragana. Returns the input unchanged on failure.</summary>
        protected static string ToKanaSafe(string romaji) {
            if (string.IsNullOrEmpty(romaji)) return romaji;
            try { return WanaKana.ToHiragana(romaji); }
            catch { return romaji; }
        }

        /// <summary>
        /// Resolves a single CV phoneme alias by trying kana first, then romaji.
        /// Follows the same pattern as the built-in Japanese phonemizers:
        /// let the OTO decide which format the voicebank actually uses.
        /// </summary>
        /// <param name="romaji">Romaji form, e.g. "ha", "tsu", "kyo".</param>
        /// <param name="tone">MIDI tone for OTO prefix-map lookup.</param>
        /// <returns>The alias that matched the OTO, or the kana form as fallback.</returns>
        protected string ResolveAlias(string romaji, int tone) {
            if (singer == null || !singer.Found)
                return romaji;

            string kana = ToKanaSafe(romaji);

            // 1) try kana (most Japanese banks)
            if (kana != romaji && singer.TryGetMappedOto(kana, tone, out _))
                return kana;

            // 2) try romaji
            if (singer.TryGetMappedOto(romaji, tone, out _))
                return romaji;

            // 3) neither matched — prefer kana for Japanese banks
            return kana != romaji ? kana : romaji;
        }

        /// <summary>
        /// Resolves a VCV phoneme alias by trying candidates in priority order:
        /// VCV-kana → bare-kana → VCV-romaji → bare-romaji.
        /// The first candidate that exists in the OTO wins.
        /// </summary>
        /// <param name="romaji">Romaji form, e.g. "ha".</param>
        /// <param name="tone">MIDI tone for OTO lookup.</param>
        /// <param name="vcvPrefix">
        ///   VCV linking vowel, e.g. "a", "i", "u", or "-" for phrase start.
        /// </param>
        /// <returns>The alias that matched the OTO, or VCV-kana as fallback.</returns>
        protected string ResolveVcvAlias(string romaji, int tone, string vcvPrefix) {
            if (singer == null || !singer.Found)
                return vcvPrefix + VcvPad + romaji;

            string kana = ToKanaSafe(romaji);

            // Priority order, matching built-in JA VCV behaviour:
            //   1. VCV kana    e.g. "a は"
            //   2. bare kana   e.g. "は"
            //   3. VCV romaji  e.g. "a ha"
            //   4. bare romaji e.g. "ha"
            string[] candidates = (kana != romaji) ? new[] {
                vcvPrefix + VcvPad + kana,
                kana,
                vcvPrefix + VcvPad + romaji,
                romaji,
            } : new[] {
                vcvPrefix + VcvPad + romaji,
                romaji,
            };

            foreach (var c in candidates) {
                if (singer.TryGetMappedOto(c, tone, out _))
                    return c;
            }

            return candidates[0];
        }

        // ── vowel helpers (used by VCV subclasses) ────────────────────

        /// <summary>
        /// Extracts the vowel from a Japanese romaji syllable.
        /// For CV syllables (ka, tsu, shi, kya) the vowel is the last character.
        /// "n" is treated as a syllabic nasal.
        /// </summary>
        protected static string ExtractVowel(string romaji) {
            if (string.IsNullOrEmpty(romaji)) return "a";
            return romaji[^1].ToString();
        }

        /// <summary>
        /// Re-computes the last sub-phoneme vowel of the previous note
        /// by looking up its lyric in the mapping table.
        /// Returns null if there is no previous note or the lookup fails.
        /// </summary>
        protected string? GetLastVowelOfNote(Note? prevNote) {
            if (prevNote == null) return null;

            string lyric = prevNote.Value.lyric.Normalize();
            if (string.IsNullOrEmpty(lyric) || lyric == "R" || lyric == "-")
                return null;
            if (lyric.Length > 0 && lyric[0] == '?')
                lyric = lyric.Substring(1);

            if (!mapping.TryGetValue(lyric, out var schemes) || schemes.Length == 0)
                return null;

            var opts = schemes[0].Options;
            if (opts.Length == 0) return null;

            return ExtractVowel(opts[^1].Romaji);
        }
    }
}
