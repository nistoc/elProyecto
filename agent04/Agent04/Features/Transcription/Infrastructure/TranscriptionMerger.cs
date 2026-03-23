using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;

namespace Agent04.Features.Transcription.Infrastructure;

public sealed class TranscriptionMerger : ITranscriptionMerger
{
    private const double TimeThreshold = 0.5;
    private const double TextSimilarityThreshold = 0.8;

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

        merged = merged.OrderBy(s => s.Start).ToList();
        merged = DeduplicateSegments(merged);
        var firstBasename = sorted[0].Result.ChunkBasename;
        return new TranscriptionResult("merged_" + firstBasename, parentChunkOffset, 0.0, merged, new Dictionary<string, object?>());
    }

    private static List<ASRSegment> DeduplicateSegments(List<ASRSegment> segments)
    {
        if (segments.Count <= 1) return segments;
        var result = new List<ASRSegment> { segments[0] };
        for (var i = 1; i < segments.Count; i++)
        {
            var seg = segments[i];
            var prev = result[result.Count - 1];
            var timeDiff = Math.Abs(seg.Start - prev.Start);
            if (timeDiff < TimeThreshold)
            {
                var sim = TextSimilarity(seg.Text.Trim().ToLowerInvariant(), prev.Text.Trim().ToLowerInvariant());
                if (sim >= TextSimilarityThreshold)
                {
                    if (seg.Text.Length > prev.Text.Length)
                        result[result.Count - 1] = seg;
                    continue;
                }
            }
            result.Add(seg);
        }
        return result;
    }

    private static double TextSimilarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
        if (a == b) return 1.0;
        var w1 = new HashSet<string>(a.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var w2 = new HashSet<string>(b.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (w1.Count == 0 || w2.Count == 0) return 0;
        var inter = w1.Intersect(w2).Count();
        var union = w1.Union(w2).Count();
        return union > 0 ? (double)inter / union : 0;
    }
}
