namespace XtractManager.Features.Jobs.Application;

/// <summary>
/// Operator-tuned refiner settings, shared by all jobs. A null value means "use the default".
/// </summary>
public sealed record RefinerSettings(string? PromptTemplate, int? BatchSize, int? ContextLines, string? Model, string? SystemPrompt = null);

/// <summary>Persists <see cref="RefinerSettings"/> in the jobs workspace root, next to the job folders.</summary>
public interface IRefinerSettingsStore
{
    /// <summary>Prompt file name, relative to the workspace root (agent06 resolves prompt_file against the same root).</summary>
    const string PromptFileName = "refiner_prompt.txt";

    /// <summary>Default batch geometry and model when the operator has not overridden them.</summary>
    const int DefaultBatchSize = 5;
    const int DefaultContextLines = 2;
    const string DefaultModel = "gpt-4o-mini";

    RefinerSettings Load();
    void Save(RefinerSettings settings);
}
