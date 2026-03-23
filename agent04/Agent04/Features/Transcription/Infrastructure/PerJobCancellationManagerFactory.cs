using System.Text.RegularExpressions;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

public sealed class PerJobCancellationManagerFactory : ICancellationManagerFactory
{
    private static readonly Regex SafeId = new(@"[^a-zA-Z0-9_\-\.]", RegexOptions.Compiled);

    public ICancellationManager Get(string agent04JobId, string workspaceRootFullPath)
    {
        var safe = string.IsNullOrEmpty(agent04JobId)
            ? "_unknown"
            : SafeId.Replace(agent04JobId, "_");
        var dir = Path.Combine(Path.GetFullPath(workspaceRootFullPath), ".agent04_chunk_cancel", safe);
        return new CancellationManager(dir);
    }
}
