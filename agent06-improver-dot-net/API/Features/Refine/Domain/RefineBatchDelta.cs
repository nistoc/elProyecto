namespace TranslationImprover.Features.Refine.Domain;

/// <summary>
/// One refined batch, carrying the before/after text so the UI can show the operator
/// exactly how each fragment was improved. Accumulated per job in <see cref="RefineJobStatus"/>.
/// </summary>
public sealed class RefineBatchDelta
{
    /// <summary>1-based batch number.</summary>
    public int Index { get; init; }
    public int Total { get; init; }
    /// <summary>Original fragment text (the batch lines before refining), newline-joined, trimmed.</summary>
    public string OriginalText { get; init; } = "";
    /// <summary>Refined fragment text (or the original when the batch failed), newline-joined, trimmed.</summary>
    public string RefinedText { get; init; } = "";
    /// <summary>True when the OpenAI call succeeded; false means the original was kept as-is.</summary>
    public bool Ok { get; init; }
    public string? Error { get; init; }
}
