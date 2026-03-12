namespace TranslationImprover.Features.Refine.Application;

/// <summary>
/// Loads prompt template from file (path relative to workspace root) or returns default.
/// </summary>
public interface IPromptLoader
{
    Task<string> LoadAsync(string? promptFileRelativePath, string workspaceRoot, CancellationToken cancellationToken = default);
}
