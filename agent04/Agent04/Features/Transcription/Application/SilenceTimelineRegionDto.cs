namespace Agent04.Features.Transcription.Application;

/// <summary>One silence interval on the source timeline (seconds), for operator UI.</summary>
public sealed record SilenceTimelineRegionDto(double StartSec, double EndSec);
