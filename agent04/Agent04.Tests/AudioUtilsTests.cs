using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public class AudioUtilsTests
{
    private readonly AudioUtils _sut = new();

    [Fact]
    public void CalculateSegmentTime_WhenDurationZero_ReturnsDefault480()
    {
        var sec = _sut.CalculateSegmentTime(1_000_000, 0, 24.0);
        Assert.Equal(480, sec);
    }

    [Fact]
    public void CalculateSegmentTime_WhenDurationPositive_ReturnsAtLeast60()
    {
        // ~1 MB file, 10 sec => 100 KB/s => for 24 MB need ~240 sec per segment, but floor 60
        var sec = _sut.CalculateSegmentTime(1_000_000, 10.0, 24.0);
        Assert.True(sec >= 60, "Segment time should be at least 60 seconds");
    }

    [Fact]
    public void CalculateSegmentTime_RealisticAudio_ReturnsReasonableSegmentSeconds()
    {
        // 50 MB file, 300 sec (5 min) => target 24 MB => ~144 sec per segment
        long size = 50L * 1024 * 1024;
        double duration = 300.0;
        var sec = _sut.CalculateSegmentTime(size, duration, 24.0);
        Assert.InRange(sec, 60, 600);
    }

    [Fact]
    public void FormatMb_FormatsBytesAsMegabytes()
    {
        Assert.Equal("1.00 MB", _sut.FormatMb(1024 * 1024));
        Assert.Equal("0.00 MB", _sut.FormatMb(0));
        Assert.Equal("24.00 MB", _sut.FormatMb(24 * 1024 * 1024));
    }

    [Fact]
    public void WhichOr_WhenPathContainsSlash_ReturnsPathAsIs()
    {
        var path = "/usr/bin/ffprobe";
        var result = _sut.WhichOr(path, "ffprobe");
        Assert.Equal(path, result);
    }

    [Fact]
    public void WhichOr_WhenPathContainsBackslash_ReturnsPathAsIs()
    {
        var path = "C:\\Tools\\ffprobe.exe";
        var result = _sut.WhichOr(path, "ffprobe");
        Assert.Equal(path, result);
    }

    [Fact]
    public void WhichOr_WhenPathKeyNull_UsesDefaultName()
    {
        // Which() behavior depends on PATH; we only assert it doesn't throw and returns null or a path
        var result = _sut.WhichOr(null, "ffprobe");
        Assert.True(result == null || result.Length > 0);
    }

    [Fact]
    public void WhichOr_WhenPathKeyEmpty_UsesDefaultName()
    {
        var result = _sut.WhichOr("", "ffprobe");
        Assert.True(result == null || result.Length > 0);
    }
}
