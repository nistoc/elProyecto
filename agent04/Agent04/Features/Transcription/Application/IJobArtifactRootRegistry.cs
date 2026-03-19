namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Maps Agent04 internal job id to the artifact root directory (for chunk cancel signals) for the process lifetime.
/// </summary>
public interface IJobArtifactRootRegistry
{
    void Register(string agent04JobId, string artifactRootFullPath);
    void Unregister(string agent04JobId);
    bool TryGet(string agent04JobId, out string? artifactRootFullPath);
}
