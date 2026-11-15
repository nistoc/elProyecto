using Grpc.Net.Client;
using TranslationImprover.Proto;

namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class RefinerGrpcClient : Application.IRefinerServiceClient, IDisposable
{
    private readonly string _address;
    private readonly ILogger<RefinerGrpcClient> _logger;
    private readonly GrpcChannel _channel;
    private readonly RefinerService.RefinerServiceClient _client;

    public RefinerGrpcClient(IConfiguration configuration, ILogger<RefinerGrpcClient> logger)
    {
        _address = configuration["Agent06:GrpcAddress"] ?? "http://localhost:5002";
        _logger = logger;
        _channel = GrpcChannelFactory.CreateKeepAliveChannel(_address);
        _client = new RefinerService.RefinerServiceClient(_channel);
    }

    public void Dispose() => _channel.Dispose();

    public async Task<Application.SubmitRefineJobResult> SubmitRefineJobAsync(Application.RefineJobInput input, CancellationToken ct = default)
    {
        var request = new SubmitRefineJobRequest
        {
            InputFilePath = input.InputFilePath ?? "",
            InputContent = input.InputContent ?? "",
            OutputFilePath = input.OutputFilePath ?? "",
            BatchSize = input.BatchSize,
            ContextLines = input.ContextLines,
            JobDirectoryRelative = input.JobDirectoryRelative ?? "",
            PromptFile = input.PromptFile ?? "",
            SystemPrompt = input.SystemPrompt ?? "",
            SourceName = input.SourceName ?? "",
            RespellingOnly = input.RespellingOnly,
            Model = input.Model ?? "",
            SaveIntermediate = !string.IsNullOrEmpty(input.IntermediateDir),
            IntermediateDir = input.IntermediateDir ?? ""
        };
        if (input.Tags != null)
            request.Tags.AddRange(input.Tags);

        var response = await _client.SubmitRefineJobAsync(request, cancellationToken: ct);
        var jobId = response.JobId;
        _logger.LogInformation("Agent06 SubmitRefineJob returned job_id={JobId}", jobId);
        return new Application.SubmitRefineJobResult(jobId);
    }

    public async IAsyncEnumerable<Application.RefineStatusUpdate> StreamRefineStatusAsync(string jobId, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new StreamRefineStatusRequest { JobId = jobId };
        var stream = _client.StreamRefineStatus(request, cancellationToken: ct);
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
                update.ErrorMessage,
                string.IsNullOrEmpty(update.PromptPreview) ? null : update.PromptPreview,
                update.Batches.Count > 0
                    ? update.Batches.Select(b => new Application.RefineBatchDelta(
                        b.Index, b.Total, b.OriginalText, b.RefinedText, b.Ok,
                        string.IsNullOrEmpty(b.Error) ? null : b.Error,
                        b.ContextText, b.StartLine, b.EndLine)).ToList()
                    : null
            );
            if (update.State is "Completed" or "Failed" or "Cancelled")
                break;
        }
    }

    public async Task<bool> CancelRefineJobAsync(string jobId, CancellationToken ct = default)
    {
        var request = new CancelRefineJobRequest { JobId = jobId };
        var response = await _client.CancelRefineJobAsync(request, cancellationToken: ct);
        return response.Cancelled;
    }

    public async Task<Application.RefineDefaults> GetDefaultsAsync(CancellationToken ct = default)
    {
        var r = await _client.GetRefineDefaultsAsync(new GetRefineDefaultsRequest(), cancellationToken: ct);
        return new Application.RefineDefaults(r.SystemPrompt, r.UserPromptTemplate, r.Model);
    }
}
