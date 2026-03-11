using Agent04.Application;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Agent04.Proto;
using Grpc.Core;
using JobState = Agent04.Features.Transcription.Application.JobState;

namespace Agent04.Services;

public class TranscriptionGrpcService : TranscriptionService.TranscriptionServiceBase
{
    private readonly ITranscriptionPipeline _pipeline;
    private readonly IJobStatusStore _store;
    private readonly WorkspaceRoot _workspaceRoot;
    private readonly INodeModel? _nodeModel;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IOutboundJobNotifier? _outboundNotifier;

    public TranscriptionGrpcService(ITranscriptionPipeline pipeline, IJobStatusStore store, WorkspaceRoot workspaceRoot, INodeModel? nodeModel = null, IHttpClientFactory? httpClientFactory = null, IOutboundJobNotifier? outboundNotifier = null)
    {
        _pipeline = pipeline;
        _store = store;
        _workspaceRoot = workspaceRoot;
        _nodeModel = nodeModel;
        _httpClientFactory = httpClientFactory;
        _outboundNotifier = outboundNotifier;
    }

    public override async Task<SubmitJobResponse> SubmitJob(Agent04.Proto.SubmitJobRequest request, ServerCallContext context)
    {
        var root = _workspaceRoot.RootPath;
        var configPathRel = (request.ConfigPath ?? "config/default.json").Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configPathFull = Path.Combine(root, configPathRel);
        if (!File.Exists(configPathFull))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Config file not found"));

        var config = await TranscriptionConfig.FromFileAsync(configPathFull, context.CancellationToken);
        var files = config.GetFiles();
        var rawPath = request.InputFilePath ?? (files.Count > 0 ? files[0] : null);
        if (string.IsNullOrEmpty(rawPath))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Input file not specified"));
        rawPath = rawPath.Trim();
        if (Path.IsPathRooted(rawPath))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "input_file_path must be relative to workspace_root; absolute paths are not allowed"));

        var inputPathRel = rawPath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var inputPathFull = Path.Combine(root, inputPathRel);
        if (!File.Exists(inputPathFull))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Input file not found"));

        var tags = request.Tags?.Count > 0 ? request.Tags.ToList() : null;
        var callbackUrl = !string.IsNullOrWhiteSpace(request.CallbackUrl) ? request.CallbackUrl.Trim() : null;
        var jobId = _store.Create(tags, callbackUrl);
        _ = RunJobAsync(jobId, config, inputPathFull, root, context.CancellationToken);
        return new SubmitJobResponse { JobId = jobId };
    }

    public override Task<QueryJobsResponse> QueryJobs(QueryJobsRequest request, ServerCallContext context)
    {
        JobState? status = null;
        if (!string.IsNullOrEmpty(request.Status) && Enum.TryParse<JobState>(request.Status, ignoreCase: true, out var s))
            status = s;
        DateTimeOffset? from = null;
        if (!string.IsNullOrEmpty(request.From) && DateTimeOffset.TryParse(request.From, out var f))
            from = f;
        DateTimeOffset? to = null;
        if (!string.IsNullOrEmpty(request.To) && DateTimeOffset.TryParse(request.To, out var t))
            to = t;
        var filter = new JobListFilter
        {
            SemanticKey = !string.IsNullOrEmpty(request.SemanticKey) ? request.SemanticKey : null,
            Status = status,
            From = from,
            To = to,
            Limit = request.Limit > 0 ? request.Limit : 50,
            Offset = request.Offset >= 0 ? request.Offset : 0
        };
        var list = _store.List(filter);
        var response = new QueryJobsResponse();
        foreach (var job in list)
            response.Jobs.Add(ToResponse(job));
        return Task.FromResult(response);
    }

    public override Task<JobStatusResponse> GetJobStatus(GetJobStatusRequest request, ServerCallContext context)
    {
        var job = _store.Get(request.JobId);
        if (job == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Job not found"));
        return Task.FromResult(ToResponse(job));
    }

    public override async Task StreamJobStatus(StreamJobStatusRequest request, IServerStreamWriter<Agent04.Proto.JobStatusUpdate> responseStream, ServerCallContext context)
    {
        var jobId = request.JobId;
        var lastState = "";
        var lastUpdated = "";
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var job = _store.Get(jobId);
            if (job == null)
            {
                await responseStream.WriteAsync(new Agent04.Proto.JobStatusUpdate { JobId = jobId, State = "NotFound" });
                return;
            }
            var state = job.State.ToString();
            var updated = job.UpdatedAt.ToString("O");
            if (state != lastState || updated != lastUpdated)
            {
                lastState = state;
                lastUpdated = updated;
                await responseStream.WriteAsync(new Agent04.Proto.JobStatusUpdate
                {
                    JobId = job.JobId,
                    State = state,
                    ProgressPercent = job.ProgressPercent,
                    CurrentPhase = job.CurrentPhase ?? "",
                    TotalChunks = job.TotalChunks,
                    ProcessedChunks = job.ProcessedChunks,
                    UpdatedAt = updated,
                    MdOutputPath = job.MdOutputPath ?? "",
                    JsonOutputPath = job.JsonOutputPath ?? "",
                    ErrorMessage = job.ErrorMessage ?? ""
                });
                if (job.State is JobState.Completed or JobState.Failed or JobState.Cancelled)
                    return;
            }
            await Task.Delay(500, context.CancellationToken);
        }
    }

    private static JobStatusResponse ToResponse(JobStatus job)
    {
        return new JobStatusResponse
        {
            JobId = job.JobId,
            State = job.State.ToString(),
            ProgressPercent = job.ProgressPercent,
            CurrentPhase = job.CurrentPhase ?? "",
            TotalChunks = job.TotalChunks,
            ProcessedChunks = job.ProcessedChunks,
            CreatedAt = job.CreatedAt.ToString("O"),
            StartedAt = job.StartedAt?.ToString("O") ?? "",
            CompletedAt = job.CompletedAt?.ToString("O") ?? "",
            UpdatedAt = job.UpdatedAt.ToString("O"),
            MdOutputPath = job.MdOutputPath ?? "",
            JsonOutputPath = job.JsonOutputPath ?? "",
            ErrorMessage = job.ErrorMessage ?? ""
        };
    }

    private async Task RunJobAsync(string jobId, TranscriptionConfig config, string inputPath, string workspaceRoot, CancellationToken cancellationToken)
    {
        try
        {
            await _pipeline.ProcessFileAsync(config, inputPath, workspaceRoot, jobId, _store, _nodeModel, cancellationToken);
        }
        catch (Exception ex)
        {
            _store.Update(jobId, new Agent04.Features.Transcription.Application.JobStatusUpdate { State = JobState.Failed, ErrorMessage = ex.Message });
        }
        var job = _store.Get(jobId);
        if (job != null && (job.State == JobState.Completed || job.State == JobState.Failed || job.State == JobState.Cancelled))
        {
            if (!string.IsNullOrEmpty(job.CallbackUrl))
                _ = FireCallbackAsync(job);
            _outboundNotifier?.NotifyJobCompletedAsync(job.JobId, job.State);
        }
    }

    private async Task FireCallbackAsync(JobStatus job)
    {
        if (_httpClientFactory == null) return;
        try
        {
            var client = _httpClientFactory.CreateClient();
            var json = System.Text.Json.JsonSerializer.Serialize(job);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await client.PostAsync(job.CallbackUrl!, content);
        }
        catch
        {
            // Fire-and-forget
        }
    }
}
