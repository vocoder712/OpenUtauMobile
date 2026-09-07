using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenUtau.Api;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.ViewModels;
using Serilog;
using Serilog.Core;
using Serilog.Events;

internal static class Program
{
    private static readonly float[][] Embeddings = [[1, 0, 0, 2, -1], [0, 1, 0, 4, 2], [0, 0, 1, -3, 5]];
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int passed;
    private static string root = "";

    private static void Main(string[] args)
    {
        root = Path.GetFullPath(args.Length == 0 ? "artifacts/pr287/fixtures" : args[0]);
        Directory.CreateDirectory(root);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Test("phone one-hot selection, embedding orientation and tensor shape", TestPhones);
        Test("Voice Color 0/50/100%, >100%, negative and tiny weights", TestCurves);
        Test("PaddedSegments gap frames retain preceding speaker", TestGapMapping);
        Test("suffix exact/path matching, warning deduplication and invalid speakers", TestSuffixes);
        Test("duration config layouts, priority and distinct errors", TestLayouts);
        Test("speaker fallback order, no subbanks, null and empty speakers", TestFallback);
        Test("archive UTAU/DiffSinger/Enunu/Neutrino, declaration and warning", TestArchives);
        TestBaseline();
        Console.WriteLine($"PASS: {passed} regression groups");
    }

    private static void Test(string name, Action action)
    {
        action();
        passed++;
        Console.WriteLine($"PASS {name}");
    }

    private static DsConfig Config() => new() { speakers = ["A", "B", "C"], hiddenSize = Embeddings[0].Length };

    private static DiffSingerSpeakerEmbedManager Manager()
    {
        DsConfig config = Config();
        for (int i = 0; i < config.speakers.Count; i++)
        {
            using BinaryWriter writer = new(File.Create(Path.Combine(root, config.speakers[i] + ".emb")));
            foreach (float value in Embeddings[i])
            {
                writer.Write(value);
            }
        }
        return new DiffSingerSpeakerEmbedManager(config, root);
    }

    private static void TestPhones()
    {
        Tensor<float> result = Manager().PhraseSpeakerEmbedByPhone(["C", "A", "B", "A"]);
        Check(result.Dimensions.ToArray().SequenceEqual([1, 4, 5]), "phone shape");
        Near(result.ToArray(), Embeddings[2].Concat(Embeddings[0]).Concat(Embeddings[1]).Concat(Embeddings[0]).ToArray());
    }

    private static RenderPhrase Phrase(params (string Suffix, double Start, double Duration)[] phones)
    {
        RenderPhrase phrase = (RenderPhrase)RuntimeHelpers.GetUninitializedObject(typeof(RenderPhrase));
        Set(phrase, "singer", new TestSinger(root, Banks()));
        UProject project = new();
        project.timeAxis.BuildSegments(project);
        Set(phrase, "timeAxis", project.timeAxis);
        Set(phrase, "phones", phones.Select(phone =>
        {
            RenderPhone result = (RenderPhone)RuntimeHelpers.GetUninitializedObject(typeof(RenderPhone));
            Set(result, "suffix", phone.Suffix);
            Set(result, "phoneme", "a");
            Set(result, "positionMs", phone.Start);
            Set(result, "durationMs", phone.Duration);
            Set(result, "endMs", phone.Start + phone.Duration);
            return result;
        }).ToArray());
        Set(phrase, "curves", Array.Empty<Tuple<string, float[]>>());
        return phrase;
    }

    private static Tensor<float> Frames(object manager, RenderPhrase phrase)
    {
        int[] durations = DiffSingerUtils.PaddedPhoneDurations(phrase, 10, 2, 2);
        return (Tensor<float>)Invoke(manager, "PhraseSpeakerEmbedByFrame", phrase, durations, 10f, durations.Sum(), 2, 2)!;
    }

