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

    /// <summary>
    /// Looks again at the kept answers of a job transcribed by a plain-text model: the lines the doubts of
    /// today find in them and that were not asked about then are asked again with the audio around their
    /// pieces (paid, the seconds of those cuts only) and settled as at transcription. A changed answer is
    /// written to <c>chunks_json</c> and the cache, its chunk md follows it unless the operator edited it,
    /// and the combined transcript is written anew (a new file) when anything changed.
    /// </summary>
    /// <param name="chunkIndex">One main chunk; below zero, every chunk of the job.</param>
    Task<LookAgainSummary> LookAgainAtMainChunksAsync(
        TranscriptionConfig config,
        string artifactRoot,
        string agentJobId,
        int chunkIndex,
        CancellationToken cancellationToken = default);
}

/// <summary>What <see cref="ITranscriptionPipeline.LookAgainAtMainChunksAsync"/> did.</summary>
/// <param name="Chunks">Chunks whose kept answer was read.</param>
/// <param name="ChunksChanged">Chunks whose answer was written anew.</param>
/// <param name="LinesAsked">Pieces asked again with the audio around them.</param>
/// <param name="LinesTaken">Lines replaced.</param>
/// <param name="AudioSecAsked">Seconds of audio sent.</param>
/// <param name="MdKept">Changed chunks whose md the operator had edited: it stays, the new text is in the JSON only.</param>
/// <param name="Rebuilt">Path of the combined transcript written anew; null when nothing changed.</param>
public sealed record LookAgainSummary(int Chunks, int ChunksChanged, int LinesAsked, int LinesTaken, double AudioSecAsked, int MdKept, string? Rebuilt);
