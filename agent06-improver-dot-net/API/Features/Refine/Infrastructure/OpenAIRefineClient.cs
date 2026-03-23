using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Infrastructure;

public sealed class OpenAIRefineClient : IOpenAIRefineClient
{
    private static readonly JsonSerializerOptions RequestJsonLogOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenAIRefineClient>? _logger;

    public OpenAIRefineClient(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<OpenAIRefineClient>? logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    public async Task<BatchResult> RefineBatchAsync(
        BatchInfo batchInfo,
        string model,
        float temperature,
        string systemPrompt,
        string userPromptTemplate,
        string? baseUrlOverride = null,
        string? debugLogArtifactRoot = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _config["OpenAiApiKey"] ?? _config["OpenAI:ApiKey"] ?? "";
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger?.LogWarning("OpenAI API key not configured");
            RefineDebugLog.Append(debugLogArtifactRoot, $"Batch {batchInfo.Index}: OpenAI API key not configured");
            return new BatchResult { BatchIndex = batchInfo.Index, FixedLines = batchInfo.Lines.ToList(), Success = false, Error = "OpenAI API key not configured" };
        }

        var baseUrl = (baseUrlOverride ?? _config["OpenAI:BaseUrl"] ?? "https://api.openai.com/").TrimEnd('/') + "/";
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(120);

        var userPrompt = RefinePromptComposer.BuildUserMessageContent(batchInfo, userPromptTemplate);

        var body = new
        {
            model,
            temperature,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        var requestJson = JsonSerializer.Serialize(body, RequestJsonLogOptions);

        try
        {
            var response = await client.PostAsJsonAsync("v1/chat/completions", body, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(cancellationToken);
                var detail = TryParseOpenAiErrorMessage(errBody) ?? errBody;
                var detailForLog = detail;
                if (detailForLog.Length > 4000)
                    detailForLog = detailForLog[..4000] + "…";
                _logger?.LogWarning(
                    "OpenAI HTTP {Status} for batch {Index}: {Detail}",
                    (int)response.StatusCode,
                    batchInfo.Index,
                    detailForLog);
                _logger?.LogWarning(
                    "OpenAI request JSON (batch {Index}):\n{RequestJson}",
                    batchInfo.Index,
                    requestJson);
                _logger?.LogWarning(
                    "OpenAI response body (batch {Index}):\n{ResponseBody}",
                    batchInfo.Index,
                    errBody);

                RefineDebugLog.Append(debugLogArtifactRoot, $"--- OpenAI HTTP error batch {batchInfo.Index} status {(int)response.StatusCode} ---");
                RefineDebugLog.AppendBlock(debugLogArtifactRoot, "Request JSON (full):", requestJson);
                RefineDebugLog.AppendBlock(debugLogArtifactRoot, "Response body (full):", errBody);

                if (detail.Length > 4000)
                    detail = detail[..4000] + "…";
                return new BatchResult
                {
                    BatchIndex = batchInfo.Index,
                    FixedLines = batchInfo.Lines.ToList(),
                    Success = false,
                    Error = $"OpenAI {(int)response.StatusCode}: {detail}"
                };
            }

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            var content = json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            var fixedText = content.Trim();
            var fixedLines = new List<string>();
            foreach (var line in fixedText.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                fixedLines.Add(line.TrimEnd() + (line.EndsWith('\n') ? "" : "\n"));
            }
            if (fixedLines.Count == 0 && batchInfo.Lines.Count > 0)
                fixedLines = batchInfo.Lines.ToList();

            return new BatchResult { BatchIndex = batchInfo.Index, FixedLines = fixedLines, Success = true };
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "OpenAI API call failed for batch {Index}", batchInfo.Index);
            _logger?.LogWarning(
                "OpenAI request JSON (batch {Index}):\n{RequestJson}",
                batchInfo.Index,
                requestJson);
            RefineDebugLog.Append(debugLogArtifactRoot, $"--- OpenAI exception batch {batchInfo.Index}: {ex.GetType().Name} ---");
            RefineDebugLog.AppendBlock(debugLogArtifactRoot, "Request JSON (full):", requestJson);
            RefineDebugLog.AppendBlock(debugLogArtifactRoot, "Exception:", ex.ToString());
            return new BatchResult { BatchIndex = batchInfo.Index, FixedLines = batchInfo.Lines.ToList(), Success = false, Error = ex.Message };
        }
    }

    private static string? TryParseOpenAiErrorMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                if (err.ValueKind == JsonValueKind.String)
                    return err.GetString();
                if (err.TryGetProperty("message", out var msg))
                    return msg.GetString();
            }
        }
        catch
        {
            /* not JSON */
        }

        return null;
    }

}
