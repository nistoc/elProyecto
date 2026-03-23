using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public class AudioChunkNamingTests
{
    [Fact]
    public void Align_replaces_m4a_template_with_wav_when_source_is_wav()
    {
        var r = AudioChunkNaming.AlignOutputExtensionWithSource(
            @"C:\work\clip_part_%03d.m4a",
            @"C:\audio\2026-03-04 20.02.28.wav");
        Assert.EndsWith("_part_%03d.wav", r.Replace('\\', '/'), StringComparison.Ordinal);
    }

    [Fact]
    public void Align_leaves_matching_extension_unchanged()
    {
        var p = "x_part_%03d.m4a";
        Assert.Equal(p, AudioChunkNaming.AlignOutputExtensionWithSource(p, @"a\b\z.m4a"));
    }

    [Fact]
    public void Align_appends_extension_when_template_has_none()
    {
        var r = AudioChunkNaming.AlignOutputExtensionWithSource("x_part_%03d", @"a.flac");
        Assert.Equal("x_part_%03d.flac", r);
    }
}
