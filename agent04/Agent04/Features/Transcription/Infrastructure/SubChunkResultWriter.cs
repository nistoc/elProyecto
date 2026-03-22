using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Agent04.Features.Transcription.Domain;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Writes <c>sub_chunk_XX_result.json</c> next to agent01 split CLI (see <c>cli/split.py</c>).
/// </summary>
public static class SubChunkResultWriter
{
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void Save(string resultsDir, int subIndex, TranscriptionResult result)
    {
        Directory.CreateDirectory(resultsDir);
        var path = Path.Combine(resultsDir, $"sub_chunk_{subIndex:D2}_result.json");
        var dto = new SubChunkResultDto
        {
            SubIdx = subIndex,
            ChunkBasename = result.ChunkBasename,
            Offset = result.Offset,
            EmitGuard = result.EmitGuard,
            Segments = result.Segments.Select(s => new SubChunkSegmentDto
            {
                Start = s.Start,
                End = s.End,
                Text = s.Text,
                Speaker = s.Speaker
            }).ToList(),
            RawResponse = JsonSerializer.SerializeToElement(result.RawResponse, TranscriptionJsonSerializerOptions.Compact)
        };
        var json = JsonSerializer.Serialize(dto, WriteOpts);
        File.WriteAllText(path, json);
    }

    private sealed class SubChunkResultDto
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
        public string Text { get; set; } = "";

        [JsonPropertyName("speaker")]
        public string? Speaker { get; set; }
    }
}
