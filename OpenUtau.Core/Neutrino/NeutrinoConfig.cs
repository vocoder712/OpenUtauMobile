using System;
using System.IO;
using Serilog;

namespace OpenUtau.Core.Neutrino
{
    public sealed class NeutrinoConfig
    {
        public string SingerName { get; private set; } = string.Empty;
        public string Gender { get; private set; } = "female";
        public string Language { get; private set; } = "Japanese";
        public int TopKey { get; private set; } = 86;
        public int BottomKey { get; private set; } = 41;
        public string ModelVersion { get; private set; } = string.Empty;
        public bool Support { get; private set; } = true;

        public static NeutrinoConfig Load(string singerPath)
        {
            NeutrinoConfig config = new NeutrinoConfig();
            string infoPath = Path.Combine(singerPath, "model", "info.toml");
            if (!File.Exists(infoPath))
            {
                infoPath = Path.Combine(singerPath, "info.toml");
            }
            if (!File.Exists(infoPath))
            {
                Log.Warning("NEUTRINO info.toml not found in {SingerPath}", singerPath);
                return config;
            }

            string section = string.Empty;
            foreach (string line in File.ReadAllLines(infoPath))
            {
                string trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                {
                    continue;
                }
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    section = trimmed.Substring(1, trimmed.Length - 2).Trim();
                    continue;
                }

                int equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex < 0)
                {
                    continue;
                }
                string key = trimmed.Substring(0, equalsIndex).Trim();
                string value = trimmed.Substring(equalsIndex + 1).Trim().Trim('"');
                ApplyValue(config, section, key, value);
            }
            return config;
        }

        private static void ApplyValue(
            NeutrinoConfig config,
            string section,
            string key,
            string value)
        {
            if (string.IsNullOrEmpty(section) || section == "speaker")
            {
                switch (key)
                {
                    case "name":
                        config.SingerName = value;
                        break;
                    case "gender":
                        config.Gender = value;
                        break;
                    case "language":
                        config.Language = value;
                        break;
                    case "top_key":
                        if (int.TryParse(value, out int topKey))
                        {
                            config.TopKey = topKey;
                        }
                        break;
                    case "bottom_key":
                        if (int.TryParse(value, out int bottomKey))
                        {
                            config.BottomKey = bottomKey;
                        }
                        break;
                    case "version":
                        config.ModelVersion = value;
                        break;
                    case "support":
                        config.Support = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                        break;
                }
                return;
            }

            if (section == "acoustic")
            {
                switch (key)
                {
                    case "version":
                        config.ModelVersion = value;
                        break;
                    case "top_key":
                        if (int.TryParse(value, out int topKey))
                        {
                            config.TopKey = topKey;
                        }
                        break;
                    case "bottom_key":
                        if (int.TryParse(value, out int bottomKey))
                        {
                            config.BottomKey = bottomKey;
                        }
                        break;
                }
            }
        }

        public static double MidiToFreq(double midi)
        {
            return 440.0 * Math.Pow(2.0, (midi - 69.0) / 12.0);
        }
    }
}
