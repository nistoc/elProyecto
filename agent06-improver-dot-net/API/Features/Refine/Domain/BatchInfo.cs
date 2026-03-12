namespace TranslationImprover.Features.Refine.Domain;

/// <summary>
/// Information about a batch of lines to process (refinement).
/// </summary>
public sealed class BatchInfo
{
    public int Index { get; set; }
    public int StartLine { get; set; }
    public int EndLine { get; set; }
    public IReadOnlyList<string> Lines { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string>? Context { get; set; }
}
