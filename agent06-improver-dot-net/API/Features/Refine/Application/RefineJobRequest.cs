namespace TranslationImprover.Features.Refine.Application;

/// <summary>
/// Request parameters for a refine job (from REST/gRPC).
/// </summary>
public sealed class RefineJobRequest
{
    public string? InputFilePath { get; set; }
    public string? InputContent { get; set; }
    public string? OutputFilePath { get; set; }
    public int BatchSize { get; set; } = 10;
    public int ContextLines { get; set; } = 3;
    public string Model { get; set; } = "gpt-4o-mini";
    public float Temperature { get; set; } = 0f;
    public string? PromptFile { get; set; }
    public string? OpenAIBaseUrl { get; set; }
    public string? OpenAIOrganization { get; set; }
    public bool SaveIntermediate { get; set; } = true;
    public string? IntermediateDir { get; set; }
    public string? CallbackUrl { get; set; }
    public IReadOnlyList<string>? Tags { get; set; }
    /// <summary>Optional. Single segment under workspace (Xtract job folder). When set, file I/O uses this directory; prompt paths still use workspace root.</summary>
    public string? JobDirectoryRelative { get; set; }
    /// <summary>Optional. When set (e.g. by Agent05), replaces configured WorkspaceRoot for this job so artifacts align with the caller's job directory.</summary>
    public string? WorkspaceRootOverride { get; set; }
}
