using Agent04.Features.Transcription.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

public sealed class ProjectArtifactService : IProjectArtifactService
{
    private readonly IJobArtifactRootRegistry _registry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ProjectArtifactService> _logger;

    public ProjectArtifactService(
        IJobArtifactRootRegistry registry,
        IConfiguration configuration,
        ILogger<ProjectArtifactService> logger)
    {
        _registry = registry;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public ArtifactRootResolutionResult ResolveJobArtifactRoot(
        string workspaceRootFull,
        string agent04JobId,
        string? jobDirectoryRelative)
    {
        if (string.IsNullOrWhiteSpace(workspaceRootFull))
            throw new ArgumentException("Workspace root is required.", nameof(workspaceRootFull));

        var root = Path.GetFullPath(workspaceRootFull.Trim());
        if (_registry.TryGet(agent04JobId, out var registered) && !string.IsNullOrEmpty(registered))
            return new ArtifactRootResolutionResult(registered, null, null);

        if (!string.IsNullOrWhiteSpace(jobDirectoryRelative))
        {
            var rel = jobDirectoryRelative.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (rel.Contains("..", StringComparison.Ordinal) ||
                rel.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
            {
                return new ArtifactRootResolutionResult(
                    null,
                    ArtifactRootResolutionFailureCode.InvalidRelativePath,
                    "job_directory_relative must be a single path segment");
            }

            var combined = Path.GetFullPath(Path.Combine(root, rel));
            var back = Path.GetRelativePath(root, combined);
            if (back.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(back))
            {
                return new ArtifactRootResolutionResult(
                    null,
                    ArtifactRootResolutionFailureCode.OutsideWorkspace,
                    "job_directory_relative resolves outside workspace_root");
            }

            return new ArtifactRootResolutionResult(combined, null, null);
        }

        var strict = _configuration.GetValue("Agent04:StrictChunkCancelPath", false);
        if (strict)
        {
            return new ArtifactRootResolutionResult(
                null,
                ArtifactRootResolutionFailureCode.StrictRequiresJobDirectoryRelative,
                "Chunk cancel requires job_directory_relative after restart or unknown job; set Agent04:StrictChunkCancelPath=false to allow legacy workspace-root fallback.");
        }

        _logger.LogWarning(
            "ChunkCommand: cancel signals use workspace root for Agent04 job {JobId}; worker may not see them if artifacts are under a job subfolder. Pass job_directory_relative.",
            agent04JobId);
        return new ArtifactRootResolutionResult(root, null, null);
    }
}
