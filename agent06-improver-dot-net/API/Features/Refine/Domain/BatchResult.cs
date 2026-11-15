namespace TranslationImprover.Features.Refine.Domain;

/// <summary>
/// Result of processing a single batch.
/// </summary>
public sealed class BatchResult
{
    public int BatchIndex { get; set; }
    public IReadOnlyList<string> FixedLines { get; set; } = Array.Empty<string>();
    public bool Success { get; set; }
    public string? Error { get; set; }

    /// <summary>The model's answer as it came, before any check of it; null when the call gave none.</summary>
    public string? RawAnswer { get; set; }

    /// <summary>Tokens the call was billed for, as the API reports them; 0 when it reported none.</summary>
    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }
}
