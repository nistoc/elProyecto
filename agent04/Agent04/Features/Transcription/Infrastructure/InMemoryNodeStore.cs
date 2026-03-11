using System.Collections.Concurrent;
using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// In-memory store for virtual model nodes. Implements write (INodeModel) and read (INodeQuery).
/// </summary>
public sealed class InMemoryNodeStore : INodeModel, INodeQuery
{
    private readonly ConcurrentDictionary<string, NodeRecord> _nodes = new();
    private readonly ConcurrentDictionary<string, List<string>> _scopeIndex = new();

    public void EnsureNode(string nodeId, string? parentId, string scopeId, string? kind = null, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        var now = DateTimeOffset.UtcNow;
        _nodes.AddOrUpdate(nodeId, _ =>
        {
            AddToScopeIndex(scopeId, nodeId);
            return new NodeRecord
            {
                Id = nodeId,
                ParentId = parentId,
                ScopeId = scopeId,
                Kind = kind,
                Status = JobState.Pending,
                UpdatedAt = now,
                Metadata = metadata != null ? new Dictionary<string, object?>(metadata) : null
            };
        }, (_, existing) =>
        {
            if (existing.Kind == null && kind != null) existing.Kind = kind;
            if (metadata != null)
            {
                existing.Metadata ??= new Dictionary<string, object?>();
                foreach (var kv in metadata)
                    existing.Metadata[kv.Key] = kv.Value;
            }
            existing.UpdatedAt = now;
            return existing;
        });
    }

    public void StartNode(string nodeId)
    {
        var now = DateTimeOffset.UtcNow;
        if (_nodes.TryGetValue(nodeId, out var node))
        {
            node.Status = JobState.Running;
            node.StartedAt ??= now;
            node.UpdatedAt = now;
        }
    }

    public void CompleteNode(string nodeId, JobState status, DateTimeOffset? completedAt = null, string? errorMessage = null)
    {
        var now = completedAt ?? DateTimeOffset.UtcNow;
        if (_nodes.TryGetValue(nodeId, out var node))
        {
            node.Status = status;
            node.CompletedAt = now;
            node.UpdatedAt = now;
            if (errorMessage != null) node.ErrorMessage = errorMessage;
        }
    }

    public void UpdateNodeProgress(string nodeId, int progressPercent, string? phase = null)
    {
        var now = DateTimeOffset.UtcNow;
        if (_nodes.TryGetValue(nodeId, out var node))
        {
            node.ProgressPercent = progressPercent;
            if (phase != null) node.Phase = phase;
            node.UpdatedAt = now;
        }
    }

    public IReadOnlyList<NodeInfo> GetByScope(string scopeId)
    {
        if (!_scopeIndex.TryGetValue(scopeId, out var ids))
            return Array.Empty<NodeInfo>();
        lock (ids)
        {
            return ids.Select(id => _nodes.TryGetValue(id, out var r) ? ToNodeInfo(r, null) : null).Where(x => x != null).Cast<NodeInfo>().ToList();
        }
    }

    public IReadOnlyList<NodeInfo> GetTreeByScope(string scopeId)
    {
        var flat = GetByScope(scopeId);
        if (flat.Count == 0) return Array.Empty<NodeInfo>();
        var byId = flat.ToDictionary(n => n.Id, n => new NodeInfo
        {
            Id = n.Id,
            ParentId = n.ParentId,
            ScopeId = n.ScopeId,
            Kind = n.Kind,
            Status = n.Status,
            StartedAt = n.StartedAt,
            CompletedAt = n.CompletedAt,
            UpdatedAt = n.UpdatedAt,
            ProgressPercent = n.ProgressPercent,
            Phase = n.Phase,
            ErrorMessage = n.ErrorMessage,
            Metadata = n.Metadata,
            Children = new List<NodeInfo>()
        }, StringComparer.Ordinal);
        var roots = new List<NodeInfo>();
        foreach (var n in flat)
        {
            var copy = byId[n.Id];
            if (n.ParentId == null)
                roots.Add(copy);
            else if (byId.TryGetValue(n.ParentId, out var parent))
                ((List<NodeInfo>)parent.Children!).Add(copy);
        }
        return roots;
    }

    private void AddToScopeIndex(string scopeId, string nodeId)
    {
        var list = _scopeIndex.GetOrAdd(scopeId, _ => new List<string>());
        lock (list)
        {
            if (!list.Contains(nodeId))
                list.Add(nodeId);
        }
    }

    private static NodeInfo ToNodeInfo(NodeRecord r, IReadOnlyList<NodeInfo>? children)
    {
        return new NodeInfo
        {
            Id = r.Id,
            ParentId = r.ParentId,
            ScopeId = r.ScopeId,
            Kind = r.Kind,
            Status = r.Status,
            StartedAt = r.StartedAt,
            CompletedAt = r.CompletedAt,
            UpdatedAt = r.UpdatedAt,
            ProgressPercent = r.ProgressPercent,
            Phase = r.Phase,
            ErrorMessage = r.ErrorMessage,
            Metadata = r.Metadata,
            Children = children
        };
    }

    private sealed class NodeRecord
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
        public Dictionary<string, object?>? Metadata { get; set; }
    }
}
