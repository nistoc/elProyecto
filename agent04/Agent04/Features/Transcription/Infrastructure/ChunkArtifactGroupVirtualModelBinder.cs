using Agent04.Proto;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Attaches Rentgen <see cref="ChunkVirtualModelEntry"/> rows to gRPC chunk artifact groups (phase 8).
/// </summary>
public static class ChunkArtifactGroupVirtualModelBinder
{
    public static void ApplyToResponse(
        GetChunkArtifactGroupsResponse resp,
        IReadOnlyList<ChunkVirtualModelEntry> virtualModel)
    {
        if (virtualModel.Count == 0)
            return;

        foreach (var row in resp.Groups)
        {
            var main = virtualModel.FirstOrDefault(e => !e.IsSubChunk && e.ChunkIndex == row.Index);
            if (main != null)
                row.MainVirtualModel = main.Clone();

            foreach (var sc in row.SubChunks)
            {
                if (!sc.HasSubIndex)
                    continue;
                var sub = virtualModel.FirstOrDefault(e =>
                    e.IsSubChunk
                    && e.ParentChunkIndex == row.Index
                    && e.SubChunkIndex == sc.SubIndex);
                if (sub != null)
                    sc.SubVirtualModel = sub.Clone();
            }
        }
    }
}
