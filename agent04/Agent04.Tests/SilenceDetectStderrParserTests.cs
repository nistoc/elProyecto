using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public sealed class SilenceDetectStderrParserTests
{
    [Fact]
    public void Parse_empty_returns_empty()
    {
        Assert.Empty(SilenceDetectStderrParser.Parse(""));
        Assert.Empty(SilenceDetectStderrParser.Parse("   "));
    }

    [Fact]
    public void Parse_typical_ffmpeg_stderr_returns_intervals()
    {
        const string stderr = """
            [silencedetect @ 0xaaa] silence_start: 0.0224375
            [silencedetect @ 0xaaa] silence_end: 0.522437 | silence_duration: 0.5
            [silencedetect @ 0xaaa] silence_start: 2
            [silencedetect @ 0xaaa] silence_end: 2.8 | silence_duration: 0.8
            """;

        var list = SilenceDetectStderrParser.Parse(stderr);
        Assert.Equal(2, list.Count);
        Assert.Equal(0.0224375, list[0].StartSec, 6);
        Assert.Equal(0.522437, list[0].EndSec, 6);
        Assert.Equal(0.5, list[0].DurationSec, 6);
        Assert.Equal(2, list[1].StartSec);
        Assert.Equal(2.8, list[1].EndSec);
        Assert.Equal(0.8, list[1].DurationSec);
    }

    [Fact]
    public void Parse_end_without_explicit_duration_computes_duration()
    {
        const string stderr = """
            silence_start: 1
            silence_end: 3.5
            """;

        var list = SilenceDetectStderrParser.Parse(stderr);
        Assert.Single(list);
        Assert.Equal(1, list[0].StartSec);
        Assert.Equal(3.5, list[0].EndSec);
        Assert.Equal(2.5, list[0].DurationSec);
    }

    [Fact]
    public void Parse_orphan_end_without_start_is_ignored()
    {
        const string stderr = "silence_end: 9 | silence_duration: 1\n";
        Assert.Empty(SilenceDetectStderrParser.Parse(stderr));
    }
}
