using Agent04.Features.Transcription.Application;
using Xunit;

namespace Agent04.Tests;

public class TranscriptActivityLogFormatterTests
{
    [Fact]
    public void FormatLine_Warning_PrefixesBody()
    {
        var line = TranscriptActivityLogFormatter.FormatLine("OpenAI transcription HTTP timeout", TranscriptActivityLogKind.Warning);
        Assert.Contains("[warn] OpenAI transcription HTTP timeout", line, StringComparison.Ordinal);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T", line);
    }

    [Fact]
    public void TranscriptVmNodeId_ForTranscribeChunk_MatchesSinkConvention()
    {
        Assert.Equal("j:transcribe:chunk-3", TranscriptVmNodeId.ForTranscribeChunk("j", 3, null));
        Assert.Equal("j:transcribe:chunk-3:sub-1", TranscriptVmNodeId.ForTranscribeChunk("j", 3, 1));
    }
}
