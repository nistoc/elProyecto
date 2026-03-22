using Agent04.Features.Transcription.Domain;

namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Orchestrates the full transcription workflow: chunking, cache, API, merge, output.
/// </summary>
public interface ITranscriptionPipeline
{
    /// <summary>
    /// Process one audio file. Returns (mdOutputPath, jsonOutputPath).
    /// workspaceRoot: trust boundary; input must exist under this directory. Artifact paths from config (chunks, cache, transcript, etc.)
    /// are resolved under the input file's directory (per-job isolation). Chunk cancel signals use the same directory via the registry / ChunkCommand.
    /// When jobId and statusStore are provided, updates progress for monitoring.
    /// When jobId and nodeModel are provided, records node hierarchy for virtual model.
    /// </summary>
    Task<(string MdPath, string JsonPath)> ProcessFileAsync(
        TranscriptionConfig config,
        string inputFilePath,
        string workspaceRoot,
        string? jobId = null,
        IJobStatusStore? statusStore = null,
        INodeModel? nodeModel = null,
        CancellationToken cancellationToken = default,
        IReadOnlySet<int>? chunkIndicesFilter = null);

    /// <summary>
    /// Transcribe one operator-split sub-file; writes <c>results/sub_chunk_XX_result.json</c> and VM/work-state row.
    /// </summary>
    Task TranscribeSplitSubChunkAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        int parentChunkIndex,
        int subChunkIndex,
        int totalChunks,
        INodeModel? nodeModel,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-run API transcription for one main pipeline chunk. Updates per-chunk JSON, cache manifest, and work state.
    /// When <c>chunks/</c> contains <c>_part_NNN</c> audio, uses that file for <paramref name="chunkIndex"/> (not config root input).
    /// Does not rebuild combined markdown or combined JSON (separate refiner flow).
    /// </summary>
    Task RetranscribeMainChunkAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        int chunkIndex,
        INodeModel? nodeModel,
        CancellationToken cancellationToken = default,
        IJobStatusStore? statusStore = null);

    /// <summary>
    /// Rebuild combined markdown and combined JSON from <c>chunks_json</c> only (no API calls).
    /// </summary>
    Task RebuildCombinedOutputsFromPerChunkJsonAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        INodeModel? nodeModel,
        CancellationToken cancellationToken = default);
}