    private static void TestCurves()
    {
        foreach ((float b, float c) in new (float, float)[] { (0, 0), (50, 0), (100, 0), (80, 60), (-50, 0), (0.0000001f, 0), (-25, 150) })
        {
            RenderPhrase phrase = Phrase(("A", 0, 30));
            Set(phrase, "curves", new[] { Tuple.Create("cl02", new[] { b }), Tuple.Create("cl03", new[] { c }) });
            float[] weights = [0, b * 0.01f, c * 0.01f];
            float sum = weights.Sum();
            if (sum > 1)
            {
                for (int i = 0; i < weights.Length; i++) weights[i] /= sum;
            }
            else
            {
                weights[0] += 1 - sum;
            }
            Tensor<float> result = Frames(Manager(), phrase);
            float[] expected = Enumerable.Range(0, 5).Select(d => Enumerable.Range(0, 3).Sum(s => weights[s] * Embeddings[s][d])).ToArray();
            for (int frame = 0; frame < result.Dimensions[1]; frame++)
            {
                Near(Enumerable.Range(0, 5).Select(d => result[0, frame, d]).ToArray(), expected);
            }
            Console.WriteLine($"  cl_B={b}, cl_C={c}: [{string.Join(",", weights)}]");
        }
    }

    private static void TestGapMapping()
    {
        RenderPhrase phrase = Phrase(("A", 0, 20), ("B", 50, 30), ("C", 80, 10));
        List<(string Phoneme, double DurationMs, int PhoneIndex)> segments = DiffSingerUtils.PaddedSegments(phrase, 10, 2, 2);
        Check(segments.Select(s => s.PhoneIndex).SequenceEqual([-1, 0, -1, 1, 2, -1]), "padded phone indexes");
        int[] durations = DiffSingerUtils.PaddedPhoneDurations(phrase, 10, 2, 2);
        int[] speakers = [0, 0, 0, 1, 2, 2];
        float[] expected = speakers.SelectMany((speaker, i) => Enumerable.Range(0, durations[i]).SelectMany(_ => Embeddings[speaker])).ToArray();
        Near(Frames(Manager(), phrase).ToArray(), expected);
    }

    private static void TestSuffixes()
    {
        DsConfig config = Config();
        config.speakers = ["nested/A", "A", "nested/B"];
        DiffSingerSpeakerEmbedManager manager = new(config, root);
        Check(manager.getSpeakerIndexBySuffix("A") == 1, "exact match before path suffix");
        Check(manager.getSpeakerIndexBySuffix("B") == 2, "path suffix");
        CaptureSink sink = new();
        using Logger logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        ILogger original = Log.Logger;
        Log.Logger = logger;
        try
        {
            for (int i = 0; i < 3; i++) Check(manager.getSpeakerIndexBySuffix("missing") == 0, "missing fallback");
            Check(sink.Events.Count == 1, "one warning per missing suffix");
        }
        finally { Log.Logger = original; }
        Throws<InvalidOperationException>(() => new DiffSingerSpeakerEmbedManager(new DsConfig(), root).getSpeakerIndexBySuffix("A"));
        config.speakers = [];
        Throws<InvalidOperationException>(() => manager.getSpeakerIndexBySuffix("A"));
        Throws<InvalidOperationException>(() => manager.getSpeakerEmbeds());
    }

