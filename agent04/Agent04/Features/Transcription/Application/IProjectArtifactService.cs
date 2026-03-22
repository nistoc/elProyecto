namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Single owner of persistent job artifact layout on disk (resolve paths, read/write/delete, grouping).
/// Phases 2+ add methods; phase 1 exposes root resolution shared with chunk commands.
/// </summary>
public interface IProjectArtifactService
{
    /// <summary>
    /// Resolves artifact root: <see cref="IJobArtifactRootRegistry"/> first, then validated
    /// <c>job_directory_relative</c> under workspace, then optional legacy workspace root (with warning) or strict failure.
    /// </summary>
    ArtifactRootResolutionResult ResolveJobArtifactRoot(
        string workspaceRootFull,
        string agent04JobId,
        string? jobDirectoryRelative);
}
