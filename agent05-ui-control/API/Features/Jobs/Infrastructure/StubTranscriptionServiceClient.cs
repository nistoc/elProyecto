namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class StubTranscriptionServiceClient : Application.ITranscriptionServiceClient
{
    public Task<Application.SubmitJobResult> SubmitJobAsync(string configPath, string inputFilePath, IReadOnlyList<string>? tags, CancellationToken ct = default)
    {
        return Task.FromResult(new Application.SubmitJobResult("stub-transcription-job"));
    }

    public async IAsyncEnumerable<Application.JobStatusUpdate> StreamJobStatusAsync(string jobId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var u in StreamJobStatusAsync(jobId, null, ct))
            yield return u;
    }

    public async IAsyncEnumerable<Application.JobStatusUpdate> StreamJobStatusAsync(
        string jobId,
        IReadOnlyList<Application.ChunkVirtualModelEntry>? clientChunkVirtualModel,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<Application.JobStatusUpdate?> GetJobStatusAsync(string agent04JobId, CancellationToken ct = default) =>
        GetJobStatusAsync(agent04JobId, null, ct);

    public Task<Application.JobStatusUpdate?> GetJobStatusAsync(
        string agent04JobId,
        IReadOnlyList<Application.ChunkVirtualModelEntry>? clientChunkVirtualModel,
        CancellationToken ct) =>
        Task.FromResult<Application.JobStatusUpdate?>(null);

    public Task<Application.ChunkCommandResult> ChunkCommandAsync(string agent04JobId, Application.TranscriptionChunkAction action, int chunkIndex, string? jobDirectoryRelative = null, int splitParts = 0, int subChunkIndex = 0, CancellationToken ct = default) =>
        Task.FromResult(new Application.ChunkCommandResult(false, "stub_transcription_client"));

    public Task<Application.ChunkArtifactGroupsResult?> GetChunkArtifactGroupsAsync(
        string agent04JobId,
        string jobDirectoryRelative,
        int totalChunks,
        CancellationToken ct = default) =>
        Task.FromResult<Application.ChunkArtifactGroupsResult?>(
            new Application.ChunkArtifactGroupsResult { Groups = Array.Empty<Application.ChunkArtifactGroupJson>() });
}
