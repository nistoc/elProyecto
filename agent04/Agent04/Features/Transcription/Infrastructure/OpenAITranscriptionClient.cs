using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
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
    private int _transcriptionHttpInFlight;

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

                        var raw = await SendRequestAsync(audioPath, model, language, prompt, temperature, responseFormat, chunkingStrategy, options, cancellationToken);
                        var dict = RawToDictionary(raw);
                        var segments = ParseSegments(dict);
                        return new TranscriptionClientResult { RawResponse = dict, Segments = segments };
                    }
                    catch (Exception ex) when (cancellationToken.IsCancellationRequested && ex is OperationCanceledException)
                    {
                        _logger?.LogInformation(ex,
                            "Transcription cancelled (job/chunk token) on model {Model}; skipping remaining models and variations",
                            model);
                        throw;
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

                        if (!ShouldSuppressNonRetryableWarning(ex))
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

    private async Task<JsonElement> SendRequestAsync(
        string audioPath,
        string model,
        string? language,
        string? prompt,
        double? temperature,
        string? responseFormat,
        string? chunkingStrategy,
        TranscriptionClientOptions logContext,
        CancellationToken cancellationToken)
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

        var sw = Stopwatch.StartNew();
        var inFlight = Interlocked.Increment(ref _transcriptionHttpInFlight);
        var httpAttemptId = Guid.NewGuid();
        try
        {
            _logger?.LogInformation(
                "OpenAI transcription HTTP start HttpAttemptId={AttemptId} AgentJobId={JobId} ChunkIndex={Chunk} Model={Model} ParallelWorkersConfigured={Workers} InFlight={InFlight} File={File} Bytes={Bytes}",
                httpAttemptId,
                logContext.AgentJobId ?? "(none)",
                logContext.ChunkIndex,
                model,
                logContext.ParallelWorkersConfigured,
                inFlight,
                fileName,
                fileStream.Length);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            sw.Stop();
            if (!response.IsSuccessStatusCode)
            {
                var category = ClassifyStatus(response.StatusCode);
                var reason = string.IsNullOrWhiteSpace(response.ReasonPhrase) ? "" : response.ReasonPhrase.Trim();
                var detail = string.IsNullOrEmpty(reason)
                    ? TruncateForLog(body, 400)
                    : $"{reason} | {TruncateForLog(body, 360)}";
                _logger?.LogWarning(
                    "OpenAI transcription HTTP failed HttpAttemptId={AttemptId} AgentJobId={JobId} ChunkIndex={Chunk} Status={Status} Category={Category} DurationMs={Ms} InFlight={Flight} Detail={Detail}",
                    httpAttemptId,
                    logContext.AgentJobId ?? "(none)",
                    logContext.ChunkIndex,
                    (int)response.StatusCode,
                    category,
                    sw.ElapsedMilliseconds,
                    inFlight,
                    detail);
                throw new HttpRequestException($"{(int)response.StatusCode}: {body}");
            }

            _logger?.LogInformation(
                "OpenAI transcription HTTP OK HttpAttemptId={AttemptId} AgentJobId={JobId} ChunkIndex={Chunk} Status={Status} DurationMs={Ms} InFlightAfter={Flight}",
                httpAttemptId,
                logContext.AgentJobId ?? "(none)",
                logContext.ChunkIndex,
                (int)response.StatusCode,
                sw.ElapsedMilliseconds,
                inFlight);
            return JsonDocument.Parse(body).RootElement.Clone();
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            if (cancellationToken.IsCancellationRequested)
                _logger?.LogInformation(
                    "OpenAI transcription HTTP cancelled (token) HttpAttemptId={AttemptId} AgentJobId={JobId} ChunkIndex={Chunk} DurationMs={Ms}",
                    httpAttemptId,
                    logContext.AgentJobId ?? "(none)",
                    logContext.ChunkIndex,
                    sw.ElapsedMilliseconds);
            else
                _logger?.LogWarning(
                    "OpenAI transcription HTTP timeout HttpAttemptId={AttemptId} AgentJobId={JobId} ChunkIndex={Chunk} DurationMs={Ms} Category=timeout Reason=http_client_timeout",
                    httpAttemptId,
                    logContext.AgentJobId ?? "(none)",
                    logContext.ChunkIndex,
                    sw.ElapsedMilliseconds);
            throw;
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger?.LogWarning(ex,
                "OpenAI transcription HTTP error HttpAttemptId={AttemptId} AgentJobId={JobId} ChunkIndex={Chunk} DurationMs={Ms} Category=network",
                httpAttemptId,
                logContext.AgentJobId ?? "(none)",
                logContext.ChunkIndex,
                sw.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _transcriptionHttpInFlight);
        }
    }

    private static bool ShouldSuppressNonRetryableWarning(Exception ex)
    {
        if (ex is not OperationCanceledException) return false;
        for (var e = ex; e != null; e = e.InnerException)
        {
            if (e is TimeoutException) return true;
            if (e.Message.Contains("HttpClient.Timeout", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string ClassifyStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "auth",
        HttpStatusCode.TooManyRequests => "rate_limit",
        _ when (int)code >= 500 => "server_error",
        _ when (int)code >= 400 => "client_error",
        _ => "other"
    };

    private static string TruncateForLog(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var t = text.Replace("\r", " ").Replace("\n", " ");
        if (t.Length <= maxLen) return t;
        return t[..maxLen] + "…";
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
            var txt = JsonSerializer.Serialize(raw, TranscriptionJsonSerializerOptions.Compact);
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
