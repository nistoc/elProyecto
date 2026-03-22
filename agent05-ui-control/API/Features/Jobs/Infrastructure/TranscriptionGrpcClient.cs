using Agent04.Proto;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

using Google.Protobuf.Collections;

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

        _logger.LogInformation(
            "Agent04 SubmitJob → {Address}: ConfigPath={Config}, InputFilePath={Input}, TagCount={Tags}",
            _address, configPath, inputFilePath, tags?.Count ?? 0);
        try
        {
            var response = await client.SubmitJobAsync(request, cancellationToken: ct);
            _logger.LogInformation("Agent04 SubmitJob OK: agent04_job_id={JobId}", response.JobId);
            return new Application.SubmitJobResult(response.JobId);
        }
        catch (RpcException ex)
        {
            _logger.LogError(
                "Agent04 SubmitJob RPC failed: StatusCode={Code}, Detail={Detail}, Address={Address}, ConfigPath={Config}, InputFilePath={Input}",
                ex.StatusCode, ex.Status.Detail, _address, configPath, inputFilePath);
            throw;
        }
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
            var vm = MapChunkVirtualModel(update.ChunkVirtualModel);
            yield return new Application.JobStatusUpdate(
                update.JobId,
                update.State,
                update.ProgressPercent,
                update.CurrentPhase,
                update.TotalChunks,
                update.ProcessedChunks,
                update.MdOutputPath,
                update.JsonOutputPath,
                update.ErrorMessage,
                vm,
                string.IsNullOrEmpty(update.TranscriptionFooterHint) ? null : update.TranscriptionFooterHint);
        }
    }

    public async Task<Application.JobStatusUpdate?> GetJobStatusAsync(string agent04JobId, CancellationToken ct = default)
    {
        using var channel = GrpcChannel.ForAddress(_address);
        var client = new TranscriptionService.TranscriptionServiceClient(channel);
        try
        {
            var resp = await client.GetJobStatusAsync(new GetJobStatusRequest { JobId = agent04JobId }, cancellationToken: ct)
                .ConfigureAwait(false);
            var vm = MapChunkVirtualModel(resp.ChunkVirtualModel);
            return new Application.JobStatusUpdate(
                resp.JobId,
                resp.State,
                resp.ProgressPercent,
                resp.CurrentPhase,
                resp.TotalChunks,
                resp.ProcessedChunks,
                resp.MdOutputPath,
                resp.JsonOutputPath,
                resp.ErrorMessage,
                vm,
                string.IsNullOrEmpty(resp.TranscriptionFooterHint) ? null : resp.TranscriptionFooterHint);
        }
        catch (RpcException ex)
        {
            _logger.LogDebug(ex, "Agent04 GetJobStatus failed for {JobId}", agent04JobId);
            return null;
        }
    }

    private static IReadOnlyList<Application.ChunkVirtualModelEntry>? MapChunkVirtualModel(
        RepeatedField<ChunkVirtualModelEntry> entries)
    {
        if (entries.Count == 0)
            return null;
        return entries.Select(e => new Application.ChunkVirtualModelEntry
        {
            Index = e.ChunkIndex,
            StartedAt = string.IsNullOrEmpty(e.StartedAt) ? null : e.StartedAt,
            CompletedAt = string.IsNullOrEmpty(e.CompletedAt) ? null : e.CompletedAt,
            State = string.IsNullOrEmpty(e.State) ? "Pending" : e.State,
            ErrorMessage = string.IsNullOrEmpty(e.ErrorMessage) ? null : e.ErrorMessage,
            IsSubChunk = e.IsSubChunk,
            ParentChunkIndex = e.ParentChunkIndex,
            SubChunkIndex = e.SubChunkIndex,
            TranscriptActivityLog = string.IsNullOrEmpty(e.TranscriptActivityLog) ? null : e.TranscriptActivityLog
        }).ToList();
    }

    public async Task<Application.ChunkCommandResult> ChunkCommandAsync(
        string agent04JobId,
        Application.TranscriptionChunkAction action,
        int chunkIndex,
        string? jobDirectoryRelative,
        int splitParts = 0,
        int subChunkIndex = 0,
        CancellationToken ct = default)
    {
        using var channel = GrpcChannel.ForAddress(_address);
        var client = new TranscriptionService.TranscriptionServiceClient(channel);
        var protoAction = (ChunkCommandAction)(int)action;
        var response = await client.ChunkCommandAsync(
            new ChunkCommandRequest
            {
                JobId = agent04JobId,
                Action = protoAction,
                ChunkIndex = chunkIndex,
                JobDirectoryRelative = jobDirectoryRelative ?? "",
                SplitParts = splitParts,
                SubChunkIndex = subChunkIndex
            },
            cancellationToken: ct);
        return new Application.ChunkCommandResult(response.Ok, response.Message ?? "");
    }
}
