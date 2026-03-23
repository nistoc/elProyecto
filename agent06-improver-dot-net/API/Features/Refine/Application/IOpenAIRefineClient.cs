using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Application;

/// <summary>
/// Calls OpenAI (or compatible) API to fix a single batch of lines.
/// </summary>
public interface IOpenAIRefineClient
{
    Task<BatchResult> RefineBatchAsync(
        BatchInfo batchInfo,
        string model,
        float temperature,
        string systemPrompt,
        string userPromptTemplate,
        string? baseUrlOverride = null,
        string? debugLogArtifactRoot = null,
        CancellationToken cancellationToken = default);
}
