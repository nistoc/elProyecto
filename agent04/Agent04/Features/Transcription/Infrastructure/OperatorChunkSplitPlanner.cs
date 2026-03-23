namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Segment boundaries for operator split — same math as agent01 <c>ChunkSplitter.split_chunk</c> (overlap + stride).
/// </summary>
public static class OperatorChunkSplitPlanner
{
    public readonly record struct Segment(double StartSec, double DurationSec);

    /// <summary>
    /// Plan <paramref name="parts"/> segments over <paramref name="durationSec"/> with <paramref name="overlapSec"/> overlap between neighbours.
    /// </summary>
    public static IReadOnlyList<Segment> PlanEqualSegmentsWithOverlap(double durationSec, int parts, double overlapSec)
    {
        if (parts < 2)
            throw new ArgumentOutOfRangeException(nameof(parts));
        if (durationSec <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationSec));
        if (overlapSec < 0)
            overlapSec = 0;

        var segmentDuration = durationSec / parts + overlapSec * (parts - 1) / parts;
        var stride = parts > 1 ? (durationSec - segmentDuration) / (parts - 1) : durationSec;

        const double minSegment = 2.0;
        if (segmentDuration < minSegment)
        {
            segmentDuration = Math.Min(durationSec, minSegment);
            stride = parts > 1 ? (durationSec - segmentDuration) / (parts - 1) : durationSec;
        }

        var list = new List<Segment>(parts);
        for (var i = 0; i < parts; i++)
        {
            var start = i * stride;
            var end = i == parts - 1
                ? durationSec
                : Math.Min(start + segmentDuration, durationSec);
            var actualDur = end - start;
            if (actualDur < 0.01)
                actualDur = Math.Min(0.01, Math.Max(0, durationSec - start));
            list.Add(new Segment(start, actualDur));
        }

        return list;
    }
}
