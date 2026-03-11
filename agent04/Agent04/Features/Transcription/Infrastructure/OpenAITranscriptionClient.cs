using System.Net.Http.Headers;
using System.Text.Json;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

public sealed class OpenAITranscriptionClient : ITranscriptionClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly IReadOnlyList<string> _fallbackModels;
    private readonly ILogger<OpenAITranscriptionClient>? _logger;

    public OpenAITranscriptionClient(
        HttpClient httpClient,
        string apiKey,
        string model,
        IReadOnlyList<string>? fallbackModels = null,
        ILogger<OpenAITranscriptionClient>? logger = null)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _model = model;
        _fallbackModels = fallbackModels ?? new[] { "gpt-4o-mini-transcribe", "whisper-1" };
        _logger = logger;
    }

    public async Task<TranscriptionClientResult> TranscribeAsync(string audioPath, TranscriptionClientOptions options, CancellationToken cancellationToken = default)
    {
        var modelsToTry = new List<string> { _model };
        foreach (var m in _fallbackModels)
            if (m != _model) modelsToTry.Add(m);

        Exception? lastErr = null;
        var isServer500 = false;

        foreach (var model in modelsToTry)
        {
            var variations = BuildParamVariations(options, model);
            foreach (var (language, prompt, temperature, responseFormat, chunkingStrategy) in variations)
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        if (attempt > 0)
                            _logger?.LogInformation("Retry attempt {Attempt}/3 for model {Model}", attempt + 1, model);

                        var raw = await SendRequestAsync(audioPath, model, language, prompt, temperature, responseFormat, chunkingStrategy, cancellationToken);
                        var dict = RawToDictionary(raw);
                        var segments = ParseSegments(dict);
                        return new TranscriptionClientResult { RawResponse = dict, Segments = segments };
                    }
                    catch (Exception ex)
                    {
                        lastErr = ex;
                        var msg = ex.ToString();
                        if (msg.Contains("500", StringComparison.Ordinal) || msg.Contains("InternalServerError", StringComparison.Ordinal))
                        {
                            isServer500 = true;
                            if (attempt < 2)
                            {
                                var delay = Math.Min(1 << attempt, 4) * 1000;
                                await Task.Delay(delay, cancellationToken);
                                continue;
                            }
                        }
                        _logger?.LogWarning(ex, "Non-retryable error on model {Model}", model);
                        break;
                    }
                }
            }
            _logger?.LogInformation("Model {Model} failed; trying next fallback", model);
        }

        if (isServer500 && lastErr != null)
            throw new InvalidOperationException($"Server 500 errors: {lastErr.Message}", lastErr);
        throw new InvalidOperationException("Transcription failed for all models", lastErr);
    }

    private async Task<JsonElement> SendRequestAsync(string audioPath, string model, string? language, string? prompt, double? temperature, string? responseFormat, string? chunkingStrategy, CancellationToken cancellationToken)
    {
        using var content = new MultipartFormDataContent();
        await using var fileStream = File.OpenRead(audioPath);
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var fileName = Path.GetFileName(audioPath);
        content.Add(fileContent, "file", fileName);

        content.Add(new StringContent(model), "model");
        if (!string.IsNullOrEmpty(language)) content.Add(new StringContent(language), "language");
        if (!string.IsNullOrEmpty(prompt)) content.Add(new StringContent(prompt), "prompt");
        if (temperature.HasValue) content.Add(new StringContent(temperature.Value.ToString("G", System.Globalization.CultureInfo.InvariantCulture)), "temperature");
        if (!string.IsNullOrEmpty(responseFormat)) content.Add(new StringContent(responseFormat), "response_format");
        if (!string.IsNullOrEmpty(chunkingStrategy)) content.Add(new StringContent(chunkingStrategy), "chunking_strategy");

        var request = new HttpRequestMessage(HttpMethod.Post, "v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Content = content;

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode}: {body}");

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static List<(string? language, string? prompt, double? temperature, string? responseFormat, string? chunkingStrategy)> BuildParamVariations(TranscriptionClientOptions options, string model)
    {
        var list = new List<(string?, string?, double?, string?, string?)>();
        var lang = options.Language;
        var prompt = options.Prompt;
        var temp = options.Temperature;
        var fmt = options.ResponseFormat;
        var chunk = options.ChunkingStrategy ?? (model.Contains("diarize", StringComparison.OrdinalIgnoreCase) ? "auto" : null);

        if (model.Contains("diarize", StringComparison.OrdinalIgnoreCase))
            prompt = null;

        list.Add((lang, prompt, temp, fmt, chunk));
        if (!string.IsNullOrEmpty(fmt))
            list.Add((lang, prompt, temp, null, chunk));
        if (model.Contains("diarize", StringComparison.OrdinalIgnoreCase))
        {
            list.Add((lang, prompt, temp, fmt, "none"));
            list.Add((lang, prompt, temp, null, "none"));
        }
        list.Add((lang, prompt, temp, null, null));
        return list;
    }

    private static IReadOnlyDictionary<string, object?> RawToDictionary(JsonElement el)
    {
        var d = new Dictionary<string, object?>();
        if (el.ValueKind != JsonValueKind.Object) return d;
        foreach (var p in el.EnumerateObject())
            d[p.Name] = ToObject(p.Value);
        return d;
    }

    private static object? ToObject(JsonElement e)
    {
        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.TryGetDouble(out var d) ? d : e.GetInt32(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => e.EnumerateArray().Select(ToObject).ToArray(),
            JsonValueKind.Object => RawToDictionary(e),
            _ => null
        };
    }

    public static IReadOnlyList<ASRSegment> ParseSegments(IReadOnlyDictionary<string, object?> raw)
    {
        var segments = new List<ASRSegment>();
        if (raw.TryGetValue("segments", out var segVal) && segVal is object[] arr)
        {
            foreach (var s in arr)
            {
                if (s is not IReadOnlyDictionary<string, object?> seg)
                    continue;
                var start = GetDouble(seg, "start", 0);
                var end = Math.Max(start, GetDouble(seg, "end", start));
                var text = (GetString(seg, "text") ?? "").Trim();
                var speaker = GetString(seg, "speaker") ?? GetString(seg, "speaker_label");
                segments.Add(new ASRSegment(start, end, text, speaker));
            }
        }
        else if (raw.TryGetValue("text", out var textVal))
        {
            var txt = (textVal?.ToString() ?? "").Trim();
            if (txt.Length > 0)
                segments.Add(new ASRSegment(0, 0, txt, null));
        }
        else
        {
            var txt = JsonSerializer.Serialize(raw);
            segments.Add(new ASRSegment(0, 0, txt, null));
        }
        return segments;
    }

    private static double GetDouble(IReadOnlyDictionary<string, object?> d, string key, double def)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return def;
        if (v is double dbl) return Math.Max(0, dbl);
        if (v is int i) return Math.Max(0, i);
        return double.TryParse(v.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? Math.Max(0, parsed) : def;
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return null;
        return v.ToString();
    }
}
