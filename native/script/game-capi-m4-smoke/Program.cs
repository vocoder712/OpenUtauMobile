using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenUtau.Core.Analysis;
using OpenUtau.Core.Ustx;
using OpenUtauMobile.Services.Game;

namespace GameM4Smoke;

/// <summary>
/// M4 无 GUI 端到端冒烟：用真实语音 .wav 驱动 M4 的 GameGgmlMidiExtractor.Transcribe，验证
/// 「音频 → mono/重采样 → AudioSlicer 分块 → GAME ggml infer → UVoicePart 生成」整条链路。
/// 与 App 内 EditorViewModel.TranscribeAudio 的差别只有：不走 FilePicker/LoadingPopup/undo 命令。
/// </summary>
internal static class Program
{
    private static int Main(string[] rawArgs)
    {
        Console.WriteLine("== M4 headless smoke: GameGgmlMidiExtractor.Transcribe ==");
        Console.WriteLine($"GAME version: {GameGgml.VersionString()}");
        Console.WriteLine($"ggml version: {GameGgml.GgmlVersionString()}");
        Console.WriteLine($"available backends: {GameGgml.AvailableBackendsString()}");

        string wav = rawArgs.Length > 0 && System.IO.File.Exists(rawArgs[0])
            ? rawArgs[0]
            : System.IO.Path.GetFullPath(System.IO.Path.Combine("pick", "voice10.wav"));
        if (!System.IO.File.Exists(wav))
        {
            Console.WriteLine($"音频不存在: {wav}");
            return 1;
        }

        List<int> lookback = new();
        try
        {
            UProject project = new();
            UWavePart wavePart = new() { FilePath = wav };
            wavePart.Load(project);
            wavePart.Peaks.Wait(); // 等待 Samples 就绪
            if (wavePart.Samples == null)
            {
                Console.WriteLine("Samples 未就绪 (null)");
                return 1;
            }

            Console.WriteLine(
                $"wave: ch={wavePart.channels} sampleRate={wavePart.sampleRate} samples={wavePart.Samples.Length}");

            Stopwatch sw = Stopwatch.StartNew();
            int totalDone = 0, totalTotal = 0;
            UVoicePart? part = null;
            using (GameGgmlMidiExtractor extractor = new())
            {
                GameOptions options = new() { SamplingSteps = 8 };
                part = extractor.Transcribe(
                    project, wavePart, options, null, null,
                    (done, total) =>
                    {
                        totalDone = done;
                        totalTotal = total;
                        Console.WriteLine($"  progress {done}s / {total}s");
                    });
            }

            sw.Stop();
            if (part == null)
            {
                Console.WriteLine("Transcribe 返回 null（确认回调取消？）");
                return 1;
            }

            int voiced = part.notes.Count;
            Console.WriteLine($"elapsed={sw.Elapsed.TotalSeconds:F1}s progress={totalDone}s/{totalTotal}s");
            Console.WriteLine($"UVoicePart notes={part.notes.Count} voiced={voiced}");
            Console.WriteLine($"part position={part.position} duration={part.Duration}");
            if (voiced > 0)
            {
                UNote first = part.notes.First();
                Console.WriteLine($"first note: pos={first.position} dur={first.duration} tone={first.tone} lyric='{first.lyric}'");
                UNote last = part.notes.Last();
                Console.WriteLine($"last note: pos={last.position} dur={last.duration} tone={last.tone} lyric='{last.lyric}'");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {ex}");
            return 1;
        }
    }
}
