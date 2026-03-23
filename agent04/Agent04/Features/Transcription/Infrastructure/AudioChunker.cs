using System.Diagnostics;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Handles audio file splitting with overlap support (ffmpeg).
/// </summary>
public sealed class AudioChunker : IAudioChunker
{
    private readonly IAudioUtils _utils;
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;

    public AudioChunker(IAudioUtils utils, string ffmpegPath, string ffprobePath)
    {
        _utils = utils;
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath;
    }

    public IReadOnlyList<ChunkInfo> SliceWithOverlap(
        string sourcePath,
        int segmentTimeSeconds,
        double overlapSec,
        string workdir,
        string namingPattern,
        double maxDurationSec = 0,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(workdir);
        var (dur, _) = _utils.GetDurationAndSize(_ffprobePath, sourcePath);
        if (maxDurationSec > 0)
            dur = Math.Min(dur, maxDurationSec);

        var overlap = Math.Max(0.0, Math.Min(overlapSec, Math.Max(0.0, segmentTimeSeconds - 0.5)));
        var stride = Math.Max(1.0, segmentTimeSeconds - overlap);
        var baseNoExt = Path.GetFileNameWithoutExtension(sourcePath);
        var pattern = namingPattern.Replace("{base}", baseNoExt);
        pattern = AudioChunkNaming.AlignOutputExtensionWithSource(pattern, sourcePath);
        var outPattern = Path.Combine(workdir, Path.GetFileName(pattern));
        var estCount = (int)Math.Ceiling(Math.Max(1.0, dur) / stride);
        var pad = Math.Max(3, (int)Math.Ceiling(Math.Log10(Math.Max(1, estCount + 1))));

        var chunkInfos = new List<ChunkInfo>();
        var t = 0.0;
        var idx = 0;

        while (t < dur - 0.25)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var winDur = Math.Min(segmentTimeSeconds, Math.Max(0.0, dur - t));
            var outPath = outPattern.Replace("%03d", idx.ToString().PadLeft(pad, '0'));

            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                ArgumentList = { "-y", "-loglevel", "error", "-hide_banner", "-i", sourcePath, "-ss", $"{t:F3}", "-t", $"{winDur:F3}", "-c", "copy", outPath },
                UseShellExecute = false,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null) throw new InvalidOperationException("Failed to start ffmpeg.");
            p.WaitForExit();
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"ffmpeg failed: {p.StandardError.ReadToEnd()}");

            var emitGuard = idx == 0 ? 0.0 : overlap;
            chunkInfos.Add(new ChunkInfo(outPath, t, emitGuard, null));
            idx++;
            t += stride;
        }

        return chunkInfos.OrderBy(c => c.Offset).ToList();
    }

    public async Task<IReadOnlyList<ChunkInfo>> ProcessChunksForFileAsync(
        string sourcePath,
        double targetMb,
        string workdir,
        string namingPattern,
        double overlapSec,
        bool reencode = true,
        int reencodeBitrateKbps = 256,
        double maxDurationMinutes = 0,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Source file not found.", sourcePath);

        var (dur, size) = _utils.GetDurationAndSize(_ffprobePath, sourcePath);
        var maxDurationSec = maxDurationMinutes > 0 ? maxDurationMinutes * 60 : 0.0;
        if (maxDurationSec > 0 && dur > maxDurationSec)
            dur = maxDurationSec;

        var targetBytes = (long)(targetMb * 1024 * 1024);
        if (size <= targetBytes && maxDurationMinutes == 0)
            return new[] { new ChunkInfo(sourcePath, 0.0, 0.0, null) };

        var segTime = _utils.CalculateSegmentTime(size, dur, targetMb);
        var chunks = SliceWithOverlap(sourcePath, segTime, overlapSec, workdir, namingPattern, maxDurationSec, cancellationToken);

        if (!reencode)
            return chunks;

        var result = new List<ChunkInfo>();
        foreach (var c in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var newPath = _utils.ReencodeIfNeeded(_ffmpegPath, c.Path, targetMb, reencodeBitrateKbps);
            result.Add(new ChunkInfo(newPath, c.Offset, c.EmitGuard, c.Fingerprint));
        }
        return await Task.FromResult(result).ConfigureAwait(false);
    }
}
