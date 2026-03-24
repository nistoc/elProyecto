using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agent04.Application;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Agent04.Features.Transcription.Infrastructure;
using Agent04.Proto;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using JobState = Agent04.Features.Transcription.Application.JobState;

namespace Agent04.Services;

public class TranscriptionGrpcService : TranscriptionService.TranscriptionServiceBase
{
    private static readonly Regex SubChunkNodeId = new(
        @"^(?<job>.+):transcribe:chunk-(?<parent>\d+):sub-(?<sub>\d+)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly ITranscriptionPipeline _pipeline;
    private readonly IJobStatusStore _store;
    private readonly WorkspaceRoot _workspaceRoot;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<TranscriptionGrpcService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IJobArtifactRootRegistry _artifactRootRegistry;
    private readonly IProjectArtifactService _projectArtifactService;
    private readonly ITranscriptionMerger _merger;
    private readonly INodeModel? _nodeModel;
    private readonly INodeQuery? _nodeQuery;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IOutboundJobNotifier? _outboundNotifier;
    private readonly TranscriptionTelemetryHub? _telemetryHub;

    public TranscriptionGrpcService(
        ITranscriptionPipeline pipeline,
        IJobStatusStore store,
        WorkspaceRoot workspaceRoot,
        IHostEnvironment hostEnvironment,
        ILogger<TranscriptionGrpcService> logger,
        IConfiguration configuration,
        IJobArtifactRootRegistry artifactRootRegistry,
        IProjectArtifactService projectArtifactService,
        ITranscriptionMerger merger,
        INodeModel? nodeModel = null,
        INodeQuery? nodeQuery = null,
        IHttpClientFactory? httpClientFactory = null,
        IOutboundJobNotifier? outboundNotifier = null,
        TranscriptionTelemetryHub? telemetryHub = null)
    {
        _pipeline = pipeline;
        _store = store;
        _workspaceRoot = workspaceRoot;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
        _configuration = configuration;
        _artifactRootRegistry = artifactRootRegistry;
        _projectArtifactService = projectArtifactService;
        _merger = merger;
        _nodeModel = nodeModel;
        _nodeQuery = nodeQuery;
        _httpClientFactory = httpClientFactory;
        _outboundNotifier = outboundNotifier;
        _telemetryHub = telemetryHub;
    }

    public override async Task<SubmitJobResponse> SubmitJob(Agent04.Proto.SubmitJobRequest request, ServerCallContext context)
    {
        var root = _workspaceRoot.RootPath;
        var configPathRel = (request.ConfigPath ?? "config/default.json").Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _logger.LogInformation(
            "SubmitJob: WorkspaceRoot={Root}, ConfigPath(rel)={ConfigRel}, InputFilePath(rel)={InputRel}, Tags={TagCount}",
            root, configPathRel, request.InputFilePath ?? "(null)", request.Tags?.Count ?? 0);

        var configPathFull = ResolveConfigFullPath(root, configPathRel);
        if (string.IsNullOrEmpty(configPathFull))
        {
            _logger.LogWarning(
                "SubmitJob rejected: config not found under workspace ({WorkspaceTry}) or content root for rel={ConfigRel}",
                Path.Combine(root, configPathRel), configPathRel);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Config file not found"));
        }

        TranscriptionConfig config;
        try
        {
            config = await TranscriptionConfig.FromFileAsync(configPathFull, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SubmitJob rejected: failed to load config from {ConfigPath}", configPathFull);
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Config load failed: {ex.Message}"));
        }

        var files = config.GetFiles();
        var rawPath = request.InputFilePath ?? (files.Count > 0 ? files[0] : null);
        if (string.IsNullOrEmpty(rawPath))
        {
            _logger.LogWarning("SubmitJob rejected: no input path in request and config has no files");
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Input file not specified"));
        }

        rawPath = rawPath.Trim();
        if (Path.IsPathRooted(rawPath))
        {
            _logger.LogWarning("SubmitJob rejected: input_file_path must be relative, got rooted path");
            throw new RpcException(new Status(StatusCode.InvalidArgument, "input_file_path must be relative to workspace_root; absolute paths are not allowed"));
        }

        var inputPathRel = rawPath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var inputPathFull = Path.Combine(root, inputPathRel);
        if (!File.Exists(inputPathFull))
        {
            _logger.LogWarning(
                "SubmitJob rejected: input file missing. WorkspaceRoot={Root}, resolved={FullPath}",
                root, inputPathFull);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Input file not found"));
        }

        var tags = request.Tags?.Count > 0 ? request.Tags.ToList() : null;
        var callbackUrl = !string.IsNullOrWhiteSpace(request.CallbackUrl) ? request.CallbackUrl.Trim() : null;
        var jobId = _store.Create(tags, callbackUrl);
        string artifactRoot;
        try
        {
            artifactRoot = TranscriptionPaths.ResolveArtifactRoot(root, inputPathFull);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SubmitJob: could not resolve artifact root for {Input}", inputPathFull);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Input path is not under workspace root."));
        }

        _artifactRootRegistry.Register(jobId, artifactRoot);
        _logger.LogInformation(
            "SubmitJob accepted: Agent04JobId={Agent04JobId}, config={ConfigPath}, input={InputPath}, artifactRoot={ArtifactRoot}",
            jobId, configPathFull, inputPathFull, artifactRoot);
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
        return Task.FromResult(ToResponse(job, request.ClientChunkVirtualModel));
    }

