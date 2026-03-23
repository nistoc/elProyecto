namespace Agent04.Features.Transcription.Application;

/// <summary>Progress during <see cref="IAudioUtils.WriteWavWithCompressedSilence"/> (per-segment extract + final concat).</summary>
/// <param name="StepCompleted">Finished segment extractions (1..speechTotal) while <see cref="Stage"/> is <c>segments</c>; <c>speechTotal</c> when starting concat; <c>TotalSteps</c> when concat finished.</param>
/// <param name="TotalSteps">Speech extractions + 1 (final concat).</param>
/// <param name="TimelinePositionSec">End time of last processed speech in the original timeline (seconds).</param>
/// <param name="Stage"><c>segments</c> | <c>concat</c></param>
public readonly record struct SilenceCompressionProgress(
    int StepCompleted,
    int TotalSteps,
    double TimelinePositionSec,
    string Stage);
