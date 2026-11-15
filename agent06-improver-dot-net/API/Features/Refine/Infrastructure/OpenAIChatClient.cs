using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// One chat completion against OpenAI or a compatible endpoint: the request, the answer's text and the tokens
/// the API says it billed. The key comes from the configuration only, never from a request.
/// </summary>
public sealed class OpenAIChatClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    public OpenAIChatClient(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    /// <param name="Content">The text of the first choice; empty when the answer has none.</param>
    /// <param name="PromptTokens">As the API reports them; 0 when the answer has no usage block.</param>
    /// <param name="HasText">False when the answer has no text of a first choice at all (another shape, a refusal).</param>
    public sealed record Answer(string Content, int PromptTokens, int CompletionTokens, bool HasText = true);

    public bool HasKey => !string.IsNullOrEmpty(ApiKey);

    private string ApiKey => _config["OpenAiApiKey"] ?? _config["OpenAI:ApiKey"] ?? "";

    /// <param name="jsonAnswer">Ask for an answer that is one JSON object (<c>response_format</c>).</param>
    public async Task<Answer> CompleteAsync(string model, float temperature, string systemPrompt, string userPrompt,
        string? baseUrlOverride, bool jsonAnswer, CancellationToken cancellationToken)
    {
        var baseUrl = (baseUrlOverride ?? _config["OpenAI:BaseUrl"] ?? "https://api.openai.com/").TrimEnd('/') + "/";
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(120);

        var messages = new[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        };
        object body = jsonAnswer
            ? new { model, temperature, messages, response_format = new { type = "json_object" } }
            : new { model, temperature, messages };

        var response = await client.PostAsJsonAsync("v1/chat/completions", body, cancellationToken);
        response.EnsureSuccessStatusCode();
        var answer = ReadAnswer(await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken));
        if (!answer.HasText)
            throw new InvalidOperationException("The answer of the model has no text");
        return answer;
    }

    /// <summary>The text and the usage of a chat completion answer.</summary>
    public static Answer ReadAnswer(JsonElement json)
    {
        var content = "";
        var hasText = false;
        if (json.ValueKind == JsonValueKind.Object
            && json.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
            && choices[0].ValueKind == JsonValueKind.Object
            && choices[0].TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object
            && message.TryGetProperty("content", out var text) && text.ValueKind == JsonValueKind.String)
        {
            content = text.GetString() ?? "";
            hasText = true;
        }

        int promptTokens = 0, completionTokens = 0;
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out var pv)) promptTokens = pv;
            if (usage.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out var cv)) completionTokens = cv;
        }

        return new Answer(content, promptTokens, completionTokens, hasText);
    }
}
