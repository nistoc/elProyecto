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
}
