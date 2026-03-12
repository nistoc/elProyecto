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
}
