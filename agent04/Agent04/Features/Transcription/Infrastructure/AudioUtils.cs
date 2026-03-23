using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Audio file utilities using ffmpeg/ffprobe (Process-based).
/// </summary>
public sealed class AudioUtils : IAudioUtils
{
    public string? WhichOr(string? pathKey, string defaultName)
    {
        var name = pathKey ?? defaultName;
        if (string.IsNullOrEmpty(name)) return null;
        if (!name.Contains(Path.DirectorySeparatorChar) && !name.Contains('/'))
            return Which(name);
        return name;
    }

    private static string? Which(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;
        var ext = OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT" : null;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), name);
            if (File.Exists(candidate)) return candidate;
            if (OperatingSystem.IsWindows() && !Path.HasExtension(candidate))
                foreach (var e in (ext ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
                    if (File.Exists(candidate + e)) return candidate + e;
        }
        return null;
    }

    public (double DurationSeconds, long SizeBytes) GetDurationAndSize(string ffprobePath, string filePath)
    {
        var size = new FileInfo(filePath).Length;
        double dur = 0.0;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobePath,
                ArgumentList = { "-v", "error", "-show_entries", "format=duration", "-of", "default=nw=1:nk=1", filePath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            if (p == null) return (0.0, size);
            var outStr = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode == 0 && double.TryParse(outStr.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                dur = d;
        }
        catch { /* ignore */ }
        return (dur, size);
    }

    public int CalculateSegmentTime(long sizeBytes, double durationSec, double targetMb)
    {
        if (durationSec <= 0) return 480;
        var targetBytes = (long)(targetMb * 1024 * 1024);
        var bytesPerSec = sizeBytes / Math.Max(durationSec, 0.001);
        return Math.Max(60, (int)((targetBytes * 0.97) / Math.Max(bytesPerSec, 1)));
    }

    public string ReencodeIfNeeded(string ffmpegPath, string inputPath, double targetMb, int bitrateKbps)
    {
        var sizeMb = new FileInfo(inputPath).Length / (1024.0 * 1024.0);
        if (sizeMb <= targetMb) return inputPath;

        var dir = Path.GetDirectoryName(inputPath) ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var ext = Path.GetExtension(inputPath).ToLowerInvariant();
        string outputPath;
        if (ext == ".wav")
        {
            outputPath = Path.Combine(dir, baseName + "_re.wav");
            RunFfmpeg(ffmpegPath, new[] { "-y", "-loglevel", "error", "-hide_banner", "-i", inputPath, "-ac", "1", "-ar", "16000", "-acodec", "pcm_s16le", outputPath });
        }
        else
        {
            outputPath = Path.Combine(dir, baseName + "_re.m4a");
            RunFfmpeg(ffmpegPath, new[] { "-y", "-loglevel", "error", "-hide_banner", "-i", inputPath, "-ac", "1", "-ar", "16000", "-b:a", $"{bitrateKbps}k", outputPath });
        }
        return outputPath;
    }

    public string FormatMb(long numBytes) => $"{numBytes / (1024.0 * 1024.0):F2} MB";

    public string ConvertToWav(string ffmpegPath, string inputPath, string? outputDir = null)
    {
        if (inputPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            return inputPath;

        string outputPath;
        if (!string.IsNullOrEmpty(outputDir))
        {
            Directory.CreateDirectory(outputDir);
            var baseName = Path.GetFileNameWithoutExtension(inputPath);
            outputPath = Path.Combine(outputDir, baseName + ".wav");
        }
        else
            outputPath = Path.ChangeExtension(inputPath, ".wav");

        if (File.Exists(outputPath))
            return outputPath;

        RunFfmpeg(ffmpegPath, new[] { "-y", "-loglevel", "error", "-hide_banner", "-i", inputPath, "-acodec", "pcm_s16le", "-ar", "16000", "-ac", "1", outputPath });
        return outputPath;
    }

    public void ExtractAudioSegmentCopy(string ffmpegPath, string inputPath, double startSeconds, double durationSeconds, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var ss = startSeconds.ToString("G", CultureInfo.InvariantCulture);
        var t = durationSeconds.ToString("G", CultureInfo.InvariantCulture);
        RunFfmpeg(ffmpegPath, new[]
        {
            "-y", "-loglevel", "error", "-hide_banner",
            "-ss", ss, "-i", inputPath,
            "-t", t,
            "-c", "copy",
            outputPath
        });
    }

    public void ExtractAudioSegmentReencodePcm16kMono(string ffmpegPath, string inputPath, double startSeconds, double durationSeconds, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var ss = startSeconds.ToString("G", CultureInfo.InvariantCulture);
        var t = durationSeconds.ToString("G", CultureInfo.InvariantCulture);
        RunFfmpeg(ffmpegPath, new[]
        {
            "-y", "-loglevel", "error", "-hide_banner",
            "-i", inputPath,
            "-ss", ss,
            "-t", t,
            "-acodec", "pcm_s16le",
            "-ar", "16000",
            outputPath
        });
    }

    public void ExtractAudioSegmentCopyOrReencode(string ffmpegPath, string inputPath, double startSeconds, double durationSeconds, string outputPath)
    {
        try
        {
            ExtractAudioSegmentCopy(ffmpegPath, inputPath, startSeconds, durationSeconds, outputPath);
        }
        catch (InvalidOperationException)
        {
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { /* ignore */ }
            }
            ExtractAudioSegmentReencodePcm16kMono(ffmpegPath, inputPath, startSeconds, durationSeconds, outputPath);
        }
    }

    private static void RunFfmpeg(string ffmpegPath, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p == null) throw new InvalidOperationException("Failed to start ffmpeg.");
        p.WaitForExit();
        if (p.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg exited with {p.ExitCode}: {p.StandardError.ReadToEnd()}");
    }

    public IReadOnlyList<SilenceInterval> DetectSilence(
        string ffmpegPath,
        string inputPath,
        SilenceDetectOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
            throw new FileNotFoundException("Audio file not found for silence detect", inputPath);

        var noise = options.NoiseDb.ToString(CultureInfo.InvariantCulture);
        var d = options.MinDurationSec.ToString("G", CultureInfo.InvariantCulture);
        var detection = options.UseRms ? "rms" : "peak";
        var afLegacy = $"silencedetect=noise={noise}dB:d={d}";
        var afWithDetection = $"{afLegacy}:detection={detection}";

        var (exitCode, stderr) = RunSilenceDetect(ffmpegPath, inputPath, afWithDetection, cancellationToken);
        if (exitCode != 0
            && stderr.Contains("silencedetect", StringComparison.OrdinalIgnoreCase)
            && stderr.Contains("Option not found", StringComparison.OrdinalIgnoreCase)
            && stderr.Contains("detection", StringComparison.OrdinalIgnoreCase))
        {
            (exitCode, stderr) = RunSilenceDetect(ffmpegPath, inputPath, afLegacy, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (exitCode != 0)
            throw new InvalidOperationException($"ffmpeg silencedetect exited with {exitCode}: {stderr}");

        return SilenceDetectStderrParser.Parse(stderr);
    }

    private static (int ExitCode, string Stderr) RunSilenceDetect(
        string ffmpegPath,
        string inputPath,
        string audioFilter,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-nostats");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("info");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(inputPath);
        psi.ArgumentList.Add("-af");
        psi.ArgumentList.Add(audioFilter);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("null");
        psi.ArgumentList.Add("-");

        using var p = Process.Start(psi);
        if (p == null) throw new InvalidOperationException("Failed to start ffmpeg.");

        using (cancellationToken.Register(() =>
        {
            try
            {
                if (!p.HasExited)
                    p.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }
        }))
        {
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return (p.ExitCode, stderr);
        }
    }

    public SilenceCompressionReport WriteWavWithCompressedSilence(
        string ffmpegPath,
        string ffprobePath,
        string inputPath,
        string outputWavPath,
        SilenceDetectOptions detectOptions,
        double keepSilenceSec,
        IReadOnlyList<SilenceInterval>? precomputedSilence = null,
        CancellationToken cancellationToken = default,
        IProgress<SilenceCompressionProgress>? silenceCompressionProgress = null)
    {
        if (string.IsNullOrEmpty(inputPath) || !File.Exists(inputPath))
            throw new FileNotFoundException("Audio file not found for silence compression", inputPath);

        var raw = precomputedSilence ?? DetectSilence(ffmpegPath, inputPath, detectOptions, cancellationToken);
        var intervals = NormalizeSilenceIntervals(raw);
        var (durationSeconds, _) = GetDurationAndSize(ffprobePath, inputPath);
        if (durationSeconds <= 0)
            throw new InvalidOperationException("ffprobe returned zero duration; cannot compress silence.");

        var outFull = Path.GetFullPath(outputWavPath);
        var inFull = Path.GetFullPath(inputPath);

        if (intervals.Count == 0)
            return FinishSilenceCompressionNoRegions(
                inputPath, outputWavPath, inFull, outFull, durationSeconds, keepSilenceSec, ffprobePath);

        var totalSilence = intervals.Sum(x => x.DurationSec);
        var shortened = totalSilence - intervals.Count * Math.Max(0, keepSilenceSec);
        var speechExtractCount = SilenceCompressionSegmentCounter.CountSpeechExtractions(intervals, durationSeconds);
        var progressTotalSteps = speechExtractCount + 1;

        var tempRoot = Path.Combine(Path.GetTempPath(), "agent04_silence_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var concatLines = CollectSpeechAndGapConcatLines(
                ffmpegPath,
                inputPath,
                tempRoot,
                intervals,
                durationSeconds,
                keepSilenceSec,
                cancellationToken,
                silenceCompressionProgress,
                progressTotalSteps);
            if (concatLines.Count == 0)
                throw new InvalidOperationException("Silence compression produced no audio segments.");

            var listPath = Path.Combine(tempRoot, "concat.txt");
            File.WriteAllText(listPath, string.Join(Environment.NewLine, concatLines) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var workOut = string.Equals(inFull, outFull, StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(tempRoot, "output.wav")
                : outFull;

            var dirOut = Path.GetDirectoryName(workOut);
            if (!string.IsNullOrEmpty(dirOut))
                Directory.CreateDirectory(dirOut);

            silenceCompressionProgress?.Report(new SilenceCompressionProgress(
                speechExtractCount,
                progressTotalSteps,
                durationSeconds,
                "concat"));

            RunFfmpeg(ffmpegPath, new[]
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-f", "concat", "-safe", "0",
                "-i", listPath,
                "-c", "copy",
                workOut
            });

            silenceCompressionProgress?.Report(new SilenceCompressionProgress(
                progressTotalSteps,
                progressTotalSteps,
                durationSeconds,
                "concat"));

            if (!string.Equals(workOut, outFull, StringComparison.OrdinalIgnoreCase))
                File.Move(workOut, outFull, overwrite: true);

            var (outputDuration, _) = GetDurationAndSize(ffprobePath, outFull);
            return new SilenceCompressionReport
            {
                InputPath = inputPath,
                OutputPath = outputWavPath,
                InputDurationSec = durationSeconds,
                OutputDurationSec = outputDuration,
                KeepSilenceSec = keepSilenceSec,
                SilenceRegions = intervals,
                TotalSilenceDurationSec = totalSilence,
                EstimatedShortenedSec = shortened,
                AppliedCompression = true
            };
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // ignore
            }
        }
    }

    private SilenceCompressionReport FinishSilenceCompressionNoRegions(
        string inputPath,
        string outputWavPath,
        string inFull,
        string outFull,
        double durationSeconds,
        double keepSilenceSec,
        string ffprobePath)
    {
        if (!string.Equals(inFull, outFull, StringComparison.OrdinalIgnoreCase))
        {
            var dir = Path.GetDirectoryName(outFull);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.Copy(inputPath, outputWavPath, overwrite: true);
        }

        var (outDur, _) = string.Equals(inFull, outFull, StringComparison.OrdinalIgnoreCase)
            ? (durationSeconds, 0L)
            : GetDurationAndSize(ffprobePath, outFull);
        return new SilenceCompressionReport
        {
            InputPath = inputPath,
            OutputPath = string.Equals(inFull, outFull, StringComparison.OrdinalIgnoreCase) ? inputPath : outputWavPath,
            InputDurationSec = durationSeconds,
            OutputDurationSec = outDur,
            KeepSilenceSec = keepSilenceSec,
            SilenceRegions = Array.Empty<SilenceInterval>(),
            TotalSilenceDurationSec = 0,
            EstimatedShortenedSec = 0,
            AppliedCompression = false
        };
    }

    private static List<string> CollectSpeechAndGapConcatLines(
        string ffmpegPath,
        string inputPath,
        string tempRoot,
        List<SilenceInterval> intervals,
        double durationSeconds,
        double keepSilenceSec,
        CancellationToken cancellationToken,
        IProgress<SilenceCompressionProgress>? silenceCompressionProgress,
        int progressTotalSteps)
    {
        var minSpeech = SilenceCompressionSegmentCounter.MinSpeechSegmentSec;
        var concatLines = new List<string>();
        var gapPath = Path.Combine(tempRoot, "gap.wav");
        if (keepSilenceSec > 1e-6)
            WriteGapSilenceWav(ffmpegPath, gapPath, keepSilenceSec);

        var segIndex = 0;
        var cursor = 0.0;
        var speechCompleted = 0;
        foreach (var iv in intervals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (iv.StartSec > cursor + 1e-9)
            {
                var speechDur = iv.StartSec - cursor;
                if (speechDur >= minSpeech)
                {
                    var seg = Path.Combine(tempRoot, $"seg_{segIndex++:D5}.wav");
                    ExtractPcm16kMonoWavSegment(ffmpegPath, inputPath, cursor, speechDur, seg);
                    concatLines.Add(FormatConcatDemuxerLine(seg));
                    speechCompleted++;
                    silenceCompressionProgress?.Report(new SilenceCompressionProgress(
                        speechCompleted,
                        progressTotalSteps,
                        iv.StartSec,
                        "segments"));
                }
            }

            if (keepSilenceSec > 1e-6)
                concatLines.Add(FormatConcatDemuxerLine(gapPath));

            cursor = iv.EndSec;
        }

        var tailDur = durationSeconds - cursor;
        if (tailDur >= minSpeech)
        {
            var seg = Path.Combine(tempRoot, $"seg_{segIndex++:D5}.wav");
            ExtractPcm16kMonoWavSegment(ffmpegPath, inputPath, cursor, tailDur, seg);
            concatLines.Add(FormatConcatDemuxerLine(seg));
            speechCompleted++;
            silenceCompressionProgress?.Report(new SilenceCompressionProgress(
                speechCompleted,
                progressTotalSteps,
                durationSeconds,
                "segments"));
        }

        return concatLines;
    }

    private static List<SilenceInterval> NormalizeSilenceIntervals(IReadOnlyList<SilenceInterval> raw)
    {
        if (raw.Count == 0)
            return new List<SilenceInterval>();

        var sorted = raw.OrderBy(x => x.StartSec).ToList();
        var merged = new List<SilenceInterval> { sorted[0] };
        for (var i = 1; i < sorted.Count; i++)
        {
            var cur = sorted[i];
            var last = merged[^1];
            if (cur.StartSec <= last.EndSec + 1e-9)
            {
                var end = Math.Max(last.EndSec, cur.EndSec);
                merged[^1] = new SilenceInterval(last.StartSec, end, end - last.StartSec);
            }
            else
                merged.Add(cur);
        }

        return merged;
    }

    private static string FormatConcatDemuxerLine(string path)
    {
        var p = Path.GetFullPath(path).Replace('\\', '/');
        p = p.Replace("'", "'\\''", StringComparison.Ordinal);
        return $"file '{p}'";
    }

    private static void WriteGapSilenceWav(string ffmpegPath, string outputPath, double keepSec)
    {
        var t = keepSec.ToString("G", CultureInfo.InvariantCulture);
        RunFfmpeg(ffmpegPath, new[]
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", "anullsrc=r=16000:cl=mono",
            "-t", t,
            "-acodec", "pcm_s16le", "-ar", "16000", "-ac", "1",
            outputPath
        });
    }

    private static void ExtractPcm16kMonoWavSegment(string ffmpegPath, string inputPath, double startSeconds, double durationSeconds, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var ss = startSeconds.ToString("G", CultureInfo.InvariantCulture);
        var dur = durationSeconds.ToString("G", CultureInfo.InvariantCulture);
        RunFfmpeg(ffmpegPath, new[]
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-i", inputPath,
            "-ss", ss,
            "-t", dur,
            "-acodec", "pcm_s16le",
            "-ar", "16000",
            "-ac", "1",
            outputPath
        });
    }
}
