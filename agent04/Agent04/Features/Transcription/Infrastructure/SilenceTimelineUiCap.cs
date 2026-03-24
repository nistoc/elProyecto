using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>Caps silence interval count for gRPC/UI payload size.</summary>
public static class SilenceTimelineUiCap
{
    public const int MaxRegions = 500;

    public static IReadOnlyList<SilenceTimelineRegionDto> Cap(IReadOnlyList<SilenceInterval> intervals)
    {
        if (intervals.Count == 0)
            return Array.Empty<SilenceTimelineRegionDto>();
        if (intervals.Count <= MaxRegions)
        {
            var list = new SilenceTimelineRegionDto[intervals.Count];
            for (var i = 0; i < intervals.Count; i++)
            {
                var x = intervals[i];
                list[i] = new SilenceTimelineRegionDto(x.StartSec, x.EndSec);
            }

            return list;
        }

        var cap = new List<SilenceTimelineRegionDto>(MaxRegions);
        for (var k = 0; k < MaxRegions; k++)
        {
            var idx = (int)((k + 0.5) / MaxRegions * intervals.Count);
            if (idx >= intervals.Count)
                idx = intervals.Count - 1;
            var x = intervals[idx];
            cap.Add(new SilenceTimelineRegionDto(x.StartSec, x.EndSec));
        }

        return cap;
    }
}
