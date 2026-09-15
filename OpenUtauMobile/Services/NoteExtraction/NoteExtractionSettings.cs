using System;
using OpenUtau.Core.Analysis;

namespace OpenUtauMobile.Services.NoteExtraction;

internal sealed record NoteExtractionSettings(
    bool ExtractPitch = false,
    string? LanguageCode = null,
    int SamplingSteps = 8,
    float BoundaryThreshold = 0.2f,
    int BoundaryRadius = 2,
    float ScoreThreshold = 0.2f,
    int BatchSize = 1,
    float MaxBatchDuration = 60)
{
    public void Validate()
    {
        if (SamplingSteps is not (1 or 2 or 4 or 8 or 16)
            || !float.IsFinite(BoundaryThreshold) || BoundaryThreshold is < 0 or > 1
            || BoundaryRadius is < 0 or > 10
            || !float.IsFinite(ScoreThreshold) || ScoreThreshold is < 0 or > 1
            || BatchSize is < 1 or > 32
            || !float.IsFinite(MaxBatchDuration) || MaxBatchDuration is < 0 or > 600)
        {
            throw new ArgumentOutOfRangeException(nameof(NoteExtractionSettings));
        }
    }

    public GameOptions ToGameOptions() => new()
    {
        LanguageCode = LanguageCode,
        SamplingSteps = SamplingSteps,
        BoundaryThreshold = BoundaryThreshold,
        BoundaryRadius = BoundaryRadius,
        ScoreThreshold = ScoreThreshold,
    };
}

internal sealed record NoteExtractionRequest(NoteExtractionBackend Backend, NoteExtractionSettings Settings);
