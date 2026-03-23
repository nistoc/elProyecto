using System.Text.Json;

namespace Agent04.Features.Transcription.Domain;

/// <summary>
/// Transcription job configuration (same contract as agent01 config).
/// Load from JSON; resolve env:VAR_NAME for sensitive values.
/// </summary>
public sealed class TranscriptionConfig
{
    private readonly Dictionary<string, object?> _config = new();

    public TranscriptionConfig(IReadOnlyDictionary<string, object?> config)
    {
        if (config != null)
            foreach (var kv in config)
                _config[kv.Key] = kv.Value;
    }

    public static async Task<TranscriptionConfig> FromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(path, cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, object?>();
        foreach (var p in doc.RootElement.EnumerateObject())
            dict[p.Name] = ParseValue(p.Value);
        ResolveEnvVars(dict);
        return new TranscriptionConfig(dict);
    }

    private static object? ParseValue(JsonElement e)
    {
        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetDouble(out var d) ? d : e.GetInt32(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => e.EnumerateArray().Select(ParseValue).ToArray(),
            JsonValueKind.Object => e.EnumerateObject().ToDictionary(x => x.Name, x => ParseValue(x.Value)),
            _ => null
        };
    }

    private static void ResolveEnvVars(Dictionary<string, object?> config)
    {
        foreach (var key in config.Keys.ToList())
        {
            var v = config[key];
            if (v is string s && s.StartsWith("env:", StringComparison.Ordinal))
                config[key] = Environment.GetEnvironmentVariable(s.Substring(4));
        }
    }

    public T? Get<T>(string key, T? defaultValue = default)
    {
        if (!_config.TryGetValue(key, out var v) || v == null)
            return defaultValue;
        if (v is T t)
            return t;

        var underlying = Nullable.GetUnderlyingType(typeof(T));
        if (underlying == typeof(int))
        {
            int? n = v switch
            {
                int i => i,
                long l => checked((int)l),
                double dv => (int)dv,
                _ => default(int?)
            };
            if (n.HasValue)
                return (T)(object)n;
        }

        if (typeof(T) == typeof(int) && v is double dbl)
            return (T)(object)(int)dbl;
        if (typeof(T) == typeof(int) && v is long l0)
            return (T)(object)checked((int)l0);
        if (typeof(T) == typeof(float) && v is double d2)
            return (T)(object)(float)d2;
        if (typeof(T) == typeof(bool) && v is double d3)
            return (T)(object)(d3 != 0);
        if (underlying == typeof(double) && v is int i1)
            return (T)(object)(double?)i1;
        if (underlying == typeof(double) && v is long l1)
            return (T)(object)(double?)l1;
        if (underlying == typeof(double) && v is double dRaw)
            return (T)(object)(double?)dRaw;
        if (underlying == typeof(bool) && v is bool b)
            return (T)(object)(bool?)b;
        return defaultValue;
    }

    public IReadOnlyList<string> GetFiles()
    {
        if (_config.TryGetValue("files", out var filesVal) && filesVal is object[] arr)
        {
            var list = arr.Select(x => x?.ToString()).Where(s => !string.IsNullOrEmpty(s)).Cast<string>().ToList();
            if (list.Count > 0) return list;
        }
        var single = Get<string>("file");
        return string.IsNullOrEmpty(single) ? Array.Empty<string>() : new[] { single };
    }

    public string? OpenAiApiKey => Get<string>("openai_api_key");
    public string Model => Get<string>("model") ?? "gpt-4o-transcribe-diarize";
    public string? Language => Get<string>("language");
    public double Temperature => Get<double?>("temperature") ?? 0.0;
    public bool PreSplit => Get<bool?>("pre_split") ?? true;
    public double ChunkOverlapSec => Get<double?>("chunk_overlap_sec") ?? 2.0;
    public double TargetChunkMb => Get<double?>("target_chunk_mb") ?? 24.5;
    public string SplitWorkdir => Get<string>("split_workdir") ?? "chunks";
    /// <summary>Root folder for operator split output (<c>split_chunks/chunk_N/sub_chunks</c> under job artifacts).</summary>
    public string SplitChunksDir => Get<string>("split_chunks_dir") ?? "split_chunks";
    public string CacheDir => Get<string>("cache_dir") ?? "cache";
    public string? FfmpegPath => Get<string>("ffmpeg_path");
    public string? FfprobePath => Get<string>("ffprobe_path");
    public string MdOutputPath => Get<string>("md_output_path") ?? "transcript.md";
    public string RawJsonOutputPath => Get<string>("raw_json_output_path") ?? "openai_response.json";
}
