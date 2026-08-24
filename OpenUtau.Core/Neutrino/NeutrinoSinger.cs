using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using K4os.Hash.xxHash;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenUtau.Classic;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core.Neutrino
{
    public sealed class NeutrinoSinger : USinger
    {
        private readonly Voicebank voicebank;
        private readonly List<string> errors = new List<string>();
        private readonly List<USubbank> subbanks = new List<USubbank>();
        private readonly List<UOto> otos = new List<UOto>();
        private readonly object sessionLock = new object();

        private byte[] avatarData;
        private InferenceSession timingSession;
        private InferenceSession pitchSession;
        private InferenceSession melspecSession;
        private InferenceSession vocoderSession;
        private string timingModelPath = string.Empty;
        private string pitchModelPath = string.Empty;
        private string melspecModelPath = string.Empty;
        private string vocoderModelPath = string.Empty;
        private ulong modelFingerprint;
        private IReadOnlyDictionary<string, string[]> lyricDictionary =
            new Dictionary<string, string[]>(StringComparer.Ordinal);

        public NeutrinoConfig Config { get; private set; } = new NeutrinoConfig();

        public override string Id => voicebank.Id;
        public override string Name => voicebank.Name;
        public override Dictionary<string, string> LocalizedNames => voicebank.LocalizedNames;
        public override USingerType SingerType => USingerType.Neutrino;
        public override string BasePath => voicebank.BasePath;
        public override string Author => voicebank.Author;
        public override string Voice => voicebank.Voice;
        public override string Location => Path.GetDirectoryName(voicebank.File);
        public override string Web => voicebank.Web;
        public override string Version => voicebank.Version;
        public override string OtherInfo => voicebank.OtherInfo;
        public override IList<string> Errors => errors;
        public override string Avatar => voicebank.Image == null
            ? null
            : Path.Combine(Location, voicebank.Image);
        public override byte[] AvatarData => avatarData;
        public override string Portrait => voicebank.Portrait == null
            ? null
            : Path.Combine(Location, voicebank.Portrait);
        public override float PortraitOpacity => voicebank.PortraitOpacity;
        public override int PortraitHeight => voicebank.PortraitHeight;
        public override string Sample => voicebank.Sample == null
            ? null
            : Path.Combine(Location, voicebank.Sample);
        public override string DefaultPhonemizer => voicebank.DefaultPhonemizer
            ?? typeof(NeutrinoPhonemizer).FullName;
        public override Encoding TextFileEncoding => voicebank.TextFileEncoding;
        public override IList<USubbank> Subbanks => subbanks;
        public override IList<UOto> Otos => otos;

        public NeutrinoSinger(Voicebank voicebank)
        {
            this.voicebank = voicebank;
            found = true;
        }

        public override void EnsureLoaded()
        {
            if (!Loaded)
            {
                Reload();
            }
        }

        public override void EnsureAvatarLoaded()
        {
            if (avatarData == null)
            {
                LoadAvatarData();
            }
        }

        public override void Reload()
        {
            if (!Found)
            {
                return;
            }
            try
            {
                lock (sessionLock)
                {
                    loaded = false;
                    DisposeSessionsNoLock();
                    ClearModelPathsNoLock();
                    voicebank.Reload();
                    Load();
                    loaded = true;
                }
            }
            catch (Exception exception)
            {
                loaded = false;
                errors.Add(exception.Message);
                Log.Error(
                    exception,
                    "Failed to load NEUTRINO singer {SingerFile}",
                    voicebank.File);
            }
        }

        private void Load()
        {
            errors.Clear();
            Config = NeutrinoConfig.Load(Location);
            EnsureModelPaths();

            IReadOnlyDictionary<string, string[]> loadedDictionary =
                new Dictionary<string, string[]>(StringComparer.Ordinal);
            string dictionaryPath = ResolveDictionaryPath();
            if (!string.IsNullOrEmpty(dictionaryPath))
            {
                loadedDictionary = NeutrinoPhoneme.LoadDictionary(dictionaryPath);
            }
            lyricDictionary = loadedDictionary;

            subbanks.Clear();
            subbanks.Add(new USubbank(new Subbank()
            {
                Prefix = string.Empty,
                Suffix = string.Empty,
                ToneRanges = new string[] { "C1-B7" },
            }));

            otos.Clear();
            HashSet<string> aliases = new HashSet<string>(StringComparer.Ordinal);
            foreach (string phoneme in NeutrinoPhoneme.AllPhonemes)
            {
                if (aliases.Add(phoneme))
                {
                    otos.Add(UOto.OfDummy(phoneme));
                }
            }
            LoadAvatarData();
        }

        public string[] LyricToPhonemes(string lyric)
        {
            return NeutrinoPhoneme.KanaToPhonemes(lyric, lyricDictionary);
        }

        public string[] RenderPhoneToPhonemes(string phone)
        {
            return NeutrinoPhoneme.RenderPhoneToPhonemes(phone, lyricDictionary);
        }

        private void LoadAvatarData()
        {
            avatarData = null;
            if (string.IsNullOrEmpty(Avatar) || !File.Exists(Avatar))
            {
                return;
            }
            try
            {
                avatarData = File.ReadAllBytes(Avatar);
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Failed to load NEUTRINO singer avatar");
            }
        }

        private string ResolveDictionaryPath()
        {
            List<string> candidates = new List<string>();
            AddDictionaryCandidate(
                candidates,
                Path.Combine(Location, "settings", "dic", "japanese.utf_8.table"));

            DirectoryInfo directory = new DirectoryInfo(Location);
            for (int i = 0; i < 5 && directory != null; i++, directory = directory.Parent)
            {
                AddDictionaryCandidate(
                    candidates,
                    Path.Combine(
                        directory.FullName,
                        "settings",
                        "dic",
                        "japanese.utf_8.table"));
            }

            AddDictionaryCandidate(
                candidates,
                Path.Combine(
                    PathManager.Inst.DataPath,
                    "settings",
                    "dic",
                    "japanese.utf_8.table"));
            return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
        }

        private static void AddDictionaryCandidate(List<string> candidates, string path)
        {
            if (!string.IsNullOrEmpty(path) && !candidates.Contains(path))
            {
                candidates.Add(path);
            }
        }

        public void EnsureTimingSession()
        {
            if (timingSession != null)
            {
                return;
            }
            lock (sessionLock)
            {
                EnsureModelPaths();
                timingSession ??= LoadSessionWithCpuFallback(timingModelPath, "t.bin");
            }
        }

        public void EnsurePitchSession()
        {
            if (pitchSession != null)
            {
                return;
            }
            lock (sessionLock)
            {
                EnsureModelPaths();
                pitchSession ??= LoadSessionWithCpuFallback(pitchModelPath, "p.bin");
            }
        }

        public void EnsureMelspecSession()
        {
            if (melspecSession != null)
            {
                return;
            }
            lock (sessionLock)
            {
                EnsureModelPaths();
                melspecSession ??= LoadSessionWithCpuFallback(melspecModelPath, "s.bin");
            }
        }

        public void EnsureVocoderSession()
        {
            if (vocoderSession != null)
            {
                return;
            }
            lock (sessionLock)
            {
                EnsureModelPaths();
                vocoderSession ??= LoadSessionWithCpuFallback(vocoderModelPath, "v.bin");
            }
        }

        public float[] RunTiming(IReadOnlyCollection<NamedOnnxValue> inputs)
        {
            lock (sessionLock)
            {
                EnsureTimingSession();
                return RunWithCpuFallback(
                    ref timingSession,
                    timingModelPath,
                    inputs,
                    "t.bin");
            }
        }

        public float[] RunPitch(IReadOnlyCollection<NamedOnnxValue> inputs)
        {
            lock (sessionLock)
            {
                EnsurePitchSession();
                return RunWithCpuFallback(
                    ref pitchSession,
                    pitchModelPath,
                    inputs,
                    "p.bin");
            }
        }

        public float[] RunMelspec(IReadOnlyCollection<NamedOnnxValue> inputs)
        {
            lock (sessionLock)
            {
                EnsureMelspecSession();
                return RunWithCpuFallback(
                    ref melspecSession,
                    melspecModelPath,
                    inputs,
                    "s.bin");
            }
        }

        public float[] RunVocoder(IReadOnlyCollection<NamedOnnxValue> inputs)
        {
            lock (sessionLock)
            {
                EnsureVocoderSession();
                return RunWithCpuFallback(
                    ref vocoderSession,
                    vocoderModelPath,
                    inputs,
                    "v.bin");
            }
        }

        private float[] RunWithCpuFallback(
            ref InferenceSession session,
            string modelPath,
            IReadOnlyCollection<NamedOnnxValue> inputs,
            string modelName)
        {
            try
            {
                return RunFirstOutput(session, inputs);
            }
            catch (OnnxRuntimeException exception)
                when (!string.Equals(
                    Preferences.Default.OnnxRunner,
                    "CPU",
                    StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning(
                    exception,
                    "NEUTRINO {ModelName} failed on {Runner}; retrying on CPU",
                    modelName,
                    Preferences.Default.OnnxRunner);
                session?.Dispose();
                session = LoadSession(modelPath, OnnxRunnerChoice.CPU);
                return RunFirstOutput(session, inputs);
            }
        }

        private static float[] RunFirstOutput(
            InferenceSession session,
            IReadOnlyCollection<NamedOnnxValue> inputs)
        {
            lock (session)
            {
                using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs =
                    session.Run(inputs);
                if (outputs.Count == 0)
                {
                    throw new InvalidDataException("NEUTRINO model returned no outputs.");
                }
                return outputs.First().AsTensor<float>().ToArray();
            }
        }

        private void EnsureModelPaths()
        {
            if (!string.IsNullOrEmpty(timingModelPath)
                && !string.IsNullOrEmpty(pitchModelPath)
                && !string.IsNullOrEmpty(melspecModelPath)
                && !string.IsNullOrEmpty(vocoderModelPath))
            {
                return;
            }

            string modelDirectory = ResolveModelDirectory();
            timingModelPath = RequireModel(modelDirectory, "t.bin");
            pitchModelPath = RequireModel(modelDirectory, "p.bin");
            melspecModelPath = RequireModel(modelDirectory, "s.bin");
            vocoderModelPath = RequireModel(modelDirectory, "v.bin");
            modelFingerprint = ComputeModelFingerprint(
                timingModelPath,
                pitchModelPath,
                melspecModelPath,
                vocoderModelPath);
        }

        public ulong ModelFingerprint
        {
            get
            {
                lock (sessionLock)
                {
                    EnsureModelPaths();
                    return modelFingerprint;
                }
            }
        }

        private string ResolveModelDirectory()
        {
            string nested = Path.Combine(Location, "model");
            if (HasV3Models(nested))
            {
                return nested;
            }
            if (HasV3Models(Location))
            {
                return Location;
            }
            return nested;
        }

        private static bool HasV3Models(string directory)
        {
            return File.Exists(Path.Combine(directory, "t.bin"))
                && File.Exists(Path.Combine(directory, "p.bin"))
                && File.Exists(Path.Combine(directory, "s.bin"))
                && File.Exists(Path.Combine(directory, "v.bin"));
        }

        private static string RequireModel(string modelDirectory, string fileName)
        {
            string path = Path.Combine(modelDirectory, fileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"NEUTRINO v3 model file was not found: {path}",
                    path);
            }
            return path;
        }

        private static InferenceSession LoadSession(
            string path,
            OnnxRunnerChoice runnerChoice)
        {
            return Onnx.getInferenceSession(path, runnerChoice);
        }

        private static InferenceSession LoadSessionWithCpuFallback(
            string path,
            string modelName)
        {
            try
            {
                return LoadSession(path, OnnxRunnerChoice.Default);
            }
            catch (OnnxRuntimeException exception)
                when (!string.Equals(
                    Preferences.Default.OnnxRunner,
                    "CPU",
                    StringComparison.OrdinalIgnoreCase))
            {
                Log.Warning(
                    exception,
                    "Failed to create NEUTRINO {ModelName} session on {Runner}; retrying on CPU",
                    modelName,
                    Preferences.Default.OnnxRunner);
                return LoadSession(path, OnnxRunnerChoice.CPU);
            }
        }

        private static ulong ComputeModelFingerprint(params string[] modelPaths)
        {
            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, true);
            foreach (string path in modelPaths)
            {
                FileInfo info = new FileInfo(path);
                writer.Write(Path.GetFileName(path));
                writer.Write(info.Length);
                writer.Write(info.LastWriteTimeUtc.Ticks);
            }
            writer.Flush();
            return XXH64.DigestOf(stream.ToArray());
        }

        private void DisposeSessionsNoLock()
        {
            timingSession?.Dispose();
            timingSession = null;
            pitchSession?.Dispose();
            pitchSession = null;
            melspecSession?.Dispose();
            melspecSession = null;
            vocoderSession?.Dispose();
            vocoderSession = null;
        }

        private void ClearModelPathsNoLock()
        {
            timingModelPath = string.Empty;
            pitchModelPath = string.Empty;
            melspecModelPath = string.Empty;
            vocoderModelPath = string.Empty;
            modelFingerprint = 0;
        }

        public override void FreeMemory()
        {
            lock (sessionLock)
            {
                DisposeSessionsNoLock();
            }
        }

        public override bool TryGetOto(string phoneme, out UOto oto)
        {
            oto = UOto.OfDummy(phoneme);
            return true;
        }

        public override IEnumerable<UOto> GetSuggestions(string text)
        {
            string query = text?.Replace(" ", string.Empty) ?? string.Empty;
            return otos.Where(oto => string.IsNullOrEmpty(query)
                || oto.Alias.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        public override byte[] LoadPortrait()
        {
            return string.IsNullOrEmpty(Portrait) ? null : File.ReadAllBytes(Portrait);
        }

        public override byte[] LoadSample()
        {
            return string.IsNullOrEmpty(Sample) ? null : File.ReadAllBytes(Sample);
        }
    }
}
