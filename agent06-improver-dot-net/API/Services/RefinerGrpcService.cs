using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TranslationImprover.Application;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;
using TranslationImprover.Features.Refine.Infrastructure;
using TranslationImprover.Proto;

namespace TranslationImprover.Services;

public class RefinerGrpcService : RefinerService.RefinerServiceBase
{
    private readonly IRefineJobStore _store;
    private readonly IRefinePipeline _pipeline;
    private readonly IRefineJobCancellation _cancellation;
    private readonly IRefineJobPause _pause;
    private readonly WorkspaceRoot _workspaceRoot;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RefinerGrpcService> _logger;

    public RefinerGrpcService(
        IRefineJobStore store,
        IRefinePipeline pipeline,
        IRefineJobCancellation cancellation,
        IRefineJobPause pause,
        WorkspaceRoot workspaceRoot,
        IConfiguration configuration,
        ILogger<RefinerGrpcService> logger)
    {
        _store = store;
        _pipeline = pipeline;
        _cancellation = cancellation;
        _pause = pause;
        _workspaceRoot = workspaceRoot;
        _configuration = configuration;
        _logger = logger;
    }

    public override Task<SubmitRefineJobResponse> SubmitRefineJob(SubmitRefineJobRequest request, ServerCallContext context)
    {
        _logger.LogInformation(
            "gRPC SubmitRefineJob: incoming JobDirectoryRelative={JobDir}, WorkspaceRootOverride={RootOverride}, InputFilePath={InFile}, InputContentLen={InLen}, OutputFilePath={Out}",
            request.JobDirectoryRelative?.Trim() ?? "",
            request.WorkspaceRootOverride?.Trim() ?? "",
            request.InputFilePath?.Trim() ?? "",
            request.InputContent?.Length ?? 0,
            request.OutputFilePath?.Trim() ?? "");

        string artifactBase;
        string? workspaceRootOverrideStored;
        try
        {
            (artifactBase, workspaceRootOverrideStored) = RefineArtifactBaseResolver.Resolve(
                _workspaceRoot.RootPath,
                string.IsNullOrWhiteSpace(request.WorkspaceRootOverride) ? null : request.WorkspaceRootOverride,
                string.IsNullOrWhiteSpace(request.JobDirectoryRelative) ? null : request.JobDirectoryRelative.Trim());
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        _logger.LogInformation("gRPC SubmitRefineJob: artifact base (shared jobs root)={ArtifactBase}", artifactBase);

        string artifactRoot;
        try
        {
            artifactRoot = RefineWorkspacePaths.ResolveEffectiveArtifactRoot(artifactBase,
                string.IsNullOrWhiteSpace(request.JobDirectoryRelative) ? null : request.JobDirectoryRelative.Trim());
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        if (string.IsNullOrEmpty(request.InputFilePath) && string.IsNullOrEmpty(request.InputContent))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Either input_file_path or input_content must be set"));

        if (!string.IsNullOrEmpty(request.InputFilePath))
        {
            var raw = request.InputFilePath.Trim();
            if (Path.IsPathRooted(raw))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "input_file_path must be relative to workspace_root; absolute paths are not allowed"));
            var rel = raw.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.Combine(artifactRoot, rel);
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
            Model = string.IsNullOrWhiteSpace(request.Model) ? "gpt-4o-mini" : request.Model.Trim(),
            Temperature = request.Temperature,
            PromptFile = string.IsNullOrEmpty(request.PromptFile) ? null : request.PromptFile.Trim(),
            OpenAIBaseUrl = string.IsNullOrEmpty(request.OpenaiBaseUrl) ? null : request.OpenaiBaseUrl.Trim(),
            OpenAIOrganization = string.IsNullOrEmpty(request.OpenaiOrganization) ? null : request.OpenaiOrganization.Trim(),
            SaveIntermediate = request.SaveIntermediate,
            IntermediateDir = string.IsNullOrEmpty(request.IntermediateDir) ? null : request.IntermediateDir.Trim(),
            CallbackUrl = string.IsNullOrEmpty(request.CallbackUrl) ? null : request.CallbackUrl.Trim(),
            Tags = request.Tags?.Count > 0 ? request.Tags.ToList() : null,
            JobDirectoryRelative = string.IsNullOrWhiteSpace(request.JobDirectoryRelative) ? null : request.JobDirectoryRelative.Trim(),
            WorkspaceRootOverride = workspaceRootOverrideStored
        };

        var tags = request.Tags?.Count > 0 ? request.Tags.ToList() : null;
        var callbackUrl = !string.IsNullOrWhiteSpace(request.CallbackUrl) ? request.CallbackUrl.Trim() : null;
        var jobId = _store.Create(tags, callbackUrl, req.JobDirectoryRelative, workspaceRootOverrideStored);
        _logger.LogInformation("gRPC SubmitRefineJob: accepted Agent06 refine job_id={JobId}", jobId);
        RefineFreshRunArtifactCleaner.ClearForNewSubmit(artifactRoot, _logger);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        _cancellation.Register(jobId, cts);
        _ = _pipeline.RunAsync(jobId, req, artifactRoot, cts.Token);
        return Task.FromResult(new SubmitRefineJobResponse { JobId = jobId });
    }

