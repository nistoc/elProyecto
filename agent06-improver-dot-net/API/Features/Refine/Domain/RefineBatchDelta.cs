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
    /// <summary>Context lines from the previous refined batch that were sent with this one (how batches are stitched); empty for the first batch.</summary>
    public string ContextText { get; init; } = "";
    /// <summary>1-based first/last content line of this batch in the transcript body.</summary>
    public int StartLine { get; init; }
    public int EndLine { get; init; }
}
