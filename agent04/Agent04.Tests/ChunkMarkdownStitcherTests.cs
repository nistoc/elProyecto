using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public class ChunkMarkdownStitcherTests
{
    [Fact]
    public void ExtractTranscriptBodyLines_strips_markers()
    {
        var raw = ">>>>>>>\n- 1.00 speaker_0: \"a\"\n<<<<<\n";
        var body = ChunkMarkdownStitcher.ExtractTranscriptBodyLines(raw);
        Assert.Contains("- 1.00", body);
        Assert.DoesNotContain(">>>>>>>", body);
        Assert.DoesNotContain("<<<<<", body);
    }

    [Fact]
    public void ExtractTranscriptBodyLines_without_markers_returns_trimmed()
    {
        var raw = "  \n- 2.00 speaker_0: \"x\"\n";
        var body = ChunkMarkdownStitcher.ExtractTranscriptBodyLines(raw);
        Assert.Contains("- 2.00", body);
    }
}
