using System.Text.Json;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

public sealed class TranscriptionOutputWriter : ITranscriptionOutputWriter
{
    private readonly Dictionary<string, string> _speakerMap = new();
    private readonly ILogger<TranscriptionOutputWriter>? _logger;

    private const double Eps = 1e-3;

    public TranscriptionOutputWriter(ILogger<TranscriptionOutputWriter>? logger = null)
    {
        _logger = logger;
    }

    public void InitializeMarkdown(string mdPath)
    {
        var dir = Path.GetDirectoryName(mdPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(mdPath, ">>>>>>>\n");
    }

    public void AppendSegmentsToMarkdown(string mdPath, IReadOnlyList<ASRSegment> segments, double offset, double emitGuard, SpeechTimeMap? timeMap = null)
    {
        using var fs = new FileStream(mdPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var w = new StreamWriter(fs);
        foreach (var seg in segments)
        {
            var isFallback = Math.Abs(seg.Start) < Eps && Math.Abs(seg.End) < Eps;
            if (!isFallback && (seg.Start + Eps) < emitGuard)
                continue;
            var speaker = NormalizeSpeaker(seg.Speaker);
            var timestamp = FormatTimestamp(seg, offset, isFallback, timeMap);
            var text = (seg.Text ?? "").Replace("\"", "\\\"");
            w.WriteLine($"- {timestamp} {speaker}: \"{text}\"");
        }
    }

    /// <summary>
    /// Time of a line on the recording timeline. A fallback segment has no time of its own and stands at the
    /// start of its chunk; with a <paramref name="timeMap"/> both kinds are translated from the compressed file.
    /// </summary>
    internal static string FormatTimestamp(ASRSegment seg, double offset, bool isFallback, SpeechTimeMap? timeMap)
    {
        var time = seg.Start + offset;
        if (timeMap != null)
            time = isFallback ? timeMap.ToSourceTime(time) : timeMap.ToSourceSegmentStart(time, seg.End + offset);
        return time.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    }

    public void FinalizeMarkdown(string mdPath)
    {
        File.AppendAllText(mdPath, "<<<<<\n");
        _logger?.LogInformation("Finalized Markdown: {Path}", mdPath);
    }

    public void WritePerChunkMarkdown(string chunkBasename, IReadOnlyList<ASRSegment> segments, double offset, double emitGuard, string outputDir, SpeechTimeMap? timeMap = null)
    {
        Directory.CreateDirectory(outputDir);
        var safeBase = Path.GetFileNameWithoutExtension(chunkBasename);
        var outPath = Path.Combine(outputDir, safeBase + ".md");
        // Local speaker map so each chunk file starts at speaker_0 (independent of other chunks and thread-safe).
        var localMap = new Dictionary<string, string>();
        using var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var w = new StreamWriter(fs);
        w.Write(">>>>>>>\n");
        foreach (var seg in segments)
        {
            var isFallback = Math.Abs(seg.Start) < Eps && Math.Abs(seg.End) < Eps;
            if (!isFallback && (seg.Start + Eps) < emitGuard)
                continue;
            var speaker = NormalizeSpeakerInto(localMap, seg.Speaker);
            var timestamp = FormatTimestamp(seg, offset, isFallback, timeMap);
            var text = (seg.Text ?? "").Replace("\"", "\\\"");
            w.Write($"- {timestamp} {speaker}: \"{text}\"\n");
        }
        w.Write("<<<<<\n");
    }

    public void SaveCombinedJson(string jsonPath, IReadOnlyList<TranscriptionResult> results)
    {
        var dir = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        var combined = new
        {
            chunks = results.Select(r => new
            {
                chunk = r.ChunkBasename,
                offset = r.Offset,
                emit_guard = r.EmitGuard,
                response = r.RawResponse
            }).ToList()
        };
        var json = JsonSerializer.Serialize(combined, TranscriptionJsonSerializerOptions.Indented);
        File.WriteAllText(jsonPath, json);
        _logger?.LogInformation("Saved combined raw JSON to: {Path}", jsonPath);
    }

    public void SavePerChunkJson(string chunkBasename, IReadOnlyDictionary<string, object?> response, string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        var safeBase = Path.GetFileNameWithoutExtension(chunkBasename);
        var outPath = Path.Combine(outputDir, safeBase + ".json");
        try
        {
            var json = JsonSerializer.Serialize(response, TranscriptionJsonSerializerOptions.Indented);
            File.WriteAllText(outPath, json);
            _logger?.LogInformation("Saved per-chunk JSON: {Path}", outPath);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to save per-chunk JSON for {Chunk}", chunkBasename);
        }
    }

    public void ResetSpeakerMap()
    {
        _speakerMap.Clear();
    }

    private string NormalizeSpeaker(string? speakerLabel) => NormalizeSpeakerInto(_speakerMap, speakerLabel);

    private static string NormalizeSpeakerInto(Dictionary<string, string> map, string? speakerLabel)
    {
        if (string.IsNullOrEmpty(speakerLabel))
            return "speaker_0";
        if (!map.TryGetValue(speakerLabel, out var mapped))
        {
            mapped = "speaker_" + map.Count;
            map[speakerLabel] = mapped;
        }
        return mapped;
    }
}
