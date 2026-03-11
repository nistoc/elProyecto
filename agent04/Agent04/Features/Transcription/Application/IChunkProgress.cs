namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Progress tracker for chunk processing (for CLI or monitoring).
/// </summary>
public interface IChunkProgress
{
    int Total { get; }
    void MarkStarted(int chunkIndex);
    void MarkCompleted(int chunkIndex);
    void MarkCancelled(int chunkIndex);
    void Update();
    void Complete();
}
