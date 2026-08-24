using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Serilog;
using WanaKanaNet;

namespace OpenUtau.Core.Neutrino
{
    public static class NeutrinoPhoneme
    {
        public const int PAU = 0;
        public const int BR = 3;
        public const int AP = 41;

        private static readonly Dictionary<string, int> PhonemeToId =
            new Dictionary<string, int>()
            {
                { "pau", 0 },
                { "sil", 0 },
                { "a", 1 },
                { "b", 2 },
                { "br", 3 },
                { "by", 4 },
                { "ch", 5 },
                { "cl", 6 },
                { "d", 7 },
                { "dy", 8 },
                { "e", 9 },
                { "f", 10 },
                { "g", 11 },
                { "gy", 12 },
                { "h", 13 },
                { "hy", 14 },
                { "i", 15 },
                { "j", 16 },
                { "k", 17 },
                { "ky", 18 },
                { "m", 19 },
                { "my", 20 },
                { "n", 21 },
                { "N", 22 },
                { "ny", 23 },
                { "o", 24 },
                { "p", 25 },
                { "py", 27 },
                { "r", 28 },
                { "ry", 29 },
                { "s", 30 },
                { "sh", 31 },
                { "t", 33 },
                { "ts", 34 },
                { "ty", 35 },
                { "u", 36 },
                { "v", 37 },
                { "w", 38 },
                { "y", 39 },
                { "z", 40 },
                { "AP", 41 },
                { "ap", 41 },
            };

        private static readonly Dictionary<string, string[]> RomajiToPhonemes =
            BuildRomajiMap();
        private static readonly Dictionary<string, string[]> KanaToPhonemeMap =
            new Dictionary<string, string[]>();
        private static readonly object DictionaryLock = new object();

        public static IEnumerable<string> AllPhonemes => PhonemeToId
            .Where(pair => pair.Key != "sil" && pair.Key != "ap")
            .OrderBy(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key)
            .Distinct();

