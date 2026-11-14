using System.Text;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Writes the silence-compressed WAV (16 kHz, mono, 16 bit) out of the whole source decoded once to raw PCM:
/// every kept piece is copied to its place by sample positions and everything between the pieces is digital
/// silence. Positions come from <see cref="SpeechTimeMap.Plan"/>, each rounded on its own, so a piece stands
/// within half a sample of where the speech map says and nothing adds up over hundreds of pieces.
/// </summary>
internal static class SilenceCompressedWav
{
    public const int SampleRate = 16000;
    public const int BytesPerSample = 2;
    public const int HeaderBytes = 44;

    /// <summary>One kept piece in samples: where it is read from and where it is written to.</summary>
    public sealed record PieceSamples(long SourceStart, long OutputStart, long Count);

    public sealed record Layout(IReadOnlyList<PieceSamples> Pieces, long TotalSamples);

    public static long ToSamples(double seconds, int sampleRate = SampleRate) =>
        (long)Math.Round(seconds * sampleRate, MidpointRounding.AwayFromZero);

    /// <summary>Sample positions of the plan's pieces and the length of the whole file.</summary>
    public static Layout LayoutOf(IReadOnlyList<SpeechCompressionStep> steps, int sampleRate = SampleRate)
    {
        var pieces = new List<PieceSamples>();
        var endSec = 0.0;
        foreach (var step in steps)
        {
            if (step.Piece is not { } piece)
            {
                endSec += step.GapSec;
                continue;
            }

            var outputStart = ToSamples(piece.OutputStartSec, sampleRate);
            var count = ToSamples(piece.OutputEndSec, sampleRate) - outputStart;
            if (count > 0)
                pieces.Add(new PieceSamples(ToSamples(piece.SourceStartSec, sampleRate), outputStart, count));
            endSec = piece.OutputEndSec;
        }

        return new Layout(pieces, ToSamples(endSec, sampleRate));
    }

    /// <summary>
    /// Writes the header and the samples. A piece that reaches past the end of the source (the decoded audio
    /// can be a few samples shorter than the length ffprobe reports) is filled up with silence, so the file
    /// always has the length of the layout.
    /// </summary>
    /// <param name="sourcePcm">The whole source as raw 16 kHz mono 16-bit PCM.</param>
    public static void Write(Stream sourcePcm, Layout layout, Stream output, CancellationToken cancellationToken = default)
    {
        WriteHeader(output, layout.TotalSamples * BytesPerSample);

        var buffer = new byte[81920];
        long written = 0;
        long sourcePosition = 0;
        foreach (var piece in layout.Pieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (piece.OutputStart > written)
            {
                WriteSilence(output, piece.OutputStart - written, buffer);
                written = piece.OutputStart;
            }

            sourcePosition = MoveTo(sourcePcm, sourcePosition, piece.SourceStart, buffer);
            var want = Math.Min(piece.Count, layout.TotalSamples - written);
            var copied = sourcePosition == piece.SourceStart ? Copy(sourcePcm, output, want, buffer) : 0;
            sourcePosition += copied;
            if (copied < want)
                WriteSilence(output, want - copied, buffer);
            written += want;
        }

        if (layout.TotalSamples > written)
            WriteSilence(output, layout.TotalSamples - written, buffer);
    }

    /// <summary>Header of a PCM WAV: 16 kHz, mono, 16 bit.</summary>
    public static void WriteHeader(Stream output, long dataBytes, int sampleRate = SampleRate)
    {
        if (dataBytes < 0 || dataBytes > int.MaxValue - 36)
            throw new InvalidOperationException($"Audio of {dataBytes} bytes does not fit into a WAV file.");

        const short channels = 1;
        const short bitsPerSample = BytesPerSample * 8;
        const short blockAlign = channels * BytesPerSample;
        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write((int)(36 + dataBytes));
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);                       // size of the PCM format block
        writer.Write((short)1);                 // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * blockAlign);  // bytes per second
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write((int)dataBytes);
    }

    /// <summary>Moves the source to a sample position; returns where it stands afterwards.</summary>
    private static long MoveTo(Stream source, long position, long target, byte[] buffer)
    {
        if (target == position)
            return position;

        if (source.CanSeek)
        {
            var bytes = Math.Min(target * BytesPerSample, source.Length);
            source.Seek(bytes, SeekOrigin.Begin);
            return bytes == target * BytesPerSample ? target : bytes / BytesPerSample;
        }

        // A stream that cannot seek is read through; a position already passed cannot be reached.
        var left = (target - position) * BytesPerSample;
        while (left > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (read <= 0)
                break;
            left -= read;
        }

        return target - left / BytesPerSample;
    }

    /// <summary>Copies up to <paramref name="samples"/> samples; returns how many whole samples were there.</summary>
    private static long Copy(Stream source, Stream output, long samples, byte[] buffer)
    {
        var left = samples * BytesPerSample;
        while (left > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (read <= 0)
                break;
            output.Write(buffer, 0, read);
            left -= read;
        }

        var copiedBytes = samples * BytesPerSample - left;
        if (copiedBytes % BytesPerSample != 0)
        {
            // Half a sample at the very end of the source: finish it with a zero byte.
            output.WriteByte(0);
            copiedBytes++;
        }

        return copiedBytes / BytesPerSample;
    }

    private static void WriteSilence(Stream output, long samples, byte[] buffer)
    {
        Array.Clear(buffer);
        var left = samples * BytesPerSample;
        while (left > 0)
        {
            var n = (int)Math.Min(buffer.Length, left);
            output.Write(buffer, 0, n);
            left -= n;
        }
    }
}