    public override async Task StreamJobStatus(StreamJobStatusRequest request, IServerStreamWriter<Agent04.Proto.JobStatusUpdate> responseStream, ServerCallContext context)
    {
        var jobId = request.JobId;
        var lastState = "";
        var lastUpdated = "";
        var lastChunkSig = "";
        var lastFooter = "";
        var lastSilenceSig = "";
        IReadOnlyList<ChunkVirtualModelEntry>? mergedVm = request.ClientChunkVirtualModel.Count > 0
            ? request.ClientChunkVirtualModel.ToList()
            : null;
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
            var liveVm = BuildChunkVirtualModel(job.JobId, job.TotalChunks);
            mergedVm = ChunkVirtualModelMerge.Merge(mergedVm, liveVm);
            var chunkSig = ChunkVirtualModelSignature(mergedVm);
            var footer = _telemetryHub?.GetFooterHint(job.JobId) ?? "";
            var silenceSig = BuildSilenceTimelineSignature(job);
            if (state != lastState || updated != lastUpdated || chunkSig != lastChunkSig || footer != lastFooter
                || silenceSig != lastSilenceSig)
            {
                lastState = state;
                lastUpdated = updated;
                lastChunkSig = chunkSig;
                lastFooter = footer;
                lastSilenceSig = silenceSig;
                var msg = new Agent04.Proto.JobStatusUpdate
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
                    ErrorMessage = job.ErrorMessage ?? "",
                    TranscriptionFooterHint = footer
                };
                CopySilenceTimelineToUpdate(job, msg);
                foreach (var e in mergedVm)
                    msg.ChunkVirtualModel.Add(e.Clone());
                await responseStream.WriteAsync(msg);
                if (job.State is JobState.Failed or JobState.Cancelled)
                    return;
            }
            var pollMs = _configuration.GetValue("Agent04:StreamJobStatusPollMs", 2000);
            if (pollMs < 100) pollMs = 100;
            await Task.Delay(pollMs, context.CancellationToken);
        }
    }

    public override async Task<EnqueueTranscriptionWorkResponse> EnqueueTranscriptionWork(
        EnqueueTranscriptionWorkRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.JobId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "job_id is required"));

        string artifactRoot;
        try
        {
            artifactRoot = ResolveChunkCancelBase(request.JobId, request.JobDirectoryRelative);
        }
        catch (RpcException ex)
        {
            return new EnqueueTranscriptionWorkResponse { Ok = false, Message = ex.Status.Detail };
        }

        try
        {
            await _projectArtifactService
                .WritePendingChunkIndicesAsync(artifactRoot, request.ChunkIndices.ToList(), context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var path = Path.Combine(artifactRoot, PendingChunksReader.FileName);
            _logger.LogWarning(ex, "EnqueueTranscriptionWork: failed to write {Path}", path);
            return new EnqueueTranscriptionWorkResponse { Ok = false, Message = ex.Message };
        }

        _logger.LogInformation(
            "EnqueueTranscriptionWork: job {JobId} wrote pending_chunks count={Count}",
            request.JobId,
            request.ChunkIndices.Count);
        return new EnqueueTranscriptionWorkResponse { Ok = true, Message = "pending_chunks_written" };
    }

    public override async Task<GetChunkArtifactGroupsResponse> GetChunkArtifactGroups(
        GetChunkArtifactGroupsRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.JobId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "job_id is required"));

        string artifactRoot;
        try
        {
            artifactRoot = ResolveChunkCancelBase(request.JobId, request.JobDirectoryRelative);
        }
        catch (RpcException)
        {
            throw;
        }

        if (!Directory.Exists(artifactRoot))
            throw new RpcException(new Status(StatusCode.NotFound, "Job artifact directory not found"));

        var domainGroups = await _projectArtifactService
            .GetChunkArtifactGroupsAsync(artifactRoot, request.TotalChunks, context.CancellationToken)
            .ConfigureAwait(false);

        var resp = new GetChunkArtifactGroupsResponse();
        foreach (var g in domainGroups)
        {
            var row = new ChunkArtifactGroup
            {
                Index = g.Index,
                DisplayStem = g.DisplayStem ?? "",
            };
            foreach (var f in g.AudioFiles) row.AudioFiles.Add(ToProtoJobFile(f));
            foreach (var f in g.JsonFiles) row.JsonFiles.Add(ToProtoJobFile(f));
            foreach (var s in g.SubChunks)
            {
                var sg = new SubChunkArtifactGroup { DisplayStem = s.DisplayStem ?? "" };
                if (s.SubIndex.HasValue)
                    sg.SubIndex = s.SubIndex.Value;
                foreach (var f in s.AudioFiles) sg.AudioFiles.Add(ToProtoJobFile(f));
                foreach (var f in s.JsonFiles) sg.JsonFiles.Add(ToProtoJobFile(f));
                row.SubChunks.Add(sg);
            }

            foreach (var f in g.MergedSplitFiles) row.MergedSplitFiles.Add(ToProtoJobFile(f));
            resp.Groups.Add(row);
        }

        var job = _store.Get(request.JobId);
        var totalHint = request.TotalChunks > 0 ? request.TotalChunks : job?.TotalChunks ?? 0;
        var liveVm = BuildChunkVirtualModel(request.JobId, totalHint);
        IReadOnlyList<ChunkVirtualModelEntry>? clientPrev = request.ClientChunkVirtualModel.Count > 0
            ? request.ClientChunkVirtualModel.ToList()
            : null;
        var mergedClientLive = ChunkVirtualModelMerge.Merge(clientPrev, liveVm);

        var workState = await _projectArtifactService
            .TryLoadWorkStateAsync(artifactRoot, context.CancellationToken)
            .ConfigureAwait(false);
        var workVm = ChunkVirtualModelFromWorkState.Build(workState);
        var vm = ChunkVirtualModelMerge.Merge(workVm, mergedClientLive);

        ChunkArtifactGroupVirtualModelBinder.ApplyToResponse(resp, vm);

        return resp;
    }

    public override async Task<GetProjectFilesResponse> GetProjectFiles(
        GetProjectFilesRequest request,
        ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.JobId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "job_id is required"));

        string artifactRoot;
        try
        {
            artifactRoot = ResolveChunkCancelBase(request.JobId, request.JobDirectoryRelative);
        }
        catch (RpcException)
        {
            throw;
        }

        if (!Directory.Exists(artifactRoot))
            throw new RpcException(new Status(StatusCode.NotFound, "Job artifact directory not found"));

        // Same total hint as GetChunkArtifactGroups → GetChunkArtifactGroupsAsync (domain groups, not VM-only hint).
        var catalog = await _projectArtifactService
            .GetProjectFilesCatalogAsync(artifactRoot, request.TotalChunks, context.CancellationToken)
            .ConfigureAwait(false);

        var resp = new GetProjectFilesResponse();
        foreach (var f in catalog.Original) resp.Original.Add(ToProtoJobFile(f));
        foreach (var f in catalog.Transcripts) resp.Transcripts.Add(ToProtoJobFile(f));
        foreach (var f in catalog.Chunks) resp.Chunks.Add(ToProtoJobFile(f));
        foreach (var f in catalog.ChunkJson) resp.ChunkJson.Add(ToProtoJobFile(f));
        foreach (var f in catalog.Intermediate) resp.Intermediate.Add(ToProtoJobFile(f));
        foreach (var f in catalog.Converted) resp.Converted.Add(ToProtoJobFile(f));
        foreach (var f in catalog.SplitChunks) resp.SplitChunks.Add(ToProtoJobFile(f));
        return resp;
    }

    private static JobArtifactFileEntry ToProtoJobFile(ArtifactFileEntry e)
    {
        var p = new JobArtifactFileEntry
        {
            Name = e.Name,
            RelativePath = e.RelativePath,
            Kind = e.Kind,
            SizeBytes = e.SizeBytes,
        };
        if (e.LineCount.HasValue) p.LineCount = e.LineCount.Value;
        if (e.DurationSeconds.HasValue) p.DurationSeconds = e.DurationSeconds.Value;
        if (e.Index.HasValue) p.FileChunkIndex = e.Index.Value;
        if (e.ParentIndex.HasValue) p.ParentIndex = e.ParentIndex.Value;
        if (e.SubIndex.HasValue) p.SubIndex = e.SubIndex.Value;
        if (e.HasTranscript.HasValue) p.HasTranscript = e.HasTranscript.Value;
        if (e.IsTranscript.HasValue) p.IsTranscript = e.IsTranscript.Value;
        return p;
    }

    public override async Task<ChunkCommandResponse> ChunkCommand(ChunkCommandRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.JobId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "job_id is required"));

        var allowCompleted =
            request.Action == ChunkCommandAction.Split
            || request.Action == ChunkCommandAction.TranscribeSub
            || request.Action == ChunkCommandAction.Retranscribe
            || request.Action == ChunkCommandAction.RebuildCombined
            || request.Action == ChunkCommandAction.DeleteSubChunk
            || request.Action == ChunkCommandAction.RebuildSplitMerged
            || request.Action == ChunkCommandAction.WriteChunkMd
            || request.Action == ChunkCommandAction.Cancel;
        var job = _store.Get(request.JobId);
        if (job == null && allowCompleted && !string.IsNullOrWhiteSpace(request.JobDirectoryRelative))
        {
            string artifactRoot;
            try
            {
                artifactRoot = ResolveChunkCancelBase(request.JobId, request.JobDirectoryRelative);
            }
            catch (RpcException)
            {
                throw;
            }

            if (!Directory.Exists(artifactRoot))
                throw new RpcException(new Status(StatusCode.NotFound, "Job workspace not found"));

            var totalHint =
                await _projectArtifactService.ResolveTotalChunksHintAsync(artifactRoot, context.CancellationToken)
                    .ConfigureAwait(false);
            _store.EnsureDiskBackedCompletedJob(request.JobId, totalHint);
            job = _store.Get(request.JobId);
            if (job == null)
                throw new RpcException(new Status(StatusCode.Internal, "Failed to register disk-backed job"));
        }
        else if (job == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Job not found"));
        }

        if (!allowCompleted && job.State != JobState.Running)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Job is not running (state={job.State})"));
        if (allowCompleted && job.State != JobState.Running && job.State != JobState.Completed)
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                $"Split / transcribe_sub / retranscribe / rebuild_combined / rebuild_split_merged / delete_sub_chunk / cancel require job Running or Completed (state={job.State})"));

        switch (request.Action)
        {
            case ChunkCommandAction.Cancel:
                if (request.ChunkIndex < 0)
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "chunk_index must be >= 0"));
                var cancelBase = ResolveChunkCancelBase(request.JobId, request.JobDirectoryRelative);
                var cm = _projectArtifactService.GetCancellationManager(request.JobId, cancelBase);
                if (request.SubChunkIndex >= 0)
                {
                    cm.MarkSubChunkCancelled(request.ChunkIndex, request.SubChunkIndex);
                    RecordSubChunkOperatorActionInNodeModel(
                        request.JobId,
                        request.ChunkIndex,
                        request.SubChunkIndex,
                        "cancel");
                    return new ChunkCommandResponse { Ok = true, Message = "cancel_sub_requested" };
                }

                cm.MarkCancelled(request.ChunkIndex);
                RecordChunkOperatorActionInNodeModel(request.JobId, request.ChunkIndex, "cancel");
                return new ChunkCommandResponse { Ok = true, Message = "cancel_requested" };
            case ChunkCommandAction.Retranscribe:
                if (request.ChunkIndex < 0)
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "chunk_index must be >= 0"));
                RecordChunkOperatorActionInNodeModel(request.JobId, request.ChunkIndex, "retranscribe");
                return await ExecuteRetranscribeChunkAsync(request, context.CancellationToken).ConfigureAwait(false);
            case ChunkCommandAction.RebuildCombined:
                RecordChunkOperatorActionInNodeModel(request.JobId, request.ChunkIndex, "rebuild_combined");
                return await ExecuteRebuildCombinedAsync(request, context.CancellationToken).ConfigureAwait(false);
            case ChunkCommandAction.Split:
                if (request.ChunkIndex < 0)
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "chunk_index must be >= 0"));
                RecordChunkOperatorActionInNodeModel(request.JobId, request.ChunkIndex, "split");
                return await ExecuteOperatorSplitAsync(request, context.CancellationToken).ConfigureAwait(false);
            case ChunkCommandAction.TranscribeSub:
                if (request.ChunkIndex < 0)
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "chunk_index must be >= 0"));
                RecordChunkOperatorActionInNodeModel(request.JobId, request.ChunkIndex, "transcribe_sub");
                return await ExecuteTranscribeSubChunkAsync(request, context.CancellationToken).ConfigureAwait(false);
            case ChunkCommandAction.DeleteSubChunk:
                if (request.ChunkIndex < 0)
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "chunk_index must be >= 0"));
                RecordSubChunkOperatorActionInNodeModel(request.JobId, request.ChunkIndex, request.SubChunkIndex, "delete_sub_chunk");
                return await ExecuteDeleteSubChunkAsync(request, context.CancellationToken).ConfigureAwait(false);
            case ChunkCommandAction.RebuildSplitMerged:
                if (request.ChunkIndex < 0)
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "chunk_index must be >= 0"));
                RecordChunkOperatorActionInNodeModel(request.JobId, request.ChunkIndex, "rebuild_split_merged");
                return await ExecuteRebuildSplitMergedAsync(request, context.CancellationToken).ConfigureAwait(false);
            case ChunkCommandAction.WriteChunkMd:
                if (request.ChunkIndex < 0)
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "chunk_index must be >= 0"));
                RecordChunkOperatorActionInNodeModel(request.JobId, request.ChunkIndex, "write_chunk_md");
                return await ExecuteWriteChunkMarkdownAsync(request, context.CancellationToken).ConfigureAwait(false);
            default:
                return new ChunkCommandResponse { Ok = false, Message = "unknown_action" };
        }
    }

    private async Task<ChunkCommandResponse> ExecuteTranscribeSubChunkAsync(ChunkCommandRequest request, CancellationToken ct)
    {
        if (request.SubChunkIndex < 0)
            return new ChunkCommandResponse { Ok = false, Message = "sub_chunk_index must be >= 0" };

        string artifactRoot;
        try
        {
            artifactRoot = ResolveChunkCancelBase(request.JobId, request.JobDirectoryRelative);
        }
        catch (RpcException ex)
        {
            return new ChunkCommandResponse { Ok = false, Message = ex.Status.Detail };
        }

        var configRel = (_configuration["Agent04:ConfigPath"] ?? "config/default.json").Trim()
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configPathFull = ResolveConfigFullPath(_workspaceRoot.RootPath, configRel);
        if (string.IsNullOrEmpty(configPathFull))
            return new ChunkCommandResponse { Ok = false, Message = "transcription config file not found" };

        TranscriptionConfig config;
        try
        {
            config = await TranscriptionConfig.FromFileAsync(configPathFull, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ChunkCommandResponse { Ok = false, Message = "config load failed: " + ex.Message };
        }

        var job = _store.Get(request.JobId);
        var totalChunks = job?.TotalChunks ?? 0;
        if (totalChunks <= 0)
        {
            var doc = await _projectArtifactService.TryLoadWorkStateAsync(artifactRoot, ct).ConfigureAwait(false);
            totalChunks = doc?.TotalChunks ?? 0;
        }

        if (totalChunks <= 0)
            return new ChunkCommandResponse { Ok = false, Message = "total_chunks unknown; complete main transcription first" };

        var jobId = request.JobId;
        var chunkIndex = request.ChunkIndex;
        var subIdx = request.SubChunkIndex;
        _ = Task.Run(async () =>
        {
            try
            {
                await _pipeline.TranscribeSplitSubChunkAsync(
                    config,
                    artifactRoot,
                    jobId,
                    chunkIndex,
                    subIdx,
                    totalChunks,
                    _nodeModel,
                    CancellationToken.None).ConfigureAwait(false);
                _logger.LogInformation(
                    "TranscribeSub ok: job {JobId} parent chunk {Parent} sub {Sub}",
                    jobId,
                    chunkIndex,
                    subIdx);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TranscribeSub failed for job {JobId}", jobId);
            }
        }, CancellationToken.None);

        return new ChunkCommandResponse { Ok = true, Message = "transcribe_sub_started" };
    }

    private async Task<ChunkCommandResponse> ExecuteRetranscribeChunkAsync(ChunkCommandRequest request, CancellationToken ct)
    {
        string artifactRoot;
        try
        {
            artifactRoot = ResolveChunkCancelBase(request.JobId, request.JobDirectoryRelative);
        }
        catch (RpcException ex)
        {
            return new ChunkCommandResponse { Ok = false, Message = ex.Status.Detail };
        }

        var configRel = (_configuration["Agent04:ConfigPath"] ?? "config/default.json").Trim()
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configPathFull = ResolveConfigFullPath(_workspaceRoot.RootPath, configRel);
        if (string.IsNullOrEmpty(configPathFull))
            return new ChunkCommandResponse { Ok = false, Message = "transcription config file not found" };

        TranscriptionConfig config;
        try
        {
            config = await TranscriptionConfig.FromFileAsync(configPathFull, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ChunkCommandResponse { Ok = false, Message = "config load failed: " + ex.Message };
        }

        if (OperatorSplitArtifactPresence.HasArtifactsForChunk(artifactRoot, config.SplitChunksDir, request.ChunkIndex))
            return new ChunkCommandResponse { Ok = false, Message = "retranscribe_blocked_operator_split_present" };

        var jobId = request.JobId;
        var chunkIndex = request.ChunkIndex;
        _ = Task.Run(async () =>
        {
            try
            {
                await _pipeline.RetranscribeMainChunkAsync(
                    config,
                    artifactRoot,
                    jobId,
                    chunkIndex,
                    _nodeModel,
                    CancellationToken.None,
                    _store).ConfigureAwait(false);
                _logger.LogInformation("Retranscribe ok: job {JobId} chunk {Chunk}", jobId, chunkIndex);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retranscribe failed for job {JobId} chunk {Chunk}", jobId, chunkIndex);
            }
        }, CancellationToken.None);

        return new ChunkCommandResponse { Ok = true, Message = "retranscribe_started" };
    }

    private async Task<ChunkCommandResponse> ExecuteRebuildCombinedAsync(ChunkCommandRequest request, CancellationToken ct)
    {
        string artifactRoot;
        try
        {
            artifactRoot = ResolveChunkCancelBase(request.JobId, request.JobDirectoryRelative);
        }
        catch (RpcException ex)
        {
            return new ChunkCommandResponse { Ok = false, Message = ex.Status.Detail };
        }

        var configRel = (_configuration["Agent04:ConfigPath"] ?? "config/default.json").Trim()
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configPathFull = ResolveConfigFullPath(_workspaceRoot.RootPath, configRel);
        if (string.IsNullOrEmpty(configPathFull))
            return new ChunkCommandResponse { Ok = false, Message = "transcription config file not found" };

        TranscriptionConfig config;
        try
        {
            config = await TranscriptionConfig.FromFileAsync(configPathFull, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ChunkCommandResponse { Ok = false, Message = "config load failed: " + ex.Message };
        }

        var jobId = request.JobId;
        _ = Task.Run(async () =>
        {
            try
            {
                await _pipeline.RebuildCombinedOutputsFromPerChunkJsonAsync(
                    config,
                    artifactRoot,
                    jobId,
                    _nodeModel,
                    CancellationToken.None).ConfigureAwait(false);
                _logger.LogInformation("rebuild_combined ok: job {JobId}", jobId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "rebuild_combined failed for job {JobId}", jobId);
            }
        }, CancellationToken.None);

        return new ChunkCommandResponse { Ok = true, Message = "rebuild_combined_started" };
    }

    private async Task<ChunkCommandResponse> ExecuteWriteChunkMarkdownAsync(ChunkCommandRequest request, CancellationToken ct)
    {
        string artifactRoot;
        try
        {
            artifactRoot = ResolveChunkCancelBase(request.JobId, request.JobDirectoryRelative);
        }
        catch (RpcException ex)
        {
            return new ChunkCommandResponse { Ok = false, Message = ex.Status.Detail };
        }

        var configRel = (_configuration["Agent04:ConfigPath"] ?? "config/default.json").Trim()
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configPathFull = ResolveConfigFullPath(_workspaceRoot.RootPath, configRel);
        if (string.IsNullOrEmpty(configPathFull))
            return new ChunkCommandResponse { Ok = false, Message = "transcription config file not found" };

        TranscriptionConfig config;
        try
        {
            config = await TranscriptionConfig.FromFileAsync(configPathFull, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ChunkCommandResponse { Ok = false, Message = "config load failed: " + ex.Message };
        }

        var jobId = request.JobId;
        var chunkIndex = request.ChunkIndex;
        _ = Task.Run(async () =>
        {
            try
            {
                await _pipeline
                    .EnsurePerChunkMarkdownFromJsonAsync(config, artifactRoot, chunkIndex, CancellationToken.None)
                    .ConfigureAwait(false);
                _logger.LogInformation("write_chunk_md ok: job {JobId} chunk {Chunk}", jobId, chunkIndex);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "write_chunk_md failed for job {JobId} chunk {Chunk}", jobId, chunkIndex);
            }
        }, CancellationToken.None);

        return new ChunkCommandResponse { Ok = true, Message = "write_chunk_md_started" };
    }

    private async Task<ChunkCommandResponse> ExecuteRebuildSplitMergedAsync(ChunkCommandRequest request, CancellationToken ct)
    {
        string artifactRoot;
        try
        {
            artifactRoot = ResolveChunkCancelBase(request.JobId, request.JobDirectoryRelative);
        }
        catch (RpcException ex)
        {
            return new ChunkCommandResponse { Ok = false, Message = ex.Status.Detail };
        }

        var configRel = (_configuration["Agent04:ConfigPath"] ?? "config/default.json").Trim()
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configPathFull = ResolveConfigFullPath(_workspaceRoot.RootPath, configRel);
        if (string.IsNullOrEmpty(configPathFull))
            return new ChunkCommandResponse { Ok = false, Message = "transcription config file not found" };

        TranscriptionConfig config;
        try
        {
            config = await TranscriptionConfig.FromFileAsync(configPathFull, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ChunkCommandResponse { Ok = false, Message = "config load failed: " + ex.Message };
        }

        var (ok, message) = await SplitChunkMergeIntegrator
            .TryRebuildSplitMergedForChunkAsync(
                config,
                artifactRoot,
                request.JobId,
                request.ChunkIndex,
                _merger,
                _logger,
                ct,
                _pipeline,
                _projectArtifactService)
            .ConfigureAwait(false);

        if (ok)
            _logger.LogInformation(
                "rebuild_split_merged ok: job {JobId} parent chunk {Chunk}",
                request.JobId,
                request.ChunkIndex);

        return new ChunkCommandResponse { Ok = ok, Message = message };
    }

    private async Task<ChunkCommandResponse> ExecuteOperatorSplitAsync(ChunkCommandRequest request, CancellationToken ct)
    {
        string artifactRoot;
        try
        {
            artifactRoot = ResolveChunkCancelBase(request.JobId, request.JobDirectoryRelative);
        }
        catch (RpcException ex)
        {
            return new ChunkCommandResponse { Ok = false, Message = ex.Status.Detail };
        }

        var (ok, message) = await _projectArtifactService
            .TryOperatorSplitAsync(artifactRoot, request.ChunkIndex, request.SplitParts, ct)
            .ConfigureAwait(false);
        if (ok)
            _logger.LogInformation("Operator split ok: job {JobId} chunk {Chunk}", request.JobId, request.ChunkIndex);
        return new ChunkCommandResponse { Ok = ok, Message = message };
    }

    private async Task<ChunkCommandResponse> ExecuteDeleteSubChunkAsync(ChunkCommandRequest request, CancellationToken ct)
    {
        if (request.SubChunkIndex < 0)
            return new ChunkCommandResponse { Ok = false, Message = "sub_chunk_index must be >= 0" };

        string artifactRoot;
        try
        {
            artifactRoot = ResolveChunkCancelBase(request.JobId, request.JobDirectoryRelative);
        }
        catch (RpcException ex)
        {
            return new ChunkCommandResponse { Ok = false, Message = ex.Status.Detail };
        }

        string? splitChunksDir = null;
        var configRel = (_configuration["Agent04:ConfigPath"] ?? "config/default.json").Trim()
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configPathFull = ResolveConfigFullPath(_workspaceRoot.RootPath, configRel);
        if (!string.IsNullOrEmpty(configPathFull))
        {
            try
            {
                var cfg = await TranscriptionConfig.FromFileAsync(configPathFull, ct).ConfigureAwait(false);
                splitChunksDir = cfg.SplitChunksDir;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "delete_sub_chunk: optional config not loaded from {Path}", configPathFull);
            }
        }

        var (ok, msg) = await _projectArtifactService.TryDeleteSubChunkArtifactsAsync(
            artifactRoot,
            request.JobId,
            request.ChunkIndex,
            request.SubChunkIndex,
            splitChunksDir,
            ct,
            () => new ValueTask<bool>(IsSubChunkNodeRunning(request.JobId, request.ChunkIndex, request.SubChunkIndex))).ConfigureAwait(false);

        if (!ok && string.Equals(msg, "sub_chunk_running", StringComparison.Ordinal))
            throw new RpcException(new Status(StatusCode.FailedPrecondition, msg));

        return new ChunkCommandResponse { Ok = ok, Message = msg };
    }

    private bool IsSubChunkNodeRunning(string agent04JobId, int parentChunkIndex, int subChunkIndex)
    {
        if (_nodeQuery == null) return false;
        var nodeId = $"{agent04JobId}:transcribe:chunk-{parentChunkIndex}:sub-{subChunkIndex}";
        var node = _nodeQuery.GetNodeByScopeAndId(agent04JobId, nodeId);
        return node?.Status == JobState.Running;
    }

    /// <summary>
    /// Config path from the client is relative. Prefer <paramref name="workspaceRoot"/> (shared jobs folder), then Agent04 content root
    /// so <c>config/default.json</c> next to the app works when WorkspaceRoot points only at job data (e.g. agent-browser/runtime).
    /// </summary>
    private string ResolveChunkCancelBase(string agent04JobId, string jobDirectoryRelativeFromRequest)
    {
        var root = Path.GetFullPath(_workspaceRoot.RootPath);
        var result = _projectArtifactService.ResolveJobArtifactRoot(root, agent04JobId, jobDirectoryRelativeFromRequest);
        if (result.IsSuccess)
            return result.Path!;

        return result.Failure switch
        {
            ArtifactRootResolutionFailureCode.InvalidRelativePath or ArtifactRootResolutionFailureCode.OutsideWorkspace =>
                throw new RpcException(new Status(StatusCode.InvalidArgument, result.Message ?? "invalid path")),
            ArtifactRootResolutionFailureCode.StrictRequiresJobDirectoryRelative =>
                throw new RpcException(new Status(StatusCode.FailedPrecondition, result.Message ?? "strict path required")),
            _ => throw new RpcException(new Status(StatusCode.Internal, result.Message ?? "artifact root resolution failed"))
        };
    }

    private string? ResolveConfigFullPath(string workspaceRoot, string configPathRel)
    {
        var underWorkspace = Path.GetFullPath(Path.Combine(workspaceRoot, configPathRel));
        if (File.Exists(underWorkspace))
            return underWorkspace;

        var contentRoot = _hostEnvironment.ContentRootPath;
        if (!string.IsNullOrEmpty(contentRoot))
        {
            var underApp = Path.GetFullPath(Path.Combine(contentRoot, configPathRel));
            if (File.Exists(underApp))
                return underApp;
        }

        return null;
    }

    /// <summary>
    /// RENTGEN / virtual model: record operator chunk commands on the same node id the pipeline uses (<c>{jobId}:transcribe:chunk-{i}</c>).
    /// </summary>
    private void RecordChunkOperatorActionInNodeModel(string jobId, int chunkIndex, string action)
    {
        if (_nodeModel == null) return;
        var transcribeParent = jobId + ":transcribe";
        var chunkNodeId = transcribeParent + ":chunk-" + chunkIndex;
        _nodeModel.EnsureNode(chunkNodeId, transcribeParent, jobId, "chunk",
            new Dictionary<string, object?>
            {
                ["operator_action"] = action,
                ["operator_action_at"] = DateTimeOffset.UtcNow.ToString("O")
            });
    }

    private void RecordSubChunkOperatorActionInNodeModel(string jobId, int parentChunkIndex, int subChunkIndex, string action)
    {
        if (_nodeModel == null) return;
        var transcribeParent = jobId + ":transcribe";
        var localKey = "chunk-" + parentChunkIndex + ":sub-" + subChunkIndex;
        var nodeId = transcribeParent + ":" + localKey;
        _nodeModel.EnsureNode(nodeId, transcribeParent, jobId, "chunk",
            new Dictionary<string, object?>
            {
                ["operator_action"] = action,
                ["operator_action_at"] = DateTimeOffset.UtcNow.ToString("O")
            });
    }

    /// <summary>Main chunk node ids are <c>{jobId}:transcribe:chunk-{n}</c> (not sub-chunk rows).</summary>
    private int InferTotalChunksFromNodeScope(string agent04JobId)
    {
        if (_nodeQuery == null || string.IsNullOrEmpty(agent04JobId))
            return 0;
        var prefix = agent04JobId + ":transcribe:chunk-";
        var max = -1;
        foreach (var n in _nodeQuery.GetByScope(agent04JobId))
        {
            if (!n.Id.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            var rest = n.Id.AsSpan(prefix.Length);
            var idx = 0;
            var len = 0;
            foreach (var c in rest)
            {
                if (c < '0' || c > '9')
                    break;
                idx = idx * 10 + (c - '0');
                len++;
            }

            if (len == 0 || len != rest.Length)
                continue;
            max = Math.Max(max, idx);
        }

        return max < 0 ? 0 : max + 1;
    }

    private JobStatusResponse ToResponse(JobStatus job) =>
        ToResponse(job, clientVmForMerge: null);

    private JobStatusResponse ToResponse(
        JobStatus job,
        Google.Protobuf.Collections.RepeatedField<ChunkVirtualModelEntry>? clientVmForMerge)
    {
        var liveVm = BuildChunkVirtualModel(job.JobId, job.TotalChunks);
        IReadOnlyList<ChunkVirtualModelEntry>? prev = clientVmForMerge is { Count: > 0 }
            ? clientVmForMerge
            : null;
        var mergedVm = ChunkVirtualModelMerge.Merge(prev, liveVm);

        var r = new JobStatusResponse
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
            ErrorMessage = job.ErrorMessage ?? "",
            TranscriptionFooterHint = _telemetryHub?.GetFooterHint(job.JobId) ?? ""
        };
        CopySilenceTimelineToResponse(job, r);
        foreach (var e in mergedVm)
            r.ChunkVirtualModel.Add(e.Clone());
        return r;
    }

    private static string BuildSilenceTimelineSignature(JobStatus job)
    {
        var regions = job.SilenceTimelineRegions;
        var n = regions.Count;
        if (n == 0)
            return $"{job.SilenceSourceDurationSec:F4}:0";
        var a = regions[0];
        var b = regions[n - 1];
        return $"{job.SilenceSourceDurationSec:F4}:{n}:{a.StartSec:F2}:{a.EndSec:F2}:{b.StartSec:F2}:{b.EndSec:F2}";
    }

    private static void CopySilenceTimelineToResponse(JobStatus job, JobStatusResponse target)
    {
        target.SilenceSourceDurationSec = job.SilenceSourceDurationSec;
        target.SilenceRegions.Clear();
        foreach (var x in job.SilenceTimelineRegions)
            target.SilenceRegions.Add(new SilenceTimelineRegion { StartSec = x.StartSec, EndSec = x.EndSec });
    }

    private static void CopySilenceTimelineToUpdate(JobStatus job, Agent04.Proto.JobStatusUpdate target)
    {
        target.SilenceSourceDurationSec = job.SilenceSourceDurationSec;
        target.SilenceRegions.Clear();
        foreach (var x in job.SilenceTimelineRegions)
            target.SilenceRegions.Add(new SilenceTimelineRegion { StartSec = x.StartSec, EndSec = x.EndSec });
    }

    private IReadOnlyList<ChunkVirtualModelEntry> BuildChunkVirtualModel(string agent04JobId, int totalChunks)
    {
        var list = new List<ChunkVirtualModelEntry>();
        if (_nodeQuery == null)
            return list;
        var effectiveTotal = totalChunks;
        if (effectiveTotal <= 0)
            effectiveTotal = InferTotalChunksFromNodeScope(agent04JobId);
        if (effectiveTotal <= 0)
            return list;
        for (var i = 0; i < effectiveTotal; i++)
        {
            var nodeId = $"{agent04JobId}:transcribe:chunk-{i}";
            var node = _nodeQuery.GetNodeByScopeAndId(agent04JobId, nodeId);
            if (node == null)
            {
                list.Add(new ChunkVirtualModelEntry
                {
                    ChunkIndex = i,
                    State = nameof(JobState.Pending),
                    IsSubChunk = false,
                    ParentChunkIndex = 0,
                    SubChunkIndex = 0
                });
                continue;
            }

            list.Add(new ChunkVirtualModelEntry
            {
                ChunkIndex = i,
                StartedAt = node.StartedAt?.ToString("O") ?? "",
                CompletedAt = node.CompletedAt?.ToString("O") ?? "",
                State = node.Status.ToString(),
                ErrorMessage = TruncateForChunkVm(node.ErrorMessage),
                IsSubChunk = false,
                ParentChunkIndex = 0,
                SubChunkIndex = 0,
                TranscriptActivityLog = TruncateActivityLogPreservingNewlines(
                    TranscriptActivityLogFromMetadata(node.Metadata),
                    4000)
            });
        }

        foreach (var n in _nodeQuery.GetByScope(agent04JobId))
        {
            var m = SubChunkNodeId.Match(n.Id);
            if (!m.Success) continue;
            if (!string.Equals(m.Groups["job"].Value, agent04JobId, StringComparison.Ordinal))
                continue;
            var parent = int.Parse(m.Groups["parent"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var sub = int.Parse(m.Groups["sub"].Value, System.Globalization.CultureInfo.InvariantCulture);
            list.Add(new ChunkVirtualModelEntry
            {
                ChunkIndex = parent,
                StartedAt = n.StartedAt?.ToString("O") ?? "",
                CompletedAt = n.CompletedAt?.ToString("O") ?? "",
                State = n.Status.ToString(),
                ErrorMessage = TruncateForChunkVm(n.ErrorMessage),
                IsSubChunk = true,
                ParentChunkIndex = parent,
                SubChunkIndex = sub,
                TranscriptActivityLog = TruncateActivityLogPreservingNewlines(
                    TranscriptActivityLogFromMetadata(n.Metadata),
                    4000)
            });
        }

        return list;
    }

    private static string TranscriptActivityLogFromMetadata(IReadOnlyDictionary<string, object?>? md)
    {
        if (md == null || !md.TryGetValue("transcript_activity_log", out var v))
            return "";
        return v as string ?? "";
    }

    private static string TruncateForChunkVm(string? message, int maxLen = 400)
    {
        if (string.IsNullOrEmpty(message)) return "";
        var t = message.Replace('\r', ' ').Replace('\n', ' ');
        return t.Length <= maxLen ? t : t[..maxLen] + "…";
    }

    /// <summary>Keep newlines so UI can render one row per log line; trim on a line boundary when over budget.</summary>
    private static string TruncateActivityLogPreservingNewlines(string? message, int maxLen)
    {
        if (string.IsNullOrEmpty(message)) return "";
        var t = message.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (t.Length <= maxLen) return t;
        var slice = t[..maxLen];
        var lastNl = slice.LastIndexOf('\n');
        if (lastNl > 0)
            return slice[..lastNl] + "\n…";
        return slice + "…";
    }

    private static string ChunkVirtualModelSignature(IReadOnlyList<ChunkVirtualModelEntry> entries)
    {
        if (entries.Count == 0)
            return "";
        return string.Join("|", entries.Select(e =>
            $"{e.ChunkIndex}:{e.IsSubChunk}:{e.ParentChunkIndex}:{e.SubChunkIndex}:{e.StartedAt}:{e.CompletedAt}:{e.State}:{e.ErrorMessage}:{e.TranscriptActivityLog}"));
    }

    private async Task RunJobAsync(string jobId, TranscriptionConfig config, string inputPath, string workspaceRoot, CancellationToken cancellationToken)
    {
        try
        {
            await _pipeline.ProcessFileAsync(config, inputPath, workspaceRoot, jobId, _store, _nodeModel, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transcription pipeline failed for job {JobId}, input={InputPath}", jobId, inputPath);
            _store.Update(jobId, new Agent04.Features.Transcription.Application.JobStatusUpdate { State = JobState.Failed, ErrorMessage = ex.Message });
        }
        finally
        {
            _artifactRootRegistry.Unregister(jobId);
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
            var json = System.Text.Json.JsonSerializer.Serialize(job, TranscriptionJsonSerializerOptions.Compact);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await client.PostAsync(job.CallbackUrl!, content);
        }
        catch
        {
            // Fire-and-forget
        }
    }
}
