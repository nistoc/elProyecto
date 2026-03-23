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
    void SavePerChunkJson(string chunkBasename, IReadOnlyDictionary<string, object?> response, string outputDir);
    void ResetSpeakerMap();
}
