using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

/// <summary>
/// The silence-compressed file written from one decode of the source: every kept piece stands where the
/// speech map says, everything between the pieces is digital silence.
/// </summary>
public sealed class SilenceCompressedWavTests
{
    private const int Rate = SilenceCompressedWav.SampleRate;

    /// <summary>Source where sample <c>i</c> holds <c>i % 30000 + 1</c>: never zero, so a copied sample tells where it came from.</summary>
    private static byte[] SourcePcm(int samples)
    {
        var bytes = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2, 2), (short)(i % 30000 + 1));
        return bytes;
    }

    private static short[] Samples(byte[] wav)
    {
        var data = wav.AsSpan(SilenceCompressedWav.HeaderBytes);
        var samples = new short[data.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = BitConverter.ToInt16(data.Slice(i * 2, 2));
        return samples;
    }

    private static short[] Compress(
        IReadOnlyList<SilenceInterval> silence,
        double sourceSec,
        double keep,
        int? sourceSamples = null,
        bool seekable = true)
    {
        var steps = SpeechTimeMap.Plan(silence, sourceSec, keep, 0.02);
        var pcm = SourcePcm(sourceSamples ?? (int)Math.Round(sourceSec * Rate));
        using Stream source = seekable ? new MemoryStream(pcm) : new ForwardOnlyStream(pcm);
        using var output = new MemoryStream();
        SilenceCompressedWav.Write(source, SilenceCompressedWav.LayoutOf(steps), output);
        return Samples(output.ToArray());
    }

    private static short SourceValue(long sample) => (short)(sample % 30000 + 1);

    [Fact]
    public void Layout_puts_every_piece_where_the_plan_puts_it()
    {
        // speech 0–1, pause 0.5 s, speech 1.5–2.5, pause 2 s, speech 4.5–6; a pause keeps at most 1 s
        var silence = new[] { new SilenceInterval(1.0, 1.5, 0.5), new SilenceInterval(2.5, 4.5, 2.0) };

        var layout = SilenceCompressedWav.LayoutOf(SpeechTimeMap.Plan(silence, 6, 1.0, 0.02));

        Assert.Equal(
            new[]
            {
                new SilenceCompressedWav.PieceSamples(0, 0, 16000),
                new SilenceCompressedWav.PieceSamples(24000, 24000, 16000),
                new SilenceCompressedWav.PieceSamples(72000, 56000, 24000),
            },
            layout.Pieces);
        Assert.Equal(80000, layout.TotalSamples);
    }

    [Fact]
    public void File_holds_the_pieces_of_the_source_and_silence_between_them()
    {
        var silence = new[] { new SilenceInterval(1.0, 1.5, 0.5), new SilenceInterval(2.5, 4.5, 2.0) };

        var samples = Compress(silence, 6, 1.0);

        Assert.Equal(80000, samples.Length);
        // piece 1: source 0–1 s at 0
        Assert.Equal(SourceValue(0), samples[0]);
        Assert.Equal(SourceValue(15999), samples[15999]);
        // the short pause stays whole, as silence
        Assert.All(samples[16000..24000], x => Assert.Equal(0, x));
        // piece 2: source 1.5–2.5 s at 1.5 s
        Assert.Equal(SourceValue(24000), samples[24000]);
        Assert.Equal(SourceValue(39999), samples[39999]);
        // the long pause is 1 s of silence
        Assert.All(samples[40000..56000], x => Assert.Equal(0, x));
        // piece 3: source 4.5–6 s at 3.5 s
        Assert.Equal(SourceValue(72000), samples[56000]);
        Assert.Equal(SourceValue(95999), samples[79999]);
        Assert.DoesNotContain((short)0, samples[56000..80000]);
    }

    [Fact]
    public void Header_is_a_16k_mono_16_bit_wav_of_the_layout_length()
    {
        var steps = SpeechTimeMap.Plan(new[] { new SilenceInterval(1.0, 3.0, 2.0) }, 4, 0.2, 0.02);
        using var output = new MemoryStream();

        SilenceCompressedWav.Write(new MemoryStream(SourcePcm(64000)), SilenceCompressedWav.LayoutOf(steps), output);

        var bytes = output.ToArray();
        var dataBytes = (16000 + 3200 + 16000) * 2;
        Assert.Equal(44 + dataBytes, bytes.Length);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(36 + dataBytes, BitConverter.ToInt32(bytes, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.Equal(1, BitConverter.ToInt16(bytes, 20));      // PCM
        Assert.Equal(1, BitConverter.ToInt16(bytes, 22));      // mono
        Assert.Equal(16000, BitConverter.ToInt32(bytes, 24));
        Assert.Equal(32000, BitConverter.ToInt32(bytes, 28));  // bytes per second
        Assert.Equal(2, BitConverter.ToInt16(bytes, 32));
        Assert.Equal(16, BitConverter.ToInt16(bytes, 34));
        Assert.Equal("data", System.Text.Encoding.ASCII.GetString(bytes, 36, 4));
        Assert.Equal(dataBytes, BitConverter.ToInt32(bytes, 40));
    }

    [Fact]
    public void Hundreds_of_pieces_do_not_drift_from_the_speech_map()
    {
        // 300 pieces of an awkward length with awkward pauses: rounding one piece at a time must not add up
        var silence = new List<SilenceInterval>();
        var t = 0.0;
        for (var i = 0; i < 300; i++)
        {
            t += 0.33337 + i * 0.00011;
            var pause = 0.41003 + (i % 7) * 0.0707;
            silence.Add(new SilenceInterval(t, t + pause, pause));
            t += pause;
        }

        var sourceSec = t + 0.5;
        var steps = SpeechTimeMap.Plan(silence, sourceSec, 0.2, 0.02);
        var pieces = steps.Where(s => !s.IsGap).Select(s => s.Piece!).ToList();
        var samples = Compress(silence, sourceSec, 0.2);

        Assert.Equal(301, pieces.Count);
        foreach (var piece in pieces)
        {
            var at = SilenceCompressedWav.ToSamples(piece.OutputStartSec);
            var from = SilenceCompressedWav.ToSamples(piece.SourceStartSec);
            Assert.Equal(SourceValue(from), samples[at]);
            // right before a piece stands the silence of the pause, never sound of another place
            if (at > 0)
                Assert.Equal(0, samples[at - 1]);
        }

        Assert.Equal(SilenceCompressedWav.ToSamples(pieces[^1].OutputEndSec), samples.Length);
    }

    [Fact]
    public void Pause_at_the_end_is_kept_as_silence()
    {
        var samples = Compress(new[] { new SilenceInterval(1.0, 4.0, 3.0) }, 4, 1.0);

        Assert.Equal(32000, samples.Length);
        Assert.Equal(SourceValue(15999), samples[15999]);
        Assert.All(samples[16000..], x => Assert.Equal(0, x));
    }

    [Fact]
    public void Nothing_is_left_of_a_pause_when_keep_is_zero()
    {
        var samples = Compress(new[] { new SilenceInterval(1.0, 3.0, 2.0) }, 4, 0);

        Assert.Equal(32000, samples.Length);
        Assert.Equal(SourceValue(15999), samples[15999]);
        Assert.Equal(SourceValue(48000), samples[16000]);
        Assert.DoesNotContain((short)0, samples);
    }

    [Fact]
    public void Source_shorter_than_ffprobe_said_is_filled_up_with_silence()
    {
        // the decoded audio ends 100 samples before the length the plan was made for
        var silence = new[] { new SilenceInterval(1.0, 2.0, 1.0) };

        var samples = Compress(silence, 3, 0.2, sourceSamples: 48000 - 100);

        Assert.Equal(16000 + 3200 + 16000, samples.Length);
        Assert.Equal(SourceValue(32000), samples[19200]);
        Assert.Equal(SourceValue(47899), samples[19200 + 15899]);
        Assert.All(samples[(19200 + 15900)..], x => Assert.Equal(0, x));
    }

    [Fact]
    public void Source_that_ends_before_a_piece_gives_silence_of_the_planned_length()
    {
        var silence = new[] { new SilenceInterval(1.0, 2.0, 1.0) };

        var samples = Compress(silence, 3, 0.2, sourceSamples: 20000);

        Assert.Equal(16000 + 3200 + 16000, samples.Length);
        Assert.Equal(SourceValue(15999), samples[15999]);
        Assert.All(samples[16000..], x => Assert.Equal(0, x));
    }

    [Fact]
    public void Source_that_cannot_seek_is_read_through()
    {
        var silence = new[] { new SilenceInterval(1.0, 1.5, 0.5), new SilenceInterval(2.5, 4.5, 2.0) };

        Assert.Equal(Compress(silence, 6, 1.0), Compress(silence, 6, 1.0, seekable: false));
    }

    [Fact]
    public void Cancel_stops_the_writing()
    {
        var steps = SpeechTimeMap.Plan(new[] { new SilenceInterval(1.0, 2.0, 1.0) }, 3, 0.2, 0.02);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => SilenceCompressedWav.Write(
            new MemoryStream(SourcePcm(48000)), SilenceCompressedWav.LayoutOf(steps), new MemoryStream(), cts.Token));
    }

    private sealed class ForwardOnlyStream : Stream
    {
        private readonly MemoryStream _inner;

        public ForwardOnlyStream(byte[] bytes) => _inner = new MemoryStream(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        // Short reads, as a pipe gives them.
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 4097));
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