    private static void TestLayouts()
    {
        string nested = Path.Combine(root, "nested");
        string flat = Path.Combine(root, "flat");
        string empty = Path.Combine(root, "empty");
        Directory.CreateDirectory(Path.Combine(nested, "dsdur"));
        Directory.CreateDirectory(flat);
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(nested, "dsdur", "dsconfig.yaml"), "hidden_size: 5");
        File.WriteAllText(Path.Combine(nested, "dsconfig.yaml"), "hidden_size: 9");
        File.WriteAllText(Path.Combine(flat, "dsconfig.yaml"), "hidden_size: 5");
        foreach ((string location, string expected) in new[] { (nested, Path.Combine(nested, "dsdur")), (flat, flat) })
        {
            ProbePhonemizer phonemizer = new();
            Throws<ProbeComplete>(() => phonemizer.SetSinger(new TestSinger(location, [])));
            Check(phonemizer.ConfigRoot == expected, "config selection reached G2P loading");
            Check(((DsConfig)Get(phonemizer, "dsConfig")!).hiddenSize == 5, "selected config parsed");
        }
        Throws<InvalidOperationException>(() => new ProbePhonemizer().SetSinger(new TestSinger(null!, [])), "location");
        Throws<InvalidOperationException>(() => new ProbePhonemizer().SetSinger(new TestSinger("", [])), "location");
        Throws<FileNotFoundException>(() => new ProbePhonemizer().SetSinger(new TestSinger(empty, [])), "dsconfig.yaml");
    }

    private static List<USubbank> Banks() => [new(new Subbank { Suffix = "A", Color = "red" }), new(new Subbank { Suffix = "B", Color = "red", ToneRanges = ["C4"] }), new(new Subbank { Suffix = "C", Color = "blue" })];

    private static void TestFallback()
    {
        ProbePhonemizer phonemizer = new();
        DsConfig config = Config();
        Set(phonemizer, "dsConfig", config);
        Set(phonemizer, "singer", new TestSinger(root, Banks()));
        Phonemizer.Note note = new() { tone = 60, phonemeAttributes = [new() { index = 0, voiceColor = "red" }] };
        Check((string)Invoke(phonemizer, "GetSpeakerAtIndex", note, 0)! == "B", "tone + color");
        note.tone = 70;
        Check((string)Invoke(phonemizer, "GetSpeakerAtIndex", note, 0)! == "A", "color fallback");
        note.phonemeAttributes = [new() { index = 0, voiceColor = "missing" }];
        Check((string)Invoke(phonemizer, "GetSpeakerAtIndex", note, 0)! == "A", "first subbank");
        Set(phonemizer, "singer", new TestSinger(root, []));
        Check((string)Invoke(phonemizer, "GetSpeakerAtIndex", note, 0)! == "A", "first ds speaker");
        config.speakers = null!;
        Check((string)Invoke(phonemizer, "GetSpeakerAtIndex", note, 0)! == "", "embedding disabled");
        config.speakers = [];
        Throws<InvalidOperationException>(() => Invoke(phonemizer, "GetSpeakerAtIndex", note, 0));
    }

    private static void TestArchives()
    {
        ClassicSingerSetupViewModel vm = (ClassicSingerSetupViewModel)RuntimeHelpers.GetUninitializedObject(typeof(ClassicSingerSetupViewModel));
        vm.SingerTypes = ["utau", "enunu", "diffsinger", "neutrino"];
        string archivePath = Path.Combine(root, "singer.zip");
        foreach ((string[] files, string expected) in new (string[], string)[] { (["character.txt"], "utau"), (["Singer/dsconfig.yaml"], "diffsinger"), (["Singer/enuconfig.yaml"], "enunu"), (["Singer/info.toml"], "neutrino"), (["Singer/dsconfig.yaml", "Singer/enuconfig.yaml"], "enunu"), (["Singer\\dsdur\\dsconfig.yaml"], "diffsinger") })
        {
            using (FileStream stream = File.Create(archivePath))
            using (ZipArchive archive = new(stream, ZipArchiveMode.Create))
            {
                foreach (string file in files) archive.CreateEntry(file);
            }
            Check((string)Invoke(vm, "DetermineSingerType", null, archivePath)! == expected, expected + " archive");
        }
        CaptureSink sink = new();
        using Logger logger = new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
        ILogger original = Log.Logger;
        Log.Logger = logger;
        try
        {
            foreach (string declared in vm.SingerTypes)
            {
                VoicebankConfig config = new() { SingerType = declared };
                Check((string)Invoke(vm, "DetermineSingerType", config, "absent.zip")! == declared, "declaration bypasses scan");
            }
            Check(sink.Events.Count == 0, "declarations did not scan missing archive");
            Check((string)Invoke(vm, "DetermineSingerType", new VoicebankConfig { SingerType = "invalid" }, "absent.zip")! == "utau", "invalid declaration fallback");
            Check(sink.Events.Count == 1, "invalid declaration warning");
            File.WriteAllText(archivePath, "invalid archive bytes");
            Check((string)Invoke(vm, "DetermineSingerType", null, archivePath)! == "utau", "corrupt archive fallback");
            Check(sink.Events.Count == 2 && sink.Events[^1].Exception != null, "corrupt archive warning with exception");
        }
        finally { Log.Logger = original; }
    }

    private static void TestBaseline()
    {
        Type? baselineType = typeof(Program).Assembly.GetType("OpenUtau.Core.DiffSinger.BaselineSpeakerEmbedManager");
        if (baselineType == null) return;
        object baseline = Activator.CreateInstance(baselineType, Config(), root)!;
        DiffSingerSpeakerEmbedManager modified = Manager();
        Near(((Tensor<float>)Invoke(baseline, "PhraseSpeakerEmbedByPhone", (object)new[] { "C", "A", "B" })!).ToArray(), modified.PhraseSpeakerEmbedByPhone(["C", "A", "B"]).ToArray());
        Random random = new(287);
        for (int i = 0; i < 100; i++)
        {
            RenderPhrase phrase = Phrase(("A", 0, 20), ("B", 50, 30), ("C", 80, 10));
            Set(phrase, "curves", Enumerable.Range(1, 3).Select(s => Tuple.Create($"cl{s:00}", Enumerable.Range(0, 30).Select(_ => (float)(random.NextDouble() * 250 - 75)).ToArray())).ToArray());
            Near(Frames(baseline, phrase).ToArray(), Frames(modified, phrase).ToArray());
        }
        Console.WriteLine("PASS latest-dev np.dot differential: phone + 100 seeded multi-curve/gap cases");
        passed++;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Near(float[] actual, float[] expected)
    {
        Check(actual.Length == expected.Length, "length mismatch");
        for (int i = 0; i < actual.Length; i++) Check(Math.Abs(actual[i] - expected[i]) <= 0.00001f, $"element {i}: {actual[i]} != {expected[i]}");
    }

    private static void Throws<T>(Action action, string? message = null) where T : Exception
    {
        try { action(); }
        catch (T exception)
        {
            Check(message == null || exception.Message.Contains(message), exception.Message);
            return;
        }
        throw new Exception($"Expected {typeof(T).Name}");
    }

    private static Type FieldType(object target) => target is DiffSingerBasePhonemizer ? typeof(DiffSingerBasePhonemizer) : target.GetType();
    private static void Set(object target, string name, object value) => FieldType(target).GetField(name, Fields)!.SetValue(target, value);
    private static object? Get(object target, string name) => FieldType(target).GetField(name, Fields)!.GetValue(target);
    private static object? Invoke(object target, string name, params object?[] args)
    {
        try { return FieldType(target).GetMethod(name, Fields)!.Invoke(target, args); }
        catch (TargetInvocationException exception) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException!).Throw(); throw; }
    }

    private sealed class TestSinger(string location, IList<USubbank> banks) : USinger
    {
        public override string Location => location;
        public override IList<USubbank> Subbanks => banks;
    }

    private sealed class ProbeComplete : Exception;
    private sealed class ProbePhonemizer : DiffSingerBasePhonemizer
    {
        public string ConfigRoot = "";
        protected override IG2p LoadG2p(string rootPath, bool useLangId = false)
        {
            ConfigRoot = rootPath;
            throw new ProbeComplete();
        }
    }

    private sealed class CaptureSink : ILogEventSink
    {
        public readonly List<LogEvent> Events = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }
}