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

    public TranscriptionGrpcService(ITranscriptionPipeline pipeline, IJobStatusStore store)
    {
        _pipeline = pipeline;
        _store = store;
    }

    public override async Task<SubmitJobResponse> SubmitJob(Agent04.Proto.SubmitJobRequest request, ServerCallContext context)
    {
        var configPath = request.ConfigPath ?? "config/default.json";
        if (!File.Exists(configPath))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Config file not found"));

        var config = await TranscriptionConfig.FromFileAsync(configPath, context.CancellationToken);
        var files = config.GetFiles();
        var rawPath = request.InputFilePath ?? (files.Count > 0 ? files[0] : null);
        if (string.IsNullOrEmpty(rawPath))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Input file not specified"));
        var inputPath = Path.IsPathRooted(rawPath)
            ? rawPath
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".", rawPath);
        if (!File.Exists(inputPath))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Input file not found"));

        var jobId = _store.Create();
        _ = RunJobAsync(jobId, config, inputPath, context.CancellationToken);
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

    private async Task RunJobAsync(string jobId, TranscriptionConfig config, string inputPath, CancellationToken cancellationToken)
    {
        try
        {
            await _pipeline.ProcessFileAsync(config, inputPath, jobId, _store, cancellationToken);
        }
        catch (Exception ex)
        {
            _store.Update(jobId, new Agent04.Features.Transcription.Application.JobStatusUpdate { State = JobState.Failed, ErrorMessage = ex.Message });
        }
    }
}
