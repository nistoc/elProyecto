using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Application;

/// <summary>
/// Write-side of the virtual node model: create/update nodes at step boundaries.
/// Used by pipeline to record hierarchy (job → phase → batch-0..N) for monitoring.
/// </summary>
public interface INodeModel
{
    void EnsureNode(string nodeId, string? parentId, string scopeId, string? kind = null, IReadOnlyDictionary<string, object?>? metadata = null);
    void StartNode(string nodeId);
    void CompleteNode(string nodeId, RefineJobState status, DateTimeOffset? completedAt = null, string? errorMessage = null);
    void UpdateNodeProgress(string nodeId, int progressPercent, string? phase = null);
}
