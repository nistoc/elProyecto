namespace TranslationImprover.Features.Refine.Application;

/// <summary>What is wrong with a request; <see cref="Field"/> and <see cref="Value"/> name the offending part when there is one.</summary>
public sealed record RefineRequestError(string Message, string? Field = null, string? Value = null);

/// <summary>
/// The one place where a request of either entry (REST body, gRPC message) gets its defaults, its trimmed values
/// and its check. Each entry used to copy the request field by field on its own and the two copies drifted
/// apart: the REST one lost the check of edits, the system prompt and the source name, with no error.
/// A new field of <see cref="RefineJobRequest"/> is added here once; <c>RefineJobRequestMapperTests</c> fails
/// when it is forgotten.
/// </summary>
public static class RefineJobRequestMapper
{
    public const string DefaultModel = "gpt-4o-mini";
    public const int DefaultBatchSize = 10;
    public const int DefaultContextLines = 3;

    /// <summary>The request as the pipeline takes it: values trimmed, empty ones as null, defaults in place.</summary>
    public static RefineJobRequest Normalize(RefineJobRequest request) => new()
    {
        InputFilePath = TrimOrNull(request.InputFilePath),
        InputContent = string.IsNullOrEmpty(request.InputContent) ? null : request.InputContent,
        OutputFilePath = TrimOrNull(request.OutputFilePath),
        BatchSize = request.BatchSize > 0 ? request.BatchSize : DefaultBatchSize,
        ContextLines = request.ContextLines >= 0 ? request.ContextLines : DefaultContextLines,
        Model = TrimOrNull(request.Model) ?? DefaultModel,
        Temperature = request.Temperature,
        PromptFile = TrimOrNull(request.PromptFile),
        SystemPrompt = TrimOrNull(request.SystemPrompt),
        SourceName = TrimOrNull(request.SourceName),
        RespellingOnly = request.RespellingOnly,
        OpenAIBaseUrl = TrimOrNull(request.OpenAIBaseUrl),
        OpenAIOrganization = TrimOrNull(request.OpenAIOrganization),
        SaveIntermediate = request.SaveIntermediate,
        IntermediateDir = TrimOrNull(request.IntermediateDir),
        CallbackUrl = TrimOrNull(request.CallbackUrl),
        Tags = request.Tags is { Count: > 0 } ? request.Tags.ToList() : null,
        JobDirectoryRelative = TrimOrNull(request.JobDirectoryRelative),
    };

    /// <summary>Checks a normalized request against the folder its paths are read from; null when it can run.</summary>
    public static RefineRequestError? Validate(RefineJobRequest request, string artifactRoot)
    {
        if (request.InputFilePath == null && request.InputContent == null)
            return new RefineRequestError("Either input_file_path or input_content must be set");

        if (request.InputFilePath != null)
        {
            if (Path.IsPathRooted(request.InputFilePath))
                return new RefineRequestError("input_file_path must be relative to workspace_root; absolute paths are not allowed", "inputFilePath", request.InputFilePath);
            var relative = RelativePath(request.InputFilePath);
            if (!File.Exists(Path.Combine(artifactRoot, relative)))
                return new RefineRequestError("Input file not found", "inputFilePath", relative);
        }

        if (request.OutputFilePath != null && Path.IsPathRooted(request.OutputFilePath))
            return new RefineRequestError("output_file_path must be relative to workspace_root", "outputFilePath", request.OutputFilePath);

        return null;
    }

    /// <summary>A request path without the leading separators, ready to be joined to a root.</summary>
    public static string RelativePath(string path) =>
        path.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
