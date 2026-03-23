using Agent04.Features.Transcription.Domain;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public sealed class TranscriptionMergerTests
{
    [Fact]
    public void Merge_two_subs_applies_parent_offset_preserves_sub_then_segment_order()
    {
        var merger = new TranscriptionMerger();
        var sub0 = new TranscriptionResult(
            "a_sub_00.wav",
            0,
            0,
            new[] { new ASRSegment(0, 1, "one", null) },
            new Dictionary<string, object?>());
        var sub1 = new TranscriptionResult(
            "a_sub_01.wav",
            0.5,
            0,
            new[] { new ASRSegment(0, 1, "two", null) },
            new Dictionary<string, object?>());

        var merged = merger.MergeTranscriptions(new[] { (0, sub0), (1, sub1) }, 10.0);

        Assert.Equal(10.0, merged.Offset, 4);
        Assert.Equal(2, merged.Segments.Count);
        Assert.Equal("one", merged.Segments[0].Text);
        Assert.Equal("two", merged.Segments[1].Text);
        Assert.Equal(10.0, merged.Segments[0].Start, 3);
        Assert.Equal(11.0, merged.Segments[0].End, 3);
        Assert.Equal(10.5, merged.Segments[1].Start, 3);
        Assert.Equal(11.5, merged.Segments[1].End, 3);
    }

    /// <summary>
    /// If a later sub-chunk has segments whose absolute times fall between timestamps of an earlier sub-chunk,
    /// merged order must still be: all segments of sub0, then all of sub1 — not sorted by Start.
    /// </summary>
    [Fact]
    public void Merge_does_not_globally_sort_segments_by_time_between_sub_chunks()
    {
        var merger = new TranscriptionMerger();
        const double parent = 10.0;
        var sub0 = new TranscriptionResult(
            "x_sub_00.wav",
            0,
            0,
            new[]
            {
                new ASRSegment(0, 1, "A", null),
                new ASRSegment(4, 5, "B", null),
            },
            new Dictionary<string, object?>());
        var sub1 = new TranscriptionResult(
            "x_sub_01.wav",
            0,
            0,
            new[] { new ASRSegment(2, 3, "C", null) },
            new Dictionary<string, object?>());

        var merged = merger.MergeTranscriptions(new[] { (0, sub0), (1, sub1) }, parent);

        Assert.Equal(3, merged.Segments.Count);
        Assert.Equal("A", merged.Segments[0].Text);
        Assert.Equal("B", merged.Segments[1].Text);
        Assert.Equal("C", merged.Segments[2].Text);

        // Absolute times: A 10–11, B 14–15, C 12–13 — not monotonic in list order by design.
        Assert.Equal(10.0, merged.Segments[0].Start, 3);
        Assert.Equal(14.0, merged.Segments[1].Start, 3);
        Assert.Equal(12.0, merged.Segments[2].Start, 3);
        Assert.True(merged.Segments[2].Start < merged.Segments[1].Start);
    }
}
