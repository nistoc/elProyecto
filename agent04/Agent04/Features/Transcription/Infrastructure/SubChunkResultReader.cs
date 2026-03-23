using System.Text.Json;
using System.Text.Json.Serialization;
using Agent04.Features.Transcription.Domain;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>Loads <c>sub_chunk_XX_result.json</c> written by <see cref="SubChunkResultWriter"/>.</summary>
public static class SubChunkResultReader
{
    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static TranscriptionResult? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize<SubChunkFileDto>(json, ReadOpts);
            if (dto == null) return null;
            var segments = dto.Segments
                .Select(s => new ASRSegment(s.Start, s.End, s.Text ?? "", s.Speaker))
                .ToList();
            var raw = dto.RawResponse.ValueKind == JsonValueKind.Undefined
                ? new Dictionary<string, object?>()
                : JsonToObjectDict(dto.RawResponse);
            return new TranscriptionResult(
                string.IsNullOrEmpty(dto.ChunkBasename) ? Path.GetFileName(path) : dto.ChunkBasename,
                dto.Offset,
                dto.EmitGuard,
                segments,
                raw);
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyDictionary<string, object?> JsonToObjectDict(JsonElement el)
    {
        var d = new Dictionary<string, object?>();
        if (el.ValueKind != JsonValueKind.Object) return d;
        foreach (var p in el.EnumerateObject())
            d[p.Name] = JsonToObject(p.Value);
        return d;
    }

    private static object? JsonToObject(JsonElement e) =>
        e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetDouble(out var d) ? d : e.GetInt32(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => e.EnumerateArray().Select(JsonToObject).ToArray(),
            JsonValueKind.Object => JsonToObjectDict(e),
            _ => null
        };

    private sealed class SubChunkFileDto
    {
        [JsonPropertyName("sub_idx")]
        public int SubIdx { get; set; }

        [JsonPropertyName("chunk_basename")]
        public string ChunkBasename { get; set; } = "";

        [JsonPropertyName("offset")]
        public double Offset { get; set; }

        [JsonPropertyName("emit_guard")]
        public double EmitGuard { get; set; }

        [JsonPropertyName("segments")]
        public List<SubChunkSegmentDto> Segments { get; set; } = new();

        [JsonPropertyName("raw_response")]
        public JsonElement RawResponse { get; set; }
    }

    private sealed class SubChunkSegmentDto
    {
        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("speaker")]
        public string? Speaker { get; set; }
    }
}
