namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Creates a file-based <see cref="ICancellationManager"/> scoped to one Agent04 job id under the workspace (no cross-job index collisions).
/// </summary>
public interface ICancellationManagerFactory
{
    ICancellationManager Get(string agent04JobId, string workspaceRootFullPath);
}
