using Microsoft.Extensions.Logging;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Infrastructure;

public sealed class OpenAIRefineClient : IOpenAIRefineClient
{
    private readonly OpenAIChatClient _chat;
    private readonly ILogger<OpenAIRefineClient>? _logger;

    public OpenAIRefineClient(OpenAIChatClient chat, ILogger<OpenAIRefineClient>? logger = null)
    {
        _chat = chat;
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
        if (!_chat.HasKey)
        {
            _logger?.LogWarning("OpenAI API key not configured");
            return new BatchResult { BatchIndex = batchInfo.Index, FixedLines = batchInfo.Lines.ToList(), Success = false, Error = "OpenAI API key not configured" };
        }

        try
        {
            var answer = await _chat.CompleteAsync(model, temperature, systemPrompt, BuildUserPrompt(batchInfo, userPromptTemplate),
                baseUrlOverride, jsonAnswer: false, cancellationToken);
            return ToResult(answer, batchInfo);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "OpenAI API call failed for batch {Index}", batchInfo.Index);
            return new BatchResult { BatchIndex = batchInfo.Index, FixedLines = batchInfo.Lines.ToList(), Success = false, Error = ex.Message };
        }
    }

    /// <summary>The template with the block's lines at <c>{batch}</c> and its context at <c>{context}</c>.</summary>
    public static string BuildUserPrompt(BatchInfo batchInfo, string userPromptTemplate)
    {
        var contextText = batchInfo.Context != null && batchInfo.Context.Count > 0
            ? "Context from previous batch (for continuity):\n```\n" + string.Join("\n", batchInfo.Context.Select(l => l.TrimEnd())) + "\n```"
            : "No previous context available.";
        var batchText = string.Join("", batchInfo.Lines.Select(l => l.TrimEnd() + "\n"));
        return userPromptTemplate
            .Replace("{context}", contextText, StringComparison.Ordinal)
            .Replace("{batch}", batchText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The block's result from the model's answer: its lines without the empty ones, each ending with "\n".
    /// An answer with no line at all gives the block's own lines back (the call still counts as answered).
    /// </summary>
    public static BatchResult ToResult(OpenAIChatClient.Answer answer, BatchInfo batchInfo)
    {
        var lines = LinesOf(answer.Content);
        if (lines.Count == 0 && batchInfo.Lines.Count > 0)
            lines = batchInfo.Lines.ToList();

        return new BatchResult
        {
            BatchIndex = batchInfo.Index,
            FixedLines = lines,
            Success = true,
            RawAnswer = answer.Content,
            PromptTokens = answer.PromptTokens,
            CompletionTokens = answer.CompletionTokens
        };
    }

    /// <summary>The lines of a text that have anything in them, trimmed at the end, each with a "\n".</summary>
    public static List<string> LinesOf(string content)
    {
        var lines = new List<string>();
        foreach (var line in content.Trim().Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            lines.Add(line.TrimEnd() + "\n");
        }

        return lines;
    }
}
