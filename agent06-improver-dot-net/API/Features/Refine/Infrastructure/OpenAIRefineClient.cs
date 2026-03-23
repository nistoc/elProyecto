using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Infrastructure;

public sealed class OpenAIRefineClient : IOpenAIRefineClient
{
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
        CancellationToken cancellationToken = default)
    {
        var apiKey = _config["OpenAiApiKey"] ?? _config["OpenAI:ApiKey"] ?? "";
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger?.LogWarning("OpenAI API key not configured");
            return new BatchResult { BatchIndex = batchInfo.Index, FixedLines = batchInfo.Lines.ToList(), Success = false, Error = "OpenAI API key not configured" };
        }

        var baseUrl = (baseUrlOverride ?? _config["OpenAI:BaseUrl"] ?? "https://api.openai.com/").TrimEnd('/') + "/";
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(120);

        var contextText = batchInfo.Context != null && batchInfo.Context.Count > 0
            ? "Context from previous batch (for continuity):\n```\n" + string.Join("\n", batchInfo.Context.Select(l => l.TrimEnd())) + "\n```"
            : "No previous context available.";
        var batchText = string.Join("", batchInfo.Lines.Select(l => l.TrimEnd() + "\n"));
        var userPrompt = userPromptTemplate
            .Replace("{context}", contextText, StringComparison.Ordinal)
            .Replace("{batch}", batchText, StringComparison.Ordinal);

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

        try
        {
            var response = await client.PostAsJsonAsync("v1/chat/completions", body, cancellationToken);
            response.EnsureSuccessStatusCode();
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
            return new BatchResult { BatchIndex = batchInfo.Index, FixedLines = batchInfo.Lines.ToList(), Success = false, Error = ex.Message };
        }
    }

}
