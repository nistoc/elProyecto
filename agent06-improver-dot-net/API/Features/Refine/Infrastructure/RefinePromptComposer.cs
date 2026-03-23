using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// Builds the same user message as <see cref="OpenAIRefineClient"/> for preview and API calls.
/// </summary>
public static class RefinePromptComposer
{
    public static string BuildUserMessageContent(BatchInfo batchInfo, string userPromptTemplate)
    {
        var contextText = batchInfo.Context != null && batchInfo.Context.Count > 0
            ? "Context from previous batch (for continuity):\n```\n" + string.Join("\n", batchInfo.Context.Select(l => l.TrimEnd())) + "\n```"
            : "No previous context available.";
        var batchText = string.Join("", batchInfo.Lines.Select(l => l.TrimEnd() + "\n"));
        return userPromptTemplate
            .Replace("{context}", contextText, StringComparison.Ordinal)
            .Replace("{batch}", batchText, StringComparison.Ordinal);
    }

    /// <summary>Human-readable preview: system + user blocks (sent to OpenAI as two messages).</summary>
    public static string BuildOpenAiRequestPreview(string systemPrompt, string userMessageContent)
    {
        return "=== system ===\n" + systemPrompt + "\n\n=== user ===\n" + userMessageContent;
    }
}
