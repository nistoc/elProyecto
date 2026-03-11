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
}
