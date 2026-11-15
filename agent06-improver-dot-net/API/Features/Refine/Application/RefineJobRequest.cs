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
    /// <summary>Optional system prompt override; null = built-in <c>RefinePipeline.SystemPrompt</c>.</summary>
    public string? SystemPrompt { get; set; }
    /// <summary>Optional input transcript name, only recorded in the run's prompt file.</summary>
    public string? SourceName { get; set; }
    /// <summary>
    /// Of what the model changes keep only a word in Cyrillic letters respelled in Latin letters
    /// (<c>RespellingGuard</c>); everything else of a line stays as it was.
    /// </summary>
    public bool RespellingOnly { get; set; }
    public string? OpenAIBaseUrl { get; set; }
    public string? OpenAIOrganization { get; set; }
    public bool SaveIntermediate { get; set; } = true;
    public string? IntermediateDir { get; set; }
    public string? CallbackUrl { get; set; }
    public IReadOnlyList<string>? Tags { get; set; }
    /// <summary>Optional. Single segment under workspace (Xtract job folder). When set, file I/O uses this directory; prompt paths still use workspace root.</summary>
    public string? JobDirectoryRelative { get; set; }
}
