namespace Agent04.Features.Transcription.Application;

public enum ArtifactRootResolutionFailureCode
{
    InvalidRelativePath,
    OutsideWorkspace,
    StrictRequiresJobDirectoryRelative,
}

/// <summary>
/// Result of resolving the on-disk artifact root for a job (registry, validated relative path, or legacy workspace fallback).
/// </summary>
public readonly record struct ArtifactRootResolutionResult(
    string? Path,
    ArtifactRootResolutionFailureCode? Failure,
    string? Message)
{
    public bool IsSuccess => Path is not null;
}