        public static void LoadDictionary(string tablePath)
        {
            if (!File.Exists(tablePath))
            {
                return;
            }

            Dictionary<string, string[]> entries = new Dictionary<string, string[]>();
            foreach (string line in File.ReadAllLines(tablePath, Encoding.UTF8))
            {
                string trimmed = line.Trim();
                int commentIndex = trimmed.IndexOf('#');
                if (commentIndex >= 0)
                {
                    trimmed = trimmed.Substring(0, commentIndex).Trim();
                }
                if (string.IsNullOrEmpty(trimmed))
                {
                    continue;
                }

                string[] parts = trimmed.Split(
                    new char[] { ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                {
                    entries[parts[0].Normalize(NormalizationForm.FormC)] =
                        parts.Skip(1).ToArray();
                }
            }

            lock (DictionaryLock)
            {
                foreach (KeyValuePair<string, string[]> entry in entries)
                {
                    if (!KanaToPhonemeMap.ContainsKey(entry.Key))
                    {
                        KanaToPhonemeMap[entry.Key] = entry.Value.ToArray();
                    }
                }
            }
            Log.Information(
                "Loaded {Count} NEUTRINO dictionary entries from {Path}",
                entries.Count,
                tablePath);
        }

        public static int GetPhonemeId(string phoneme)
        {
            phoneme = phoneme?.Trim();
            if (string.IsNullOrEmpty(phoneme))
            {
                return PAU;
            }
            if (phoneme == "R"
                || phoneme.Equals("SP", StringComparison.OrdinalIgnoreCase)
                || phoneme.Equals("rest", StringComparison.OrdinalIgnoreCase))
            {
                return PAU;
            }
            if (PhonemeToId.TryGetValue(phoneme, out int id))
            {
                return id;
            }
            if (PhonemeToId.TryGetValue(phoneme.ToLowerInvariant(), out id))
            {
                return id;
            }
            Log.Warning("Unknown NEUTRINO phoneme: {Phoneme}", phoneme);
            return PAU;
        }

        public static string[] RenderPhoneToPhonemes(string phone)
        {
            phone = phone?.Trim();
            if (string.IsNullOrEmpty(phone))
            {
                return new string[] { "pau" };
            }
            if (IsKnownPhoneme(phone))
            {
                return new string[] { NormalizePhoneme(phone) };
            }
            return KanaToPhonemes(phone);
        }

        public static bool IsVowelPhoneme(string phoneme)
        {
            string normalized = NormalizePhoneme(phoneme?.Trim() ?? string.Empty);
            return normalized == "a"
                || normalized == "i"
                || normalized == "u"
                || normalized == "e"
                || normalized == "o"
                || normalized == "N"
                || normalized == "pau"
                || normalized == "AP";
        }

        public static string[] KanaToPhonemes(string kana)
        {
            kana = kana?.Trim();
            if (string.IsNullOrEmpty(kana))
            {
                return new string[] { "pau" };
            }
            if (kana == "R"
                || kana.Equals("SP", StringComparison.OrdinalIgnoreCase)
                || kana.Equals("rest", StringComparison.OrdinalIgnoreCase))
            {
                return new string[] { "pau" };
            }
            if (kana.Equals("n", StringComparison.OrdinalIgnoreCase)
                || kana.Equals("nn", StringComparison.OrdinalIgnoreCase)
                || kana == "ん"
                || kana == "ン")
            {
                return new string[] { "N" };
            }
            if (kana == "っ" || kana == "ッ")
            {
                return new string[] { "cl" };
            }

            string[] parts = kana.Split(
                new char[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1 && parts.All(IsKnownPhoneme))
            {
                return parts.Select(NormalizePhoneme).ToArray();
            }
            if (IsKnownPhoneme(kana))
            {
                return new string[] { NormalizePhoneme(kana) };
            }

            string normalizedKana = kana.Normalize(NormalizationForm.FormC);
            lock (DictionaryLock)
            {
                if (KanaToPhonemeMap.TryGetValue(normalizedKana, out string[] phonemes))
                {
                    return phonemes.ToArray();
                }
            }
            if (RomajiToPhonemes.TryGetValue(kana, out string[] directPhonemes))
            {
                return directPhonemes.ToArray();
            }

            try
            {
                string romaji = WanaKana.ToRomaji(normalizedKana)
                    .Replace("'", string.Empty)
                    .Replace("’", string.Empty)
                    .Trim();
                if (RomajiToPhonemes.TryGetValue(romaji, out string[] convertedPhonemes))
                {
                    return convertedPhonemes.ToArray();
                }
                if (TryMapRomajiSequence(romaji, out string[] sequencePhonemes))
                {
                    return sequencePhonemes;
                }
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "Failed to romanize NEUTRINO lyric {Lyric}", kana);
            }

            Log.Warning("Kana or romaji is not supported by NEUTRINO: {Lyric}", kana);
            return new string[] { "pau" };
        }

        private static bool TryMapRomajiSequence(
            string romaji,
            out string[] phonemes)
        {
            List<string> result = new List<string>();
            string[] syllables = RomajiToPhonemes.Keys
                .OrderByDescending(key => key.Length)
                .ThenBy(key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            int position = 0;
            while (position < romaji.Length)
            {
                if (position + 1 < romaji.Length
                    && romaji[position] == romaji[position + 1]
                    && "aeioun".IndexOf(
                        char.ToLowerInvariant(romaji[position])) < 0)
                {
                    result.Add("cl");
                    position++;
                    continue;
                }
                string syllable = syllables.FirstOrDefault(candidate =>
                    position + candidate.Length <= romaji.Length
                    && romaji.AsSpan(position, candidate.Length).Equals(
                        candidate.AsSpan(),
                        StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(syllable))
                {
                    phonemes = Array.Empty<string>();
                    return false;
                }
                result.AddRange(RomajiToPhonemes[syllable]);
                position += syllable.Length;
            }
            phonemes = result.ToArray();
            return phonemes.Length > 0;
        }

        private static Dictionary<string, string[]> BuildRomajiMap()
        {
            Dictionary<string, string[]> map =
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            AddVowelSeries(map, string.Empty, string.Empty);
            AddVowelSeries(map, "k", "k");
            AddVowelSeries(map, "g", "g");
            AddVowelSeries(map, "s", "s");
            AddVowelSeries(map, "z", "z");
            AddVowelSeries(map, "t", "t");
            AddVowelSeries(map, "d", "d");
            AddVowelSeries(map, "n", "n");
            AddVowelSeries(map, "h", "h");
            AddVowelSeries(map, "b", "b");
            AddVowelSeries(map, "p", "p");
            AddVowelSeries(map, "m", "m");
            AddVowelSeries(map, "r", "r");
            AddVowelSeries(map, "w", "w");
            AddVowelSeries(map, "v", "v");
            AddVowelSeries(map, "f", "f");

            AddPalatalizedSeries(map, "ky", "ky");
            AddPalatalizedSeries(map, "gy", "gy");
            AddPalatalizedSeries(map, "ny", "ny");
            AddPalatalizedSeries(map, "hy", "hy");
            AddPalatalizedSeries(map, "by", "by");
            AddPalatalizedSeries(map, "py", "py");
            AddPalatalizedSeries(map, "my", "my");
            AddPalatalizedSeries(map, "ry", "ry");
            AddPalatalizedSeries(map, "dy", "dy");
            AddPalatalizedSeries(map, "ty", "ty");

            AddSpecial(map, "shi", "sh", "i");
            AddSpecial(map, "si", "s", "i");
            AddSpecial(map, "sha", "sh", "a");
            AddSpecial(map, "shu", "sh", "u");
            AddSpecial(map, "she", "sh", "e");
            AddSpecial(map, "sho", "sh", "o");
            AddSpecial(map, "sya", "sh", "a");
            AddSpecial(map, "syu", "sh", "u");
            AddSpecial(map, "sye", "sh", "e");
            AddSpecial(map, "syo", "sh", "o");

            AddSpecial(map, "ji", "j", "i");
            AddSpecial(map, "zi", "z", "i");
            AddSpecial(map, "ja", "j", "a");
            AddSpecial(map, "ju", "j", "u");
            AddSpecial(map, "je", "j", "e");
            AddSpecial(map, "jo", "j", "o");
            AddSpecial(map, "zya", "j", "a");
            AddSpecial(map, "zyu", "j", "u");
            AddSpecial(map, "zye", "j", "e");
            AddSpecial(map, "zyo", "j", "o");

            AddSpecial(map, "chi", "ch", "i");
            AddSpecial(map, "cha", "ch", "a");
            AddSpecial(map, "chu", "ch", "u");
            AddSpecial(map, "che", "ch", "e");
            AddSpecial(map, "cho", "ch", "o");
            AddSpecial(map, "tsu", "ts", "u");
            AddSpecial(map, "tsa", "ts", "a");
            AddSpecial(map, "tsi", "ts", "i");
            AddSpecial(map, "tse", "ts", "e");
            AddSpecial(map, "tso", "ts", "o");
            AddSpecial(map, "fu", "f", "u");
            AddSpecial(map, "hu", "f", "u");

            AddGlideSeries(map, "kw", "k", "w");
            AddGlideSeries(map, "gw", "g", "w");
            map["ya"] = new string[] { "y", "a" };
            map["yu"] = new string[] { "y", "u" };
            map["ye"] = new string[] { "y", "e" };
            map["yo"] = new string[] { "y", "o" };
            map["n"] = new string[] { "N" };
            map["nn"] = new string[] { "N" };
            return map;
        }

        private static void AddVowelSeries(
            Dictionary<string, string[]> map,
            string spellingPrefix,
            string phonemePrefix)
        {
            foreach (string vowel in new string[] { "a", "i", "u", "e", "o" })
            {
                map[spellingPrefix + vowel] = string.IsNullOrEmpty(phonemePrefix)
                    ? new string[] { vowel }
                    : new string[] { phonemePrefix, vowel };
            }
        }

        private static void AddPalatalizedSeries(
            Dictionary<string, string[]> map,
            string spellingPrefix,
            string phoneme)
        {
            foreach (string vowel in new string[] { "a", "i", "u", "e", "o" })
            {
                map[spellingPrefix + vowel] = new string[] { phoneme, vowel };
            }
        }

        private static void AddGlideSeries(
            Dictionary<string, string[]> map,
            string spellingPrefix,
            string firstPhoneme,
            string glidePhoneme)
        {
            foreach (string vowel in new string[] { "a", "i", "u", "e", "o" })
            {
                map[spellingPrefix + vowel] =
                    new string[] { firstPhoneme, glidePhoneme, vowel };
            }
        }

        private static void AddSpecial(
            Dictionary<string, string[]> map,
            string spelling,
            string consonant,
            string vowel)
        {
            map[spelling] = new string[] { consonant, vowel };
        }

        private static bool IsKnownPhoneme(string phoneme)
        {
            phoneme = phoneme?.Trim();
            if (string.IsNullOrEmpty(phoneme))
            {
                return false;
            }
            return PhonemeToId.ContainsKey(phoneme)
                || PhonemeToId.ContainsKey(phoneme.ToLowerInvariant());
        }

        private static string NormalizePhoneme(string phoneme)
        {
            if (PhonemeToId.ContainsKey(phoneme))
            {
                return phoneme;
            }
            string lower = phoneme.ToLowerInvariant();
            return PhonemeToId.ContainsKey(lower) ? lower : phoneme;
        }
    }
}
