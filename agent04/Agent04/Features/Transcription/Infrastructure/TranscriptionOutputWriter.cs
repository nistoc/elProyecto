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

    public void AppendSegmentsToMarkdown(string mdPath, IReadOnlyList<ASRSegment> segments, double offset, double emitGuard)
    {
        using var fs = new FileStream(mdPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var w = new StreamWriter(fs);
        foreach (var seg in segments)
        {
            var isFallback = Math.Abs(seg.Start) < Eps && Math.Abs(seg.End) < Eps;
            if (!isFallback && (seg.Start + Eps) < emitGuard)
                continue;
            var speaker = NormalizeSpeaker(seg.Speaker);
            var timestamp = (seg.Start + offset).ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            var text = (seg.Text ?? "").Replace("\"", "\\\"");
            w.WriteLine($"- {timestamp} {speaker}: \"{text}\"");
        }
    }

    public void FinalizeMarkdown(string mdPath)
    {
        File.AppendAllText(mdPath, "<<<<<\n");
        _logger?.LogInformation("Finalized Markdown: {Path}", mdPath);
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
        var json = JsonSerializer.Serialize(combined, new JsonSerializerOptions { WriteIndented = true });
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
            var json = JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = true });
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

    private string NormalizeSpeaker(string? speakerLabel)
    {
        if (string.IsNullOrEmpty(speakerLabel))
            return "speaker_0";
        if (!_speakerMap.TryGetValue(speakerLabel, out var mapped))
        {
            mapped = "speaker_" + _speakerMap.Count;
            _speakerMap[speakerLabel] = mapped;
        }
        return mapped;
    }
}
