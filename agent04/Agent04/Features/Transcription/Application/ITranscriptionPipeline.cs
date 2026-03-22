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
    /// Re-run API transcription for one main pipeline chunk and rebuild combined md/json from
    /// <c>chunks_json</c> siblings + fresh result for <paramref name="chunkIndex"/>.
    /// </summary>
    Task RetranscribeMainChunkAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        int chunkIndex,
        INodeModel? nodeModel,
        CancellationToken cancellationToken = default);
}
