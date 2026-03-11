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

    public TranscriptionGrpcService(ITranscriptionPipeline pipeline, IJobStatusStore store, WorkspaceRoot workspaceRoot, INodeModel? nodeModel = null)
    {
        _pipeline = pipeline;
        _store = store;
        _workspaceRoot = workspaceRoot;
        _nodeModel = nodeModel;
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
        var jobId = _store.Create(tags);
        _ = RunJobAsync(jobId, config, inputPathFull, root, context.CancellationToken);
        return new SubmitJobResponse { JobId = jobId };
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
    }
}
