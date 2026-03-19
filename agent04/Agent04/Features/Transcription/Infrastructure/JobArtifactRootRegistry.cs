using System.Collections.Concurrent;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

public sealed class JobArtifactRootRegistry : IJobArtifactRootRegistry
{
    private readonly ConcurrentDictionary<string, string> _map = new(StringComparer.Ordinal);

    public void Register(string agent04JobId, string artifactRootFullPath)
    {
        if (string.IsNullOrWhiteSpace(agent04JobId)) return;
        _map[agent04JobId] = artifactRootFullPath;
    }

    public void Unregister(string agent04JobId)
    {
        if (string.IsNullOrWhiteSpace(agent04JobId)) return;
        _map.TryRemove(agent04JobId, out _);
    }

    public bool TryGet(string agent04JobId, out string? artifactRootFullPath)
    {
        if (string.IsNullOrWhiteSpace(agent04JobId))
        {
            artifactRootFullPath = null;
            return false;
        }
        return _map.TryGetValue(agent04JobId, out artifactRootFullPath);
    }
}
