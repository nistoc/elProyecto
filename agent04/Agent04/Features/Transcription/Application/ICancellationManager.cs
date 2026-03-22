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

    /// <summary>Operator-split sub-chunk: writes <c>cancel_sub_{parent}_{sub}.flag</c> (agent01 used <c>cancel_sub_{sub}</c> per single-parent CLI).</summary>
    void MarkSubChunkCancelled(int parentChunkIndex, int subChunkIndex);

    bool IsSubChunkCancelled(int parentChunkIndex, int subChunkIndex);
}
