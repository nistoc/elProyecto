namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Write-side of the virtual node model: create/update nodes at step boundaries (EnsureNode, StartNode, CompleteNode).
/// Used by pipeline to record hierarchy (job → phases → chunks) for monitoring.
/// </summary>
public interface INodeModel
{
    void EnsureNode(string nodeId, string? parentId, string scopeId, string? kind = null, IReadOnlyDictionary<string, object?>? metadata = null);
    void StartNode(string nodeId);
    void CompleteNode(string nodeId, JobState status, DateTimeOffset? completedAt = null, string? errorMessage = null);
    /// <summary>Update progress and phase on a node (e.g. job or transcribe phase).</summary>
    void UpdateNodeProgress(string nodeId, int progressPercent, string? phase = null);
}

/// <summary>
/// Read-side: get nodes by scope (flat list or tree), or a single node by id (tag).
/// </summary>
public interface INodeQuery
{
    IReadOnlyList<NodeInfo> GetByScope(string scopeId);
    IReadOnlyList<NodeInfo> GetTreeByScope(string scopeId);
    /// <summary>Get a single node by scope and node id (virtual model "tag" = node id/name for status).</summary>
    NodeInfo? GetNodeByScopeAndId(string scopeId, string nodeId);
}

/// <summary>
/// Node DTO for API (REST/gRPC): tree or flat list by scopeId.
/// </summary>
public sealed class NodeInfo
{
    public string Id { get; set; } = "";
    public string? ParentId { get; set; }
    public string ScopeId { get; set; } = "";
    public string? Kind { get; set; }
    public JobState Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? ProgressPercent { get; set; }
    public string? Phase { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyDictionary<string, object?>? Metadata { get; set; }
    public IReadOnlyList<NodeInfo>? Children { get; set; }
}
