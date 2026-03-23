using Agent04.Features.Transcription.Application;
using Agent04.Proto;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// Builds proto VM rows from <see cref="TranscriptionWorkStateDocument"/> for merge with Rentgen/client VM.
/// </summary>
public static class ChunkVirtualModelFromWorkState
{
    public static IReadOnlyList<ChunkVirtualModelEntry> Build(TranscriptionWorkStateDocument? doc)
    {
        if (doc?.Chunks is not { Count: > 0 })
            return Array.Empty<ChunkVirtualModelEntry>();

        var list = new List<ChunkVirtualModelEntry>(doc.Chunks.Count);
        foreach (var c in doc.Chunks)
        {
            list.Add(new ChunkVirtualModelEntry
            {
                ChunkIndex = c.Index,
                StartedAt = c.StartedAt ?? "",
                CompletedAt = c.CompletedAt ?? "",
                State = c.State ?? "",
                ErrorMessage = c.ErrorMessage ?? "",
                IsSubChunk = c.IsSubChunk,
                ParentChunkIndex = c.ParentChunkIndex,
                SubChunkIndex = c.SubChunkIndex,
            });
        }

        return list;
    }
}
