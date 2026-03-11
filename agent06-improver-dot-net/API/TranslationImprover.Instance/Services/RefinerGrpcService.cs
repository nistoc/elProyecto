using Grpc.Core;
using TranslationImprover.Application;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;
using TranslationImprover.Proto;

namespace TranslationImprover.Services;

public class RefinerGrpcService : RefinerService.RefinerServiceBase
{
    private readonly IRefineJobStore _store;
    private readonly IRefinePipeline _pipeline;
    private readonly IRefineJobCancellation _cancellation;
    private readonly WorkspaceRoot _workspaceRoot;

    public RefinerGrpcService(IRefineJobStore store, IRefinePipeline pipeline, IRefineJobCancellation cancellation, WorkspaceRoot workspaceRoot)
    {
        _store = store;
        _pipeline = pipeline;
        _cancellation = cancellation;
        _workspaceRoot = workspaceRoot;
    }

    public override async Task<SubmitRefineJobResponse> SubmitRefineJob(SubmitRefineJobRequest request, ServerCallContext context)
    {
        var root = _workspaceRoot.RootPath;

        if (string.IsNullOrEmpty(request.InputFilePath) && string.IsNullOrEmpty(request.InputContent))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Either input_file_path or input_content must be set"));

        if (!string.IsNullOrEmpty(request.InputFilePath))
        {
            var raw = request.InputFilePath.Trim();
            if (Path.IsPathRooted(raw))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "input_file_path must be relative to workspace_root; absolute paths are not allowed"));
            var rel = raw.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.Combine(root, rel);
            if (!File.Exists(full))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Input file not found"));
        }

        if (!string.IsNullOrEmpty(request.OutputFilePath))
        {
            var raw = request.OutputFilePath.Trim();
            if (Path.IsPathRooted(raw))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "output_file_path must be relative to workspace_root"));
        }

        var req = new RefineJobRequest
        {
            InputFilePath = string.IsNullOrEmpty(request.InputFilePath) ? null : request.InputFilePath.Trim(),
            InputContent = string.IsNullOrEmpty(request.InputContent) ? null : request.InputContent,
            OutputFilePath = string.IsNullOrEmpty(request.OutputFilePath) ? null : request.OutputFilePath.Trim(),
            BatchSize = request.BatchSize > 0 ? request.BatchSize : 10,
            ContextLines = request.ContextLines >= 0 ? request.ContextLines : 3,
            Model = request.Model ?? "gpt-4o-mini",
            Temperature = request.Temperature,
            PromptFile = string.IsNullOrEmpty(request.PromptFile) ? null : request.PromptFile.Trim(),
            OpenAIBaseUrl = string.IsNullOrEmpty(request.OpenaiBaseUrl) ? null : request.OpenaiBaseUrl.Trim(),
            OpenAIOrganization = string.IsNullOrEmpty(request.OpenaiOrganization) ? null : request.OpenaiOrganization.Trim(),
            SaveIntermediate = request.SaveIntermediate,
            IntermediateDir = string.IsNullOrEmpty(request.IntermediateDir) ? null : request.IntermediateDir.Trim(),
            CallbackUrl = string.IsNullOrEmpty(request.CallbackUrl) ? null : request.CallbackUrl.Trim(),
            Tags = request.Tags?.Count > 0 ? request.Tags.ToList() : null
        };

        var tags = request.Tags?.Count > 0 ? request.Tags.ToList() : null;
        var callbackUrl = !string.IsNullOrWhiteSpace(request.CallbackUrl) ? request.CallbackUrl.Trim() : null;
        var jobId = _store.Create(tags, callbackUrl);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        _cancellation.Register(jobId, cts);
        _ = _pipeline.RunAsync(jobId, req, root, cts.Token);
        return new SubmitRefineJobResponse { JobId = jobId };
    }

    public override Task<RefineStatusResponse> GetRefineStatus(GetRefineStatusRequest request, ServerCallContext context)
    {
        var job = _store.Get(request.JobId);
        if (job == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Job not found"));
        return Task.FromResult(ToResponse(job));
    }

    public override async Task StreamRefineStatus(StreamRefineStatusRequest request, IServerStreamWriter<RefineStatusUpdate> responseStream, ServerCallContext context)
    {
        var jobId = request.JobId;
        var delay = TimeSpan.FromMilliseconds(500);
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var job = _store.Get(jobId);
            if (job == null)
                throw new RpcException(new Status(StatusCode.NotFound, "Job not found"));
            await responseStream.WriteAsync(ToUpdate(job));
            if (job.State is RefineJobState.Completed or RefineJobState.Failed or RefineJobState.Cancelled)
                break;
            await Task.Delay(delay, context.CancellationToken);
        }
    }

    public override Task<CancelRefineJobResponse> CancelRefineJob(CancelRefineJobRequest request, ServerCallContext context)
    {
        var job = _store.Get(request.JobId);
        if (job == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Job not found"));
        if (job.State != RefineJobState.Running && job.State != RefineJobState.Pending)
            return Task.FromResult(new CancelRefineJobResponse { Cancelled = false });
        var cancelled = _cancellation.TryCancel(request.JobId);
        if (cancelled)
            _store.Update(request.JobId, new RefineJobStatusUpdate { State = RefineJobState.Cancelled, ErrorMessage = "Cancelled" });
        return Task.FromResult(new CancelRefineJobResponse { Cancelled = cancelled });
    }

    public override Task<QueryRefineJobsResponse> QueryRefineJobs(QueryRefineJobsRequest request, ServerCallContext context)
    {
        RefineJobState? status = null;
        if (!string.IsNullOrEmpty(request.Status) && Enum.TryParse<RefineJobState>(request.Status, ignoreCase: true, out var s))
            status = s;
        DateTimeOffset? from = null;
        if (!string.IsNullOrEmpty(request.From) && DateTimeOffset.TryParse(request.From, out var f))
            from = f;
        DateTimeOffset? to = null;
        if (!string.IsNullOrEmpty(request.To) && DateTimeOffset.TryParse(request.To, out var t))
            to = t;
        var filter = new RefineJobListFilter
        {
            SemanticKey = string.IsNullOrEmpty(request.SemanticKey) ? null : request.SemanticKey,
            Status = status,
            From = from,
            To = to,
            Limit = request.Limit > 0 ? request.Limit : 50,
            Offset = request.Offset >= 0 ? request.Offset : 0
        };
        var list = _store.List(filter);
        var response = new QueryRefineJobsResponse();
        foreach (var job in list)
            response.Jobs.Add(ToResponse(job));
        return Task.FromResult(response);
    }

    private static RefineStatusResponse ToResponse(RefineJobStatus job)
    {
        return new RefineStatusResponse
        {
            JobId = job.JobId,
            State = job.State.ToString(),
            ProgressPercent = job.ProgressPercent,
            CurrentPhase = job.CurrentPhase ?? "",
            CurrentBatch = job.CurrentBatch,
            TotalBatches = job.TotalBatches,
            OutputFilePath = job.OutputFilePath ?? "",
            ErrorMessage = job.ErrorMessage ?? "",
            CreatedAt = job.CreatedAt.ToString("O"),
            StartedAt = job.StartedAt?.ToString("O") ?? "",
            CompletedAt = job.CompletedAt?.ToString("O") ?? "",
            UpdatedAt = job.UpdatedAt.ToString("O")
        };
    }

    private static RefineStatusUpdate ToUpdate(RefineJobStatus job)
    {
        return new RefineStatusUpdate
        {
            JobId = job.JobId,
            State = job.State.ToString(),
            ProgressPercent = job.ProgressPercent,
            CurrentPhase = job.CurrentPhase ?? "",
            CurrentBatch = job.CurrentBatch,
            TotalBatches = job.TotalBatches,
            OutputFilePath = job.OutputFilePath ?? "",
            ErrorMessage = job.ErrorMessage ?? "",
            UpdatedAt = job.UpdatedAt.ToString("O")
        };
    }
}
