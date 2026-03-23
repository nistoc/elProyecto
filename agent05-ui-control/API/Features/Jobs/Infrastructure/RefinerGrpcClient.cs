using Grpc.Net.Client;
using TranslationImprover.Proto;

namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class RefinerGrpcClient : Application.IRefinerServiceClient
{
    private readonly string _address;
    private readonly ILogger<RefinerGrpcClient> _logger;

    public RefinerGrpcClient(IConfiguration configuration, ILogger<RefinerGrpcClient> logger)
    {
        _address = configuration["Agent06:GrpcAddress"] ?? "http://localhost:5002";
        _logger = logger;
    }

    public async Task<Application.SubmitRefineJobResult> SubmitRefineJobAsync(Application.RefineJobInput input, CancellationToken ct = default)
    {
        using var channel = GrpcChannel.ForAddress(_address);
        var client = new RefinerService.RefinerServiceClient(channel);
        var request = new SubmitRefineJobRequest
        {
            InputFilePath = input.InputFilePath ?? "",
            InputContent = input.InputContent ?? "",
            OutputFilePath = input.OutputFilePath ?? "",
            BatchSize = input.BatchSize,
            ContextLines = input.ContextLines,
            JobDirectoryRelative = input.JobDirectoryRelative ?? ""
        };
        if (input.Tags != null)
            request.Tags.AddRange(input.Tags);

        var response = await client.SubmitRefineJobAsync(request, cancellationToken: ct);
        var jobId = response.JobId;
        _logger.LogInformation("Agent06 SubmitRefineJob returned job_id={JobId}", jobId);
        return new Application.SubmitRefineJobResult(jobId);
    }

    public async IAsyncEnumerable<Application.RefineStatusUpdate> StreamRefineStatusAsync(string jobId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var channel = GrpcChannel.ForAddress(_address);
        var client = new RefinerService.RefinerServiceClient(channel);
        var request = new StreamRefineStatusRequest { JobId = jobId };
        var stream = client.StreamRefineStatus(request, cancellationToken: ct);
        while (await stream.ResponseStream.MoveNext(ct))
        {
            var update = stream.ResponseStream.Current;
            yield return new Application.RefineStatusUpdate(
                update.JobId,
                update.State,
                update.ProgressPercent,
                update.CurrentBatch,
                update.TotalBatches,
                update.OutputFilePath,
                update.ErrorMessage
            );
            if (update.State is "Completed" or "Failed" or "Cancelled")
                break;
        }
    }

    public async Task<bool> CancelRefineJobAsync(string jobId, CancellationToken ct = default)
    {
        using var channel = GrpcChannel.ForAddress(_address);
        var client = new RefinerService.RefinerServiceClient(channel);
        var request = new CancelRefineJobRequest { JobId = jobId };
        var response = await client.CancelRefineJobAsync(request, cancellationToken: ct);
        return response.Cancelled;
    }
}