    public override Task<RefineStatusResponse> GetRefineStatus(GetRefineStatusRequest request, ServerCallContext context)
    {
        var job = _store.Get(request.JobId);
        if (job == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Job not found"));
        return Task.FromResult(ToResponse(job));
    }

    public override async Task StreamRefineStatus(
        StreamRefineStatusRequest request,
        IServerStreamWriter<RefineStatusResponse> responseStream,
        ServerCallContext context)
    {
        var jobId = request.JobId;
        var pollMs = _configuration.GetValue("Agent06:StreamRefineStatusPollMs", 400);
        if (pollMs < 100)
            pollMs = 100;

        var sentBootstrap = false;

        while (!context.CancellationToken.IsCancellationRequested)
        {
            var drainedAny = false;
            while (_store.TryDequeueStreamSnapshot(jobId, out var snap) && snap != null)
            {
                drainedAny = true;
                await responseStream.WriteAsync(ToResponse(snap), context.CancellationToken).ConfigureAwait(false);
                if (snap.State is RefineJobState.Paused or RefineJobState.Completed or RefineJobState.Failed or RefineJobState.Cancelled)
                    return;
            }

            var job = _store.Get(jobId);
            if (job == null)
            {
                await responseStream.WriteAsync(new RefineStatusResponse { JobId = jobId, State = "NotFound" }, context.CancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            if (!sentBootstrap && !drainedAny)
            {
                sentBootstrap = true;
                await responseStream.WriteAsync(ToResponse(job), context.CancellationToken).ConfigureAwait(false);
                if (job.State is RefineJobState.Paused or RefineJobState.Completed or RefineJobState.Failed or RefineJobState.Cancelled)
                    return;
            }
            else if (!drainedAny && job.State is RefineJobState.Paused or RefineJobState.Completed or RefineJobState.Failed or RefineJobState.Cancelled)
                return;

            try
            {
                await Task.Delay(pollMs, context.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    public override Task<CancelRefineJobResponse> CancelRefineJob(CancelRefineJobRequest request, ServerCallContext context)
    {
        var job = _store.Get(request.JobId);
        if (job == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Job not found"));
        if (job.State != RefineJobState.Running && job.State != RefineJobState.Pending && job.State != RefineJobState.Paused)
            return Task.FromResult(new CancelRefineJobResponse { Cancelled = false });
        var cancelled = _cancellation.TryCancel(request.JobId);
        if (cancelled)
            _store.Update(request.JobId, new RefineJobStatusUpdate { State = RefineJobState.Cancelled, ErrorMessage = "Cancelled" });
        return Task.FromResult(new CancelRefineJobResponse { Cancelled = cancelled });
    }

    public override Task<PauseRefineJobResponse> PauseRefineJob(PauseRefineJobRequest request, ServerCallContext context)
    {
        _logger.LogInformation("gRPC PauseRefineJob: JobId={JobId}", request.JobId);
        var job = _store.Get(request.JobId);
        if (job == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Job not found"));
        if (job.State == RefineJobState.Paused)
            return Task.FromResult(new PauseRefineJobResponse { PauseRequested = true });
        if (job.State != RefineJobState.Running && job.State != RefineJobState.Pending)
        {
            _logger.LogWarning(
                "gRPC PauseRefineJob: declined JobId={JobId} (state={State}, expected Running or Pending)",
                request.JobId,
                job.State);
            return Task.FromResult(new PauseRefineJobResponse { PauseRequested = false });
        }
        _pause.RequestPause(request.JobId);
        return Task.FromResult(new PauseRefineJobResponse { PauseRequested = true });
    }

    public override Task<ResumeRefineJobResponse> ResumeRefineJob(ResumeRefineJobRequest request, ServerCallContext context)
    {
        var job = _store.Get(request.JobId);
        if (job == null)
            throw new RpcException(new Status(StatusCode.NotFound, "Job not found"));
        if (job.State != RefineJobState.Paused)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Job is not paused"));
        var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        _cancellation.Register(request.JobId, cts);
        _ = _pipeline.ResumeAsync(request.JobId, cts.Token);
        return Task.FromResult(new ResumeRefineJobResponse { Started = true });
    }

    public override Task<ResumeRefineFromCheckpointResponse> ResumeRefineFromCheckpoint(
        ResumeRefineFromCheckpointRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.JobDirectoryRelative))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "job_directory_relative is required"));

        string artifactBase;
        string? workspaceRootOverrideStored;
        try
        {
            (artifactBase, workspaceRootOverrideStored) = RefineArtifactBaseResolver.Resolve(
                _workspaceRoot.RootPath,
                string.IsNullOrWhiteSpace(request.WorkspaceRootOverride) ? null : request.WorkspaceRootOverride.Trim(),
                request.JobDirectoryRelative.Trim());
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        string artifactRoot;
        try
        {
            artifactRoot = RefineWorkspacePaths.ResolveEffectiveArtifactRoot(artifactBase, request.JobDirectoryRelative.Trim());
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        var checkpointPath = RefinePaths.CheckpointFile(artifactRoot);
        var ck = RefineCheckpoint.TryLoad(checkpointPath);
        if (ck == null)
            throw new RpcException(new Status(StatusCode.NotFound, "checkpoint.json not found or unreadable"));

        var modelReq = ck.ToRequest();
        var batches = TranscriptParser.CreateBatches(ck.ContentLines, modelReq.BatchSize > 0 ? modelReq.BatchSize : 10);
        if (batches.Count == 0 || ck.NextBatchIndex >= batches.Count)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "No batches left to resume"));

        var tags = ck.Request.Tags is { Count: > 0 } ? ck.Request.Tags : null;
        var jobId = _store.Create(tags, callbackUrl: null, request.JobDirectoryRelative.Trim(), workspaceRootOverrideStored);
        ck.JobId = jobId;
        ck.Save(checkpointPath);

        _logger.LogInformation(
            "gRPC ResumeRefineFromCheckpoint: new job_id={JobId}, nextBatch={Next}/{Total}, artifactRoot={Root}",
            jobId,
            ck.NextBatchIndex,
            batches.Count,
            artifactRoot);

        var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        _cancellation.Register(jobId, cts);
        _ = _pipeline.ResumeFromCheckpointAsync(jobId, artifactRoot, ck, cts.Token);
        return Task.FromResult(new ResumeRefineFromCheckpointResponse { JobId = jobId });
    }

    public override Task<QueryRefineJobsResponse> QueryRefineJobs(QueryRefineJobsRequest request, ServerCallContext context)
    {
        _logger.LogInformation("gRPC QueryRefineJobs: Status={Status}, Limit={Limit}", request.Status, request.Limit);
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
        var r = new RefineStatusResponse
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
            UpdatedAt = job.UpdatedAt.ToString("O"),
            StreamSequence = job.StreamSequence,
            BatchEventKind = job.BatchEventKind ?? "",
            BatchEventIndex0 = job.BatchEventIndex0,
            BatchThreadsRelativePath = job.BatchThreadsRelativePath ?? "",
            OpenaiRequestPreview = job.OpenAiRequestPreview ?? "",
            BatchBeforeText = job.BatchBeforeText ?? "",
            BatchAfterText = job.BatchAfterText ?? "",
            RefinerLogLine = job.RefinerLogLine ?? ""
        };
        foreach (var t in job.Tags ?? Array.Empty<string>())
            r.Tags.Add(t);
        return r;
    }

}
