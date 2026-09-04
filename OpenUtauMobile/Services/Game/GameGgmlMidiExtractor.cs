using System;
using System.Collections.Generic;
using OpenUtau.Core.Analysis;
using OpenUtau.Core.Ustx;

namespace OpenUtauMobile.Services.Game;

/// <summary>
/// 用 GameGgml（原生 GAME ggml 后端）替代 Core 的 ONNX Game 的转写器。
///
/// 技巧：**继承 OpenUtau.Core 的 MidiExtractor&lt;GameOptions&gt; 基类**（只读复用，不改 Core 一行），
/// 实现它的 <see cref="MidiExtractor{TOptions}.TranscribeWaveform"/>；基类负责
/// mono→44.1k 重采样→AudioSlicer 分块→批推理→UVoicePart 组装（位置/时长 tick 换算、
/// CreateNote、End 修正）。唯一差异 = 底层的 ONNX 换成我们的 ggml C ABI。
///
/// 注意：原生 Model 非线程安全。基类的 Transcribe 串行调用 TranscribeWaveform，
/// 我们单模型句柄 + 串行使用即可。
/// </summary>
public sealed class GameGgmlMidiExtractor : MidiExtractor<GameOptions>
{
    private GameGgml? model;

    /// <summary>底层采用 44100Hz 单声道 float（与 game_capi 一致）。基类会先重采样到此处。</summary>
    protected override int ExpectedSampleRate => 44100;

    /// <summary>一次 infer 已是全段（原生内部 mel→D3PM→边界→音符），不支持也不必要分批。</summary>
    protected override bool SupportsBatch => false;

    protected override List<TranscribedNote> TranscribeWaveform(float[] samples, GameOptions options)
    {
        GameGgml modelValue = EnsureModel();
        GameGgmlOptions gameOptions = ToGameOptions(options);
        IReadOnlyList<GameGgmlNote> rawNotes = modelValue.Infer(samples, gameOptions);

        List<TranscribedNote> result = new(rawNotes.Count);
        foreach (GameGgmlNote note in rawNotes)
        {
            result.Add(new TranscribedNote(
                note.DurationSeconds,
                note.PitchMidi,
                note.Voiced != 0));
        }

        return result;
    }

    private GameGgml EnsureModel()
    {
        if (this.model != null)
        {
            return this.model;
        }

        GameGgml? opened = GameGgml.Open(out string error);
        if (opened == null)
        {
            throw new InvalidOperationException($"GAME 模型打开失败: {error}");
        }

        this.model = opened;
        return opened;
    }

    private static GameGgmlOptions ToGameOptions(GameOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        return new GameGgmlOptions
        {
            LanguageCode = options.LanguageCode,
            SamplingSteps = options.SamplingSteps,
            BoundaryThreshold = options.BoundaryThreshold,
            BoundaryRadius = options.BoundaryRadius,
            ScoreThreshold = options.ScoreThreshold,
            Seed = 0UL,
        };
    }

    protected override void DisposeManaged()
    {
        this.model?.Dispose();
        this.model = null;
    }
}
