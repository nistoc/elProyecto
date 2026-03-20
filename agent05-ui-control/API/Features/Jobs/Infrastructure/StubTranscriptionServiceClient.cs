namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class StubTranscriptionServiceClient : Application.ITranscriptionServiceClient
{
    public Task<Application.SubmitJobResult> SubmitJobAsync(string configPath, string inputFilePath, IReadOnlyList<string>? tags, CancellationToken ct = default)
    {
        return Task.FromResult(new Application.SubmitJobResult("stub-transcription-job"));
    }

    public async IAsyncEnumerable<Application.JobStatusUpdate> StreamJobStatusAsync(string jobId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public Task<Application.ChunkCommandResult> ChunkCommandAsync(string agent04JobId, Application.TranscriptionChunkAction action, int chunkIndex, string? jobDirectoryRelative = null, int splitParts = 0, CancellationToken ct = default) =>
        Task.FromResult(new Application.ChunkCommandResult(false, "stub_transcription_client"));
}
