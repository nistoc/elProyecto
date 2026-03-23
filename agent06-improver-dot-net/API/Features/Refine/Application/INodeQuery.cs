using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Application;

/// <summary>
/// Read-side: get nodes by scope (flat list or tree), or a single node by id (tag).
/// </summary>
public interface INodeQuery
{
    IReadOnlyList<NodeInfo> GetByScope(string scopeId);
    IReadOnlyList<NodeInfo> GetTreeByScope(string scopeId);
    /// <summary>Get a single node by scope and node id (virtual model "tag" = node id for status).</summary>
    NodeInfo? GetNodeByScopeAndId(string scopeId, string nodeId);
}

public sealed class NodeInfo
{
    public string Id { get; set; } = "";
    public string? ParentId { get; set; }
    public string ScopeId { get; set; } = "";
    public string? Kind { get; set; }
    public RefineJobState Status { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? ProgressPercent { get; set; }
    public string? Phase { get; set; }
    public string? ErrorMessage { get; set; }
    public IReadOnlyDictionary<string, object?>? Metadata { get; set; }
    public IReadOnlyList<NodeInfo>? Children { get; set; }
}
