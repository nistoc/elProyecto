using System.Globalization;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

/// <summary>
/// ffmpeg rejects <c>-ss 0,000</c>. On a machine whose regional format uses a decimal comma the
/// chunker used to build exactly that, and every job larger than one chunk failed before transcription.
/// </summary>
public class AudioChunkerFfmpegSecondsTests
{
    [Theory]
    [InlineData("ru-RU")]
    [InlineData("es-ES")]
    [InlineData("en-US")]
    public void Seconds_use_a_dot_under_any_culture(string cultureName)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            Assert.Equal("0.000", AudioChunker.FfmpegSeconds(0));
            Assert.Equal("156.250", AudioChunker.FfmpegSeconds(156.25));
            Assert.Equal("1251.477", AudioChunker.FfmpegSeconds(1251.477333));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
