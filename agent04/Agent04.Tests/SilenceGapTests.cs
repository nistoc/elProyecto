using System.Diagnostics;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

/// <summary>
/// The silence left in place of a pause is <c>silence_keep_sec</c>, but never more than the pause itself:
/// with a long keep (1 s) a short pause (0.5 s) must not grow.
/// </summary>
public sealed class SilenceGapTests
{
    [Theory]
    [InlineData(2.5, 0.2, 0.2)]      // the usual case: a long pause is cut down to keep
    [InlineData(0.3, 0.2, 0.2)]
    [InlineData(0.5, 1.0, 0.5)]      // a pause shorter than keep keeps its own length
    [InlineData(1.0, 1.0, 1.0)]
    [InlineData(0.4567, 1.0, 0.457)] // whole milliseconds
    [InlineData(3.0, 0.0, 0.0)]      // nothing is kept
    [InlineData(0.0, 0.2, 0.0)]
    public void GapFor_is_keep_but_not_more_than_the_pause(double pause, double keep, double expected)
    {
        Assert.Equal(expected, SpeechTimeMap.GapFor(pause, keep), 9);
    }

    [Fact]
    public void Plan_does_not_lengthen_a_pause_shorter_than_keep()
    {
        // speech 0–1, pause 0.5 s, speech 1.5–2.5, pause 2 s, speech 4.5–6
        var silence = new[] { new SilenceInterval(1.0, 1.5, 0.5), new SilenceInterval(2.5, 4.5, 2.0) };

        var steps = SpeechTimeMap.Plan(silence, 6, 1.0, 0.02);

        Assert.Equal(new[] { 0.0, 0.5, 0.0, 1.0, 0.0 }, steps.Select(s => s.GapSec));
        var pieces = steps.Where(s => !s.IsGap).Select(s => s.Piece!).ToList();
        Assert.Equal(new SpeechPiece(0, 1, 0), pieces[0]);
        Assert.Equal(new SpeechPiece(1.5, 2.5, 1.5), pieces[1]);   // stands where it stood: the pause was kept whole
        Assert.Equal(new SpeechPiece(4.5, 6, 3.5), pieces[2]);     // 2 s of pause became 1 s
    }

    [Fact]
    public void Plan_with_the_default_keep_is_what_it_was()
    {
        // every pause the detector reports (0.3 s and longer) is longer than the default 0.2 s
        var silence = new[] { new SilenceInterval(1.0, 1.3, 0.3), new SilenceInterval(2.5, 4.5, 2.0) };

        var steps = SpeechTimeMap.Plan(silence, 6, 0.2, 0.02);

        Assert.Equal(new[] { 0.0, 0.2, 0.0, 0.2, 0.0 }, steps.Select(s => s.GapSec));
        Assert.Equal(new[] { 0.0, 1.2, 2.6 }, steps.Where(s => !s.IsGap).Select(s => Math.Round(s.Piece!.OutputStartSec, 6)));
    }

    /// <summary>
    /// The compressed file itself, built by ffmpeg: its length is the plan's length. No-op without ffmpeg on PATH.
    /// </summary>
    [Fact]
    public void Compressed_file_has_the_length_of_the_plan_when_keep_is_longer_than_a_pause()
    {
        var audio = new AudioUtils();
        var ffmpeg = audio.WhichOr(null, "ffmpeg");
        var ffprobe = audio.WhichOr(null, "ffprobe");
        if (ffmpeg == null || ffprobe == null)
            return;

        var dir = Path.Combine(Path.GetTempPath(), "agent04_silence_gap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "source.wav");
            RunFfmpeg(ffmpeg, "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=16000:duration=6",
                "-ac", "1", "-acodec", "pcm_s16le", source);
            var silence = new[] { new SilenceInterval(1.0, 1.5, 0.5), new SilenceInterval(2.5, 4.5, 2.0) };

            var report = audio.WriteWavWithCompressedSilence(
                ffmpeg, ffprobe, source, Path.Combine(dir, "source_silc.wav"),
                new SilenceDetectOptions(), keepSilenceSec: 1.0, precomputedSilence: silence);

            // 1 + 0.5 (whole short pause) + 1 + 1.0 (the long pause cut down) + 1.5
            Assert.Equal(5.0, report.OutputDurationSec, 2);
            Assert.Equal(1.0, report.EstimatedShortenedSec, 6);
            Assert.Equal(3, report.SpeechPieces.Count);
            Assert.Equal(3.5, report.SpeechPieces[2].OutputStartSec, 6);
            Assert.Equal(report.OutputDurationSec, report.SpeechPieces[2].OutputEndSec, 2);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    /// <summary>The default pause (0.2 s): the file is as long as it always was. No-op without ffmpeg on PATH.</summary>
    [Fact]
    public void Compressed_file_with_the_default_keep_has_the_usual_length()
    {
        var audio = new AudioUtils();
        var ffmpeg = audio.WhichOr(null, "ffmpeg");
        var ffprobe = audio.WhichOr(null, "ffprobe");
        if (ffmpeg == null || ffprobe == null)
            return;

        var dir = Path.Combine(Path.GetTempPath(), "agent04_silence_gap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "source.wav");
            RunFfmpeg(ffmpeg, "-y", "-hide_banner", "-loglevel", "error",
                "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=16000:duration=6",
                "-ac", "1", "-acodec", "pcm_s16le", source);
            var silence = new[] { new SilenceInterval(1.0, 1.5, 0.5), new SilenceInterval(2.5, 4.5, 2.0) };

            var report = audio.WriteWavWithCompressedSilence(
                ffmpeg, ffprobe, source, Path.Combine(dir, "source_silc.wav"),
                new SilenceDetectOptions(), keepSilenceSec: 0.2, precomputedSilence: silence);

            // 1 + 0.2 + 1 + 0.2 + 1.5
            Assert.Equal(3.9, report.OutputDurationSec, 2);
            Assert.Equal(2.1, report.EstimatedShortenedSec, 6);
            Assert.Equal(2.4, report.SpeechPieces[2].OutputStartSec, 6);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Theory]
    [InlineData(200, 6400)]
    [InlineData(537, 17184)]
    [InlineData(1, 32)]
    public void Silence_file_is_a_16k_mono_wav_of_exactly_that_length(int durationMs, int dataBytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "agent04_gap_" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            AudioUtils.WriteGapSilenceWav(path, durationMs);

            var bytes = File.ReadAllBytes(path);
            Assert.Equal(44 + dataBytes, bytes.Length);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
            Assert.Equal(16000, BitConverter.ToInt32(bytes, 24));
            Assert.Equal(1, BitConverter.ToInt16(bytes, 22));
            Assert.Equal(16, BitConverter.ToInt16(bytes, 34));
            Assert.Equal(dataBytes, BitConverter.ToInt32(bytes, 40));
            Assert.All(bytes.Skip(44), b => Assert.Equal(0, b));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    private static void RunFfmpeg(string ffmpeg, params string[] args)
    {
        var psi = new ProcessStartInfo { FileName = ffmpeg, UseShellExecute = false, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg did not start");
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, "ffmpeg: " + err);
    }
}
