using System;

namespace OpenUtauMobile.Services.Game;

/// <summary>
/// GAME 推理参数（与 Core.Analysis.GameOptions 对齐，但本类型不依赖 OpenUtau.Core）。
/// 默认值与 Core 的 ONNX 实现一致，保证行为对齐上游 infer.py。
/// </summary>
public sealed class GameGgmlOptions
{
    /// <summary>语言代码，例如 "en"、"zh"；null = universal/自动。</summary>
    public string? LanguageCode { get; set; }

    /// <summary>D3PM 去噪步数（--nsteps）。默认 8。</summary>
    public int SamplingSteps { get; set; } = 8;

    /// <summary>边界解码阈值（--seg-threshold）。默认 0.2。</summary>
    public float BoundaryThreshold { get; set; } = 0.2f;

    /// <summary>边界解码半径（帧，--seg-radius）。默认 2。</summary>
    public int BoundaryRadius { get; set; } = 2;

    /// <summary>音符存在性门槛（--est-threshold）。默认 0.2。</summary>
    public float ScoreThreshold { get; set; } = 0.2f;

    /// <summary>随机种子；0 = 自动（由原生层 OS 随机）。</summary>
    public ulong Seed { get; set; } = 0UL;
}
