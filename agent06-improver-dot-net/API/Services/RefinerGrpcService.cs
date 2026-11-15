using Grpc.Core;
using TranslationImprover.Application;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;
using TranslationImprover.Features.Refine.Infrastructure;
using TranslationImprover.Proto;

namespace TranslationImprover.Services;

public class RefinerGrpcService : RefinerService.RefinerServiceBase
{
    private readonly IRefineJobStore _store;
    private readonly RefineJobSubmitter _submitter;
    private readonly IRefineJobCancellation _cancellation;

    public RefinerGrpcService(IRefineJobStore store, RefineJobSubmitter submitter, IRefineJobCancellation cancellation)
    {
        _store = store;
        _submitter = submitter;
        _cancellation = cancellation;
    }

    public override Task<SubmitRefineJobResponse> SubmitRefineJob(SubmitRefineJobRequest request, ServerCallContext context)
    {
        var result = _submitter.Submit(FromProto(request), context.CancellationToken);
        if (result.Error is { } error)
            throw new RpcException(new Status(StatusCode.InvalidArgument, error.Message));
        return Task.FromResult(new SubmitRefineJobResponse { JobId = result.JobId });
    }

    /// <summary>
    /// The message as a request, field for field; defaults and trimming are <see cref="RefineJobRequestMapper"/>'s,
    /// the same for the REST entry. <c>RefineJobRequestMapperTests</c> fails when a field of the message is left out.
    /// </summary>
    public static RefineJobRequest FromProto(SubmitRefineJobRequest request) => new()
    {
        InputFilePath = request.InputFilePath,
        InputContent = request.InputContent,
        OutputFilePath = request.OutputFilePath,
        BatchSize = request.BatchSize,
        ContextLines = request.ContextLines,
        Model = request.Model,
        Temperature = request.Temperature,
        PromptFile = request.PromptFile,
        SystemPrompt = request.SystemPrompt,
        SourceName = request.SourceName,
        RespellingOnly = request.RespellingOnly,
        OpenAIBaseUrl = request.OpenaiBaseUrl,
        OpenAIOrganization = request.OpenaiOrganization,
        SaveIntermediate = request.SaveIntermediate,
        IntermediateDir = request.IntermediateDir,
        CallbackUrl = request.CallbackUrl,
        Tags = request.Tags.ToList(),
        JobDirectoryRelative = request.JobDirectoryRelative
    };

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
        var sentBatches = 0;   // how many before/after deltas we have already streamed
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var job = _store.Get(jobId);
            if (job == null)
                throw new RpcException(new Status(StatusCode.NotFound, "Job not found"));
            var batches = job.Batches;   // immutable snapshot (copy-on-write in the store)
            var update = ToUpdate(job);
            // Send only batches not yet streamed on this connection; the consumer accumulates them.
            for (var i = sentBatches; i < batches.Count; i++)
                update.Batches.Add(ToProtoBatch(batches[i]));
            sentBatches = batches.Count;
            await responseStream.WriteAsync(update);
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
            UpdatedAt = job.UpdatedAt.ToString("O"),
            FailedBatches = job.Totals.FailedBatches,
            PromptTokens = job.Totals.PromptTokens,
            CompletionTokens = job.Totals.CompletionTokens,
            EditsLetThrough = job.Totals.EditsLetThrough,
            EditsPutBack = job.Totals.EditsPutBack
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
            UpdatedAt = job.UpdatedAt.ToString("O"),
            PromptPreview = job.PromptPreview ?? "",
            FailedBatches = job.Totals.FailedBatches,
            PromptTokens = job.Totals.PromptTokens,
            CompletionTokens = job.Totals.CompletionTokens,
            EditsLetThrough = job.Totals.EditsLetThrough,
            EditsPutBack = job.Totals.EditsPutBack
        };
    }

    private static Proto.RefineBatchDelta ToProtoBatch(Features.Refine.Domain.RefineBatchDelta d) => new()
    {
        Index = d.Index,
        Total = d.Total,
        OriginalText = d.OriginalText ?? "",
        RefinedText = d.RefinedText ?? "",
        Ok = d.Ok,
        Error = d.Error ?? "",
        ContextText = d.ContextText ?? "",
        StartLine = d.StartLine,
        EndLine = d.EndLine
    };

    public override Task<GetRefineDefaultsResponse> GetRefineDefaults(GetRefineDefaultsRequest request, ServerCallContext context) =>
        Task.FromResult(new GetRefineDefaultsResponse
        {
            SystemPrompt = Features.Refine.Infrastructure.RefinePipeline.SystemPrompt,
            UserPromptTemplate = Features.Refine.Infrastructure.FilePromptLoader.DefaultPrompt,
            Model = RefineJobRequestMapper.DefaultModel
        });
}
