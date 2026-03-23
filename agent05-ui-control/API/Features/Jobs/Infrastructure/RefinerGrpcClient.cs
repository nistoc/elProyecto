using System.Net.Http;
using Grpc.Net.Client;
using TranslationImprover.Proto;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class RefinerGrpcClient : Application.IRefinerServiceClient
{
    private readonly string _address;
    private readonly IJobWorkspace _workspace;
    private readonly ILogger<RefinerGrpcClient> _logger;

    public RefinerGrpcClient(
        IConfiguration configuration,
        IJobWorkspace workspace,
        ILogger<RefinerGrpcClient> logger)
    {
        _address = configuration["Agent06:GrpcAddress"] ?? "http://localhost:5033";
        _workspace = workspace;
        _logger = logger;
    }

    /// <summary>
    /// No auto-redirect: a stray 3xx (e.g. HTTPS middleware) breaks HTTP/2 gRPC (HTTP_1_1_REQUIRED).
    /// </summary>
    private static GrpcChannel CreateChannel(string address) =>
        GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = new HttpClientHandler { AllowAutoRedirect = false },
            MaxReceiveMessageSize = 32 * 1024 * 1024,
            MaxSendMessageSize = 32 * 1024 * 1024
        });

    public async Task<Application.SubmitRefineJobResult> SubmitRefineJobAsync(Application.RefineJobInput input, CancellationToken ct = default)
    {
        using var channel = CreateChannel(_address);
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
        var workspaceRoot =
            string.IsNullOrWhiteSpace(input.WorkspaceRootOverride)
                ? _workspace.WorkspaceRootPath
                : input.WorkspaceRootOverride.Trim();
        if (!string.IsNullOrWhiteSpace(workspaceRoot))
            request.WorkspaceRootOverride = workspaceRoot;

        if (input.Tags != null)
            request.Tags.AddRange(input.Tags);

        _logger.LogInformation(
            "Calling Agent06 gRPC at {Address} SubmitRefineJob (JobDirectoryRelative={JobDir}, WorkspaceRootOverride={Root}, contentLen={Len})",
            _address,
            input.JobDirectoryRelative ?? "",
            workspaceRoot ?? "",
            input.InputContent?.Length ?? 0);

        var response = await client.SubmitRefineJobAsync(request, cancellationToken: ct);
        var jobId = response.JobId;
        _logger.LogInformation("Agent06 SubmitRefineJob returned job_id={JobId}", jobId);
        return new Application.SubmitRefineJobResult(jobId);
    }

    public async Task<Application.RefineStatusUpdate> GetRefineStatusAsync(string jobId, CancellationToken ct = default)
    {
        using var channel = CreateChannel(_address);
        var client = new RefinerService.RefinerServiceClient(channel);
        var request = new GetRefineStatusRequest { JobId = jobId };
        var r = await client.GetRefineStatusAsync(request, cancellationToken: ct);
        return MapStatus(r);
    }

    public async IAsyncEnumerable<Application.RefineStatusUpdate> StreamRefineStatusAsync(
        string jobId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var channel = CreateChannel(_address);
        var client = new RefinerService.RefinerServiceClient(channel);
        using var call = client.StreamRefineStatus(new StreamRefineStatusRequest { JobId = jobId }, cancellationToken: ct);
        while (await call.ResponseStream.MoveNext(ct).ConfigureAwait(false))
            yield return MapStatus(call.ResponseStream.Current);
    }

    private static Application.RefineStatusUpdate MapStatus(RefineStatusResponse r) =>
        new(
            r.JobId,
            r.State,
            r.ProgressPercent,
            string.IsNullOrEmpty(r.CurrentPhase) ? null : r.CurrentPhase,
            r.CurrentBatch,
            r.TotalBatches,
            string.IsNullOrEmpty(r.OutputFilePath) ? null : r.OutputFilePath,
            string.IsNullOrEmpty(r.ErrorMessage) ? null : r.ErrorMessage,
            string.IsNullOrEmpty(r.UpdatedAt) ? null : r.UpdatedAt,
            r.StreamSequence,
            string.IsNullOrEmpty(r.BatchEventKind) ? null : r.BatchEventKind,
            r.BatchEventIndex0,
            string.IsNullOrEmpty(r.BatchThreadsRelativePath) ? null : r.BatchThreadsRelativePath,
            string.IsNullOrEmpty(r.OpenaiRequestPreview) ? null : r.OpenaiRequestPreview,
            string.IsNullOrEmpty(r.BatchBeforeText) ? null : r.BatchBeforeText,
            string.IsNullOrEmpty(r.BatchAfterText) ? null : r.BatchAfterText,
            string.IsNullOrEmpty(r.RefinerLogLine) ? null : r.RefinerLogLine);

    public async Task<bool> CancelRefineJobAsync(string jobId, CancellationToken ct = default)
    {
        using var channel = CreateChannel(_address);
        var client = new RefinerService.RefinerServiceClient(channel);
        var request = new CancelRefineJobRequest { JobId = jobId };
        var response = await client.CancelRefineJobAsync(request, cancellationToken: ct);
        return response.Cancelled;
    }

    public async Task<bool> PauseRefineJobAsync(string jobId, CancellationToken ct = default)
    {
        using var channel = CreateChannel(_address);
        var client = new RefinerService.RefinerServiceClient(channel);
        var request = new PauseRefineJobRequest { JobId = jobId };
        var response = await client.PauseRefineJobAsync(request, cancellationToken: ct);
        return response.PauseRequested;
    }

    public async Task<bool> ResumeRefineJobAsync(string jobId, CancellationToken ct = default)
    {
        using var channel = CreateChannel(_address);
        var client = new RefinerService.RefinerServiceClient(channel);
        var request = new ResumeRefineJobRequest { JobId = jobId };
        var response = await client.ResumeRefineJobAsync(request, cancellationToken: ct);
        return response.Started;
    }

    public async Task<Application.SubmitRefineJobResult> ResumeRefineFromCheckpointAsync(
        string jobDirectoryRelative,
        string workspaceRootOverride,
        CancellationToken ct = default)
    {
        using var channel = CreateChannel(_address);
        var client = new RefinerService.RefinerServiceClient(channel);
        var request = new ResumeRefineFromCheckpointRequest
        {
            JobDirectoryRelative = jobDirectoryRelative ?? "",
            WorkspaceRootOverride = workspaceRootOverride ?? ""
        };
        _logger.LogInformation(
            "Calling Agent06 ResumeRefineFromCheckpoint (JobDir={JobDir}, WorkspaceRoot={Root})",
            jobDirectoryRelative ?? "",
            workspaceRootOverride ?? "");
        var response = await client.ResumeRefineFromCheckpointAsync(request, cancellationToken: ct);
        return new Application.SubmitRefineJobResult(response.JobId);
    }
}
