using Agent04.Features.Transcription.Domain;

namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Orchestrates the full transcription workflow: chunking, cache, API, merge, output.
/// </summary>
public interface ITranscriptionPipeline
{
    /// <summary>
    /// Process one audio file. Returns (mdOutputPath, jsonOutputPath).
    /// When jobId and statusStore are provided, updates progress for monitoring.
    /// </summary>
    Task<(string MdPath, string JsonPath)> ProcessFileAsync(
        TranscriptionConfig config,
        string inputFilePath,
        string? jobId = null,
        IJobStatusStore? statusStore = null,
        CancellationToken cancellationToken = default);
}
