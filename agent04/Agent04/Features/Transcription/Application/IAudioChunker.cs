using Agent04.Features.Transcription.Domain;

namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Handles audio file splitting with overlap support.
/// </summary>
public interface IAudioChunker
{
    /// <summary>
    /// Create overlapped chunks. Returns list of ChunkInfo with paths, offsets, emit guards.
    /// </summary>
    IReadOnlyList<ChunkInfo> SliceWithOverlap(
        string sourcePath,
        int segmentTimeSeconds,
        double overlapSec,
        string workdir,
        string namingPattern,
        double maxDurationSec = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Main entry: split file into chunks if needed (or single chunk if under target).
    /// </summary>
    Task<IReadOnlyList<ChunkInfo>> ProcessChunksForFileAsync(
        string sourcePath,
        double targetMb,
        string workdir,
        string namingPattern,
        double overlapSec,
        bool reencode = true,
        int reencodeBitrateKbps = 256,
        double maxDurationMinutes = 0,
        CancellationToken cancellationToken = default);
}
