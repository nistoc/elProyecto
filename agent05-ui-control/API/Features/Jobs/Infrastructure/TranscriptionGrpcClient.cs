using Agent04.Proto;
using Grpc.Net.Client;

namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class TranscriptionGrpcClient : Application.ITranscriptionServiceClient
{
    private readonly string _address;
    private readonly string _configPath;
    private readonly ILogger<TranscriptionGrpcClient> _logger;

    public TranscriptionGrpcClient(IConfiguration configuration, ILogger<TranscriptionGrpcClient> logger)
    {
        _address = configuration["Agent04:GrpcAddress"] ?? "http://localhost:5001";
        _configPath = configuration["Agent04:ConfigPath"] ?? "config/default.json";
        _logger = logger;
    }

    public async Task<Application.SubmitJobResult> SubmitJobAsync(string configPath, string inputFilePath, IReadOnlyList<string>? tags, CancellationToken ct = default)
    {
        using var channel = GrpcChannel.ForAddress(_address);
        var client = new TranscriptionService.TranscriptionServiceClient(channel);
        var request = new SubmitJobRequest
        {
            ConfigPath = configPath,
            InputFilePath = inputFilePath
        };
        if (tags != null)
            request.Tags.AddRange(tags);

        var response = await client.SubmitJobAsync(request, cancellationToken: ct);
        _logger.LogInformation("Agent04 SubmitJob returned job_id={JobId}", response.JobId);
        return new Application.SubmitJobResult(response.JobId);
    }

    public async IAsyncEnumerable<Application.JobStatusUpdate> StreamJobStatusAsync(string jobId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var channel = GrpcChannel.ForAddress(_address);
        var client = new TranscriptionService.TranscriptionServiceClient(channel);
        var request = new StreamJobStatusRequest { JobId = jobId };
        var stream = client.StreamJobStatus(request, cancellationToken: ct);
        while (await stream.ResponseStream.MoveNext(ct))
        {
            var update = stream.ResponseStream.Current;
            yield return new Application.JobStatusUpdate(
                update.JobId,
                update.State,
                update.ProgressPercent,
                update.CurrentPhase,
                update.TotalChunks,
                update.ProcessedChunks,
                update.MdOutputPath,
                update.JsonOutputPath,
                update.ErrorMessage
            );
        }
    }

    public async Task<Application.ChunkCommandResult> ChunkCommandAsync(
        string agent04JobId,
        Application.TranscriptionChunkAction action,
        int chunkIndex,
        CancellationToken ct = default)
    {
        using var channel = GrpcChannel.ForAddress(_address);
        var client = new TranscriptionService.TranscriptionServiceClient(channel);
        var protoAction = (ChunkCommandAction)(int)action;
        var response = await client.ChunkCommandAsync(
            new ChunkCommandRequest { JobId = agent04JobId, Action = protoAction, ChunkIndex = chunkIndex },
            cancellationToken: ct);
        return new Application.ChunkCommandResult(response.Ok, response.Message ?? "");
    }
}
