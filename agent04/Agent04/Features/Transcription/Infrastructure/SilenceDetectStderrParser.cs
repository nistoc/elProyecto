using System.Globalization;
using System.Text.RegularExpressions;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>Parses ffmpeg <c>silencedetect</c> lines from stderr.</summary>
public static partial class SilenceDetectStderrParser
{
    private static readonly Regex SilenceStartRegex = SilenceStartRegexImpl();
    private static readonly Regex SilenceEndRegex = SilenceEndRegexImpl();

    [GeneratedRegex(@"silence_start:\s*([0-9.+-eE]+)", RegexOptions.CultureInvariant)]
    private static partial Regex SilenceStartRegexImpl();

    [GeneratedRegex(@"silence_end:\s*([0-9.+-eE]+)(?:\s*\|\s*silence_duration:\s*([0-9.+-eE]+))?", RegexOptions.CultureInvariant)]
    private static partial Regex SilenceEndRegexImpl();

    public static IReadOnlyList<SilenceInterval> Parse(string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
            return Array.Empty<SilenceInterval>();

        var list = new List<SilenceInterval>();
        double? pendingStart = null;
        foreach (var raw in stderr.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var startMatch = SilenceStartRegex.Match(line);
            if (startMatch.Success && double.TryParse(startMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            {
                pendingStart = s;
                continue;
            }

            var endMatch = SilenceEndRegex.Match(line);
            if (!endMatch.Success || !pendingStart.HasValue) continue;
            if (!double.TryParse(endMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var end))
                continue;

            double duration;
            if (endMatch.Groups[2].Success && double.TryParse(endMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                duration = d;
            else
                duration = end - pendingStart.Value;

            if (end >= pendingStart.Value && duration >= 0)
                list.Add(new SilenceInterval(pendingStart.Value, end, duration));
            pendingStart = null;
        }

        return list;
    }
}
