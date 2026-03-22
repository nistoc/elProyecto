using Agent04.Features.Transcription.Domain;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public sealed class TranscriptionMergerTests
{
    [Fact]
    public void Merge_two_subs_applies_parent_offset_and_orders_segments()
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
        Assert.True(merged.Segments[0].Start <= merged.Segments[1].Start);
        Assert.Equal(10.0, merged.Segments[0].Start, 3);
        Assert.Equal(11.0, merged.Segments[0].End, 3);
        Assert.Equal(10.5, merged.Segments[1].Start, 3);
        Assert.Equal(11.5, merged.Segments[1].End, 3);
    }
}
