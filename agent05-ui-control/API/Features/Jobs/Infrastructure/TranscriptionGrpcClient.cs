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

    public IAsyncEnumerable<Application.JobStatusUpdate> StreamJobStatusAsync(string jobId, CancellationToken ct = default) =>
        StreamJobStatusAsync(jobId, null, ct);

    public async IAsyncEnumerable<Application.JobStatusUpdate> StreamJobStatusAsync(
        string jobId,
        IReadOnlyList<Application.ChunkVirtualModelEntry>? clientChunkVirtualModel,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var channel = GrpcChannel.ForAddress(_address);
        var client = new TranscriptionService.TranscriptionServiceClient(channel);
        var request = new StreamJobStatusRequest { JobId = jobId };
        AppendClientChunkVirtualModel(request.ClientChunkVirtualModel, clientChunkVirtualModel);
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

    public Task<Application.JobStatusUpdate?> GetJobStatusAsync(string agent04JobId, CancellationToken ct = default) =>
        GetJobStatusAsync(agent04JobId, null, ct);

    public async Task<Application.JobStatusUpdate?> GetJobStatusAsync(
        string agent04JobId,
        IReadOnlyList<Application.ChunkVirtualModelEntry>? clientChunkVirtualModel,
        CancellationToken ct)
    {
        using var channel = GrpcChannel.ForAddress(_address);
        var client = new TranscriptionService.TranscriptionServiceClient(channel);
        try
        {
            var req = new GetJobStatusRequest { JobId = agent04JobId };
            AppendClientChunkVirtualModel(req.ClientChunkVirtualModel, clientChunkVirtualModel);
            var resp = await client.GetJobStatusAsync(req, cancellationToken: ct)
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

    private static void AppendClientChunkVirtualModel(
        RepeatedField<ChunkVirtualModelEntry> target,
        IReadOnlyList<Application.ChunkVirtualModelEntry>? client)
    {
        if (client is not { Count: > 0 })
            return;
        foreach (var e in client)
            target.Add(ToProtoChunkVirtualModelEntry(e));
    }

    private static ChunkVirtualModelEntry ToProtoChunkVirtualModelEntry(Application.ChunkVirtualModelEntry e) =>
        new()
        {
            ChunkIndex = e.Index,
            StartedAt = e.StartedAt ?? "",
            CompletedAt = e.CompletedAt ?? "",
            State = string.IsNullOrEmpty(e.State) ? "Pending" : e.State,
            ErrorMessage = e.ErrorMessage ?? "",
            IsSubChunk = e.IsSubChunk,
            ParentChunkIndex = e.ParentChunkIndex,
            SubChunkIndex = e.SubChunkIndex,
            TranscriptActivityLog = e.TranscriptActivityLog ?? ""
        };

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

    public async Task<Application.ChunkArtifactGroupsResult?> GetChunkArtifactGroupsAsync(
        string agent04JobId,
        string jobDirectoryRelative,
        int totalChunks,
        IReadOnlyList<Application.ChunkVirtualModelEntry>? clientChunkVirtualModel = null,
        CancellationToken ct = default)
    {
        using var channel = GrpcChannel.ForAddress(_address);
        var client = new TranscriptionService.TranscriptionServiceClient(channel);
        try
        {
            var req = new GetChunkArtifactGroupsRequest
            {
                JobId = agent04JobId,
                JobDirectoryRelative = jobDirectoryRelative ?? "",
                TotalChunks = totalChunks
            };
            AppendClientChunkVirtualModel(req.ClientChunkVirtualModel, clientChunkVirtualModel);
            var response = await client.GetChunkArtifactGroupsAsync(req, cancellationToken: ct)
                .ResponseAsync.ConfigureAwait(false);
            var groups = response.Groups.Select(MapChunkGroup).ToList();
            return new Application.ChunkArtifactGroupsResult { Groups = groups };
        }
        catch (RpcException ex)
        {
            _logger.LogDebug(
                ex,
                "Agent04 GetChunkArtifactGroups failed for {JobId}, dir={Dir}",
                agent04JobId,
                jobDirectoryRelative);
            return null;
        }
    }

    public async Task<Application.JobProjectFiles?> GetProjectFilesAsync(
        string agent04JobId,
        string jobDirectoryRelative,
        CancellationToken ct = default)
    {
        using var channel = GrpcChannel.ForAddress(_address);
        var client = new TranscriptionService.TranscriptionServiceClient(channel);
        try
        {
            var response = await client.GetProjectFilesAsync(
                    new GetProjectFilesRequest
                    {
                        JobId = agent04JobId,
                        JobDirectoryRelative = jobDirectoryRelative ?? ""
                    },
                    cancellationToken: ct)
                .ResponseAsync.ConfigureAwait(false);
            return MapProjectFiles(response);
        }
        catch (RpcException ex)
        {
            _logger.LogDebug(
                ex,
                "Agent04 GetProjectFiles failed for {JobId}, dir={Dir}",
                agent04JobId,
                jobDirectoryRelative);
            return null;
        }
    }

    private static Application.JobProjectFiles MapProjectFiles(GetProjectFilesResponse r) =>
        new()
        {
            Original = r.Original.Select(MapArtifactFile).ToList(),
            Transcripts = r.Transcripts.Select(MapArtifactFile).ToList(),
            Chunks = r.Chunks.Select(MapArtifactFile).ToList(),
            ChunkJson = r.ChunkJson.Select(MapArtifactFile).ToList(),
            Intermediate = r.Intermediate.Select(MapArtifactFile).ToList(),
            Converted = r.Converted.Select(MapArtifactFile).ToList(),
            SplitChunks = r.SplitChunks.Select(MapArtifactFile).ToList()
        };

    private static Application.JobProjectFile MapArtifactFile(JobArtifactFileEntry e)
    {
        var f = new Application.JobProjectFile
        {
            Name = e.Name ?? "",
            RelativePath = e.RelativePath ?? "",
            Kind = string.IsNullOrEmpty(e.Kind) ? "other" : e.Kind,
            SizeBytes = e.SizeBytes,
            FullPath = null
        };
        if (e.HasLineCount)
            f.LineCount = e.LineCount;
        if (e.HasDurationSeconds)
            f.DurationSeconds = e.DurationSeconds;
        if (e.HasFileChunkIndex)
            f.Index = e.FileChunkIndex;
        if (e.HasParentIndex)
            f.ParentIndex = e.ParentIndex;
        if (e.HasSubIndex)
            f.SubIndex = e.SubIndex;
        if (e.HasHasTranscript)
            f.HasTranscript = e.HasTranscript;
        if (e.HasIsTranscript)
            f.IsTranscript = e.IsTranscript;
        return f;
    }

    private static Application.ChunkVirtualModelEntry? MapSingleVirtualModel(ChunkVirtualModelEntry? e)
    {
        if (e == null)
            return null;
        return new Application.ChunkVirtualModelEntry
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
        };
    }

    private static Application.SubChunkArtifactGroupJson MapSubChunkGroup(SubChunkArtifactGroup s)
    {
        return new Application.SubChunkArtifactGroupJson
        {
            SubIndex = s.HasSubIndex ? s.SubIndex : null,
            DisplayStem = s.DisplayStem ?? "",
            AudioFiles = s.AudioFiles.Select(MapArtifactFile).ToList(),
            JsonFiles = s.JsonFiles.Select(MapArtifactFile).ToList(),
            VmRow = MapSingleVirtualModel(s.SubVirtualModel)
        };
    }

    private static Application.ChunkArtifactGroupJson MapChunkGroup(ChunkArtifactGroup c)
    {
        return new Application.ChunkArtifactGroupJson
        {
            Index = c.Index,
            DisplayStem = c.DisplayStem ?? "",
            AudioFiles = c.AudioFiles.Select(MapArtifactFile).ToList(),
            JsonFiles = c.JsonFiles.Select(MapArtifactFile).ToList(),
            SubChunks = c.SubChunks.Select(MapSubChunkGroup).ToList(),
            MergedSplitFiles = c.MergedSplitFiles.Select(MapArtifactFile).ToList(),
            VmRow = MapSingleVirtualModel(c.MainVirtualModel)
        };
    }
}
