using System;
using System.Collections.Generic;
using System.Linq;
using OpenUtauMobile.Services.Game;

namespace GameCapiSmoke;

/// <summary>
/// 托管侧（C#）冒烟：验证 GameGgml 封装能对接原生 libgame_ggml_shared，
/// 从嵌入式模型解包 → open → infer → 回读音符。等用于 C 侧 game_capi_check，
/// 但走的是 .NET 10 LibraryImport 托管封装。
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.WriteLine("== GameCapi C# smoke ==");
        Console.WriteLine($"GAME version: {GameGgml.VersionString()}");
        Console.WriteLine($"ggml version: {GameGgml.GgmlVersionString()}");
        Console.WriteLine($"available backends: {GameGgml.AvailableBackendsString()}");

        GameGgml? model = GameGgml.Open(out string openError);
        if (model == null)
        {
            Console.WriteLine($"打开模型失败: {openError}");
            return 1;
        }

        try
        {
            Console.WriteLine($"model path: {model.ModelPath}");
            Console.WriteLine($"decided backend: {model.BackendDecidedString()}");

            // 3 秒 44.1k 单声道正弦滑音（220->440Hz），峰值 0.5（对齐 M1 smoke）。
            const int sampleRate = 44100;
            const double durationSeconds = 3.0;
            const float amplitude = 0.5f;
            float[] wave = BuildSineSweep(sampleRate, (int)(sampleRate * durationSeconds), 220.0, 440.0, amplitude);

            var options = new GameGgmlOptions
            {
                SamplingSteps = 1, // 最快，校验输出
                Seed = 42UL,
            };
            IReadOnlyList<GameGgmlNote> notes = model.Infer(wave, options);

            int voiced = notes.Count(n => n.Voiced != 0);
            double totalSeconds = notes.Sum(n => (double)n.DurationSeconds);
            Console.WriteLine($"notes={notes.Count} voiced={voiced} totalDuration={totalSeconds:F3}s");
            if (voiced > 0)
            {
                Console.WriteLine($"first voiced: t={notes.First(n => n.Voiced != 0).OffsetSeconds:F3}s " +
                                  $"dur={notes.First(n => n.Voiced != 0).DurationSeconds:F3}s " +
                                  $"midi={notes.First(n => n.Voiced != 0).PitchMidi:F1}");
            }

            return 0;
        }
        finally
        {
            model.Dispose();
        }
    }

    private static float[] BuildSineSweep(int sampleRate, int count, double startFreq, double endFreq, float amplitude)
    {
        float[] result = new float[count];
        double freqPerSample = (endFreq - startFreq) / count;
        double phase = 0.0;
        for (int i = 0; i < count; i++)
        {
            double freq = startFreq + freqPerSample * i;
            phase += 2.0 * Math.PI * freq / sampleRate;
            result[i] = (float)(amplitude * Math.Sin(phase));
        }

        return result;
    }
}
