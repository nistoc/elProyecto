namespace Agent04.Features.Transcription.Application;

/// <summary>
/// File-based cancellation signals for chunk processing (e.g. cancel_N.flag in a directory).
/// </summary>
public interface ICancellationManager
{
    /// <summary>Returns newly observed cancellation request chunk indices.</summary>
    IReadOnlySet<int> Poll();
    bool IsCancelled(int chunkIndex);
    void MarkCancelled(int chunkIndex);

    /// <summary>Removes on-disk cancel flag and in-memory latch so a new transcription run is not aborted by a stale signal. Returns whether a file existed.</summary>
    bool ClearChunkCancelFlag(int chunkIndex);

    /// <summary>Operator-split sub-chunk: writes <c>cancel_sub_{parent}_{sub}.flag</c> (agent01 used <c>cancel_sub_{sub}</c> per single-parent CLI).</summary>
    void MarkSubChunkCancelled(int parentChunkIndex, int subChunkIndex);

    bool IsSubChunkCancelled(int parentChunkIndex, int subChunkIndex);

    /// <summary>Removes stale sub-chunk cancel flag before a new <c>TranscribeSub</c> run. Returns whether a file existed.</summary>
    bool ClearSubChunkCancelFlag(int parentChunkIndex, int subChunkIndex);
}
