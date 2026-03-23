using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Concatenates sub-chunk <see cref="TranscriptionResult"/> in ascending <see cref="SubIndex"/> order.
/// Within each sub-chunk, segment order matches the source. Timestamps are shifted by
/// <paramref name="parentChunkOffset"/> and each sub&apos;s <see cref="TranscriptionResult.Offset"/>.
/// No global sort by time and no cross-segment deduplication — preserves replica order for merged output.
/// </summary>
public sealed class TranscriptionMerger : ITranscriptionMerger
{
    public TranscriptionResult MergeTranscriptions(
        IReadOnlyList<(int SubIndex, TranscriptionResult Result)> subResults,
        double parentChunkOffset = 0.0)
    {
        if (subResults.Count == 0)
            throw new ArgumentException("No sub-results to merge", nameof(subResults));

        var sorted = subResults.OrderBy(x => x.SubIndex).ToList();
        var merged = new List<ASRSegment>();

        foreach (var (_, result) in sorted)
        {
            foreach (var seg in result.Segments)
            {
                var isFallback = Math.Abs(seg.Start) < 1e-9 && Math.Abs(seg.End) < 1e-9;
                if (!isFallback && seg.Start < result.EmitGuard)
                {
                    if (seg.End <= result.EmitGuard)
                        continue;
                }

                var adjStart = seg.Start + result.Offset + parentChunkOffset;
                var adjEnd = seg.End + result.Offset + parentChunkOffset;
                merged.Add(new ASRSegment(adjStart, adjEnd, seg.Text, seg.Speaker));
            }
        }

        var firstBasename = sorted[0].Result.ChunkBasename;
        return new TranscriptionResult("merged_" + firstBasename, parentChunkOffset, 0.0, merged, new Dictionary<string, object?>());
    }
}
