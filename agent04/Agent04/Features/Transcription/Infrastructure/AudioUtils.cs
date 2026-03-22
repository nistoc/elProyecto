using System.Diagnostics;
using System.Globalization;
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
}
