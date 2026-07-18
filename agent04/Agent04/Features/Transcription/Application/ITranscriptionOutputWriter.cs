using Agent04.Features.Transcription.Domain;

namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Writes transcription results to Markdown and JSON (agent01-compatible format).
/// </summary>
public interface ITranscriptionOutputWriter
{
    void InitializeMarkdown(string mdPath);
    void AppendSegmentsToMarkdown(string mdPath, IReadOnlyList<ASRSegment> segments, double offset, double emitGuard);
    void FinalizeMarkdown(string mdPath);

    /// <summary>
    /// Writes a standalone per-chunk markdown file (<c>&gt;&gt;&gt;&gt;&gt;&gt;&gt;</c> … lines … <c>&lt;&lt;&lt;&lt;&lt;</c>) with a
    /// speaker map local to this chunk. This is the source material the combined transcript is stitched from.
    /// </summary>
    void WritePerChunkMarkdown(string chunkBasename, IReadOnlyList<ASRSegment> segments, double offset, double emitGuard, string outputDir);
    void SaveCombinedJson(string jsonPath, IReadOnlyList<TranscriptionResult> results);
    void SavePerChunkJson(string chunkBasename, IReadOnlyDictionary<string, object?> response, string outputDir);
    void ResetSpeakerMap();
}
