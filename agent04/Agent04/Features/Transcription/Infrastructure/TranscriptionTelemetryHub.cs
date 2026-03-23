using System.Collections.Concurrent;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Last footer line per Agent04 job id for gRPC status/stream (operator UI).
/// </summary>
public sealed class TranscriptionTelemetryHub
{
    private readonly ConcurrentDictionary<string, string> _footerByJob = new(StringComparer.Ordinal);

    public void SetFooterHint(string agentJobId, string line)
    {
        if (string.IsNullOrWhiteSpace(agentJobId) || string.IsNullOrWhiteSpace(line))
            return;
        var t = line.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (t.Length > 500)
            t = t[..500] + "…";
        _footerByJob[agentJobId] = t;
    }

    public string GetFooterHint(string agentJobId) =>
        _footerByJob.TryGetValue(agentJobId, out var v) ? v : "";

    public void ClearFooterHint(string agentJobId)
    {
        if (string.IsNullOrWhiteSpace(agentJobId))
            return;
        _footerByJob.TryRemove(agentJobId, out _);
    }
}
