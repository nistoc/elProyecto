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
    private readonly ICancellationManagerFactory _cancellationFactory;
    private readonly IAudioUtils _audioUtils;
    private readonly INodeModel? _nodeModel;
    private readonly INodeQuery? _nodeQuery;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IOutboundJobNotifier? _outboundNotifier;

    public TranscriptionGrpcService(
        ITranscriptionPipeline pipeline,
        IJobStatusStore store,
        WorkspaceRoot workspaceRoot,
        IHostEnvironment hostEnvironment,
        ILogger<TranscriptionGrpcService> logger,
        IConfiguration configuration,
        IJobArtifactRootRegistry artifactRootRegistry,
        ICancellationManagerFactory cancellationFactory,
        IAudioUtils audioUtils,
        INodeModel? nodeModel = null,
        INodeQuery? nodeQuery = null,
        IHttpClientFactory? httpClientFactory = null,
        IOutboundJobNotifier? outboundNotifier = null)
    {
        _pipeline = pipeline;
        _store = store;
        _workspaceRoot = workspaceRoot;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
        _configuration = configuration;
        _artifactRootRegistry = artifactRootRegistry;
        _cancellationFactory = cancellationFactory;
        _audioUtils = audioUtils;
        _nodeModel = nodeModel;
        _nodeQuery = nodeQuery;
        _httpClientFactory = httpClientFactory;
        _outboundNotifier = outboundNotifier;
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
        return Task.FromResult(ToResponse(job));
    }

    public override async Task StreamJobStatus(StreamJobStatusRequest request, IServerStreamWriter<Agent04.Proto.JobStatusUpdate> responseStream, ServerCallContext context)
    {
        var jobId = request.JobId;
        var lastState = "";
        var lastUpdated = "";
        var lastChunkSig = "";
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
            var vm = BuildChunkVirtualModel(job.JobId, job.TotalChunks);
            var chunkSig = ChunkVirtualModelSignature(vm);
            if (state != lastState || updated != lastUpdated || chunkSig != lastChunkSig)
            {
                lastState = state;
                lastUpdated = updated;
                lastChunkSig = chunkSig;
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
                    ErrorMessage = job.ErrorMessage ?? ""
                };
                foreach (var e in vm)
                    msg.ChunkVirtualModel.Add(e);
                await responseStream.WriteAsync(msg);
                if (job.State is JobState.Completed or JobState.Failed or JobState.Cancelled)
                    return;
            }
            var pollMs = _configuration.GetValue("Agent04:StreamJobStatusPollMs", 2000);
            if (pollMs < 100) pollMs = 100;
            await Task.Delay(pollMs, context.CancellationToken);
        }
    }

    public override Task<EnqueueTranscriptionWorkResponse> EnqueueTranscriptionWork(
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
            return Task.FromResult(new EnqueueTranscriptionWorkResponse { Ok = false, Message = ex.Status.Detail });
        }

        var path = Path.Combine(artifactRoot, PendingChunksReader.FileName);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var payload = new { chunk_indices = request.ChunkIndices.ToList() };
        var json = JsonSerializer.Serialize(payload, TranscriptionJsonSerializerOptions.Compact);
        try
        {
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EnqueueTranscriptionWork: failed to write {Path}", path);
            return Task.FromResult(new EnqueueTranscriptionWorkResponse { Ok = false, Message = ex.Message });
        }

        _logger.LogInformation(
            "EnqueueTranscriptionWork: job {JobId} wrote {Path} count={Count}",
            request.JobId,
            path,
            request.ChunkIndices.Count);
        return Task.FromResult(new EnqueueTranscriptionWorkResponse { Ok = true, Message = "pending_chunks_written" });
    }

    public override async Task<ChunkCommandResponse> ChunkCommand(ChunkCommandRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.JobId))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "job_id is required"));

        var allowCompleted =
            request.Action == ChunkCommandAction.Split
            || request.Action == ChunkCommandAction.TranscribeSub
            || request.Action == ChunkCommandAction.Retranscribe;
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

            job = new JobStatus
            {
                JobId = request.JobId,
                State = JobState.Completed,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }
        else if (job == null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, "Job not found"));
        }

        if (!allowCompleted && job.State != JobState.Running)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, $"Job is not running (state={job.State})"));
        if (allowCompleted && job.State != JobState.Running && job.State != JobState.Completed)
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                $"Split / transcribe_sub require job Running or Completed (state={job.State})"));

        switch (request.Action)
        {
            case ChunkCommandAction.Cancel:
                if (request.ChunkIndex < 0)
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "chunk_index must be >= 0"));
                var cancelBase = ResolveChunkCancelBase(request.JobId, request.JobDirectoryRelative);
                var cm = _cancellationFactory.Get(request.JobId, cancelBase);
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
            case ChunkCommandAction.Skip:
                if (request.ChunkIndex < 0)
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "chunk_index must be >= 0"));
                RecordChunkOperatorActionInNodeModel(request.JobId, request.ChunkIndex, "skip");
                return new ChunkCommandResponse { Ok = false, Message = "not_implemented" };
            case ChunkCommandAction.Retranscribe:
                if (request.ChunkIndex < 0)
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "chunk_index must be >= 0"));
                RecordChunkOperatorActionInNodeModel(request.JobId, request.ChunkIndex, "retranscribe");
                return await ExecuteRetranscribeChunkAsync(request, context.CancellationToken).ConfigureAwait(false);
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
            var doc = await TranscriptionWorkStateFile.TryLoadAsync(artifactRoot, ct).ConfigureAwait(false);
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
                    CancellationToken.None).ConfigureAwait(false);
                _logger.LogInformation("Retranscribe ok: job {JobId} chunk {Chunk}", jobId, chunkIndex);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Retranscribe failed for job {JobId} chunk {Chunk}", jobId, chunkIndex);
            }
        }, CancellationToken.None);

        return new ChunkCommandResponse { Ok = true, Message = "retranscribe_started" };
    }

    private async Task<ChunkCommandResponse> ExecuteOperatorSplitAsync(ChunkCommandRequest request, CancellationToken ct)
    {
        var parts = request.SplitParts;
        if (parts < 2)
            return new ChunkCommandResponse { Ok = false, Message = "split_parts must be >= 2" };

        string artifactRoot;
        try
        {
            artifactRoot = ResolveChunkCancelBase(request.JobId, request.JobDirectoryRelative);
        }
        catch (RpcException ex)
        {
            return new ChunkCommandResponse { Ok = false, Message = ex.Status.Detail };
        }

        var chunksDir = Path.Combine(artifactRoot, "chunks");
        if (!Directory.Exists(chunksDir))
            return new ChunkCommandResponse { Ok = false, Message = "chunks directory not found under job workspace" };

        static bool IsAudioChunk(string p)
        {
            var e = Path.GetExtension(p);
            return e.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                   || e.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
                   || e.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                   || e.Equals(".flac", StringComparison.OrdinalIgnoreCase)
                   || e.Equals(".ogg", StringComparison.OrdinalIgnoreCase);
        }

        var files = Directory.GetFiles(chunksDir)
            .Where(IsAudioChunk)
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (request.ChunkIndex >= files.Count)
            return new ChunkCommandResponse { Ok = false, Message = $"chunk_index {request.ChunkIndex} out of range (found {files.Count} chunk files)" };

        var inputPath = files[request.ChunkIndex];
        var ffmpeg = _audioUtils.WhichOr(_configuration["Agent04:FfmpegPath"], "ffmpeg") ?? "ffmpeg";
        var ffprobe = _audioUtils.WhichOr(_configuration["Agent04:FfprobePath"], "ffprobe") ?? "ffprobe";

        var (durSec, _) = _audioUtils.GetDurationAndSize(ffprobe, inputPath);
        if (durSec <= 0)
            return new ChunkCommandResponse { Ok = false, Message = "could not read audio duration (ffprobe)" };

        var splitChunksDir = "split_chunks";
        var overlapSec = 1.0;
        var configRel = (_configuration["Agent04:ConfigPath"] ?? "config/default.json").Trim()
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configPathFull = ResolveConfigFullPath(_workspaceRoot.RootPath, configRel);
        if (!string.IsNullOrEmpty(configPathFull))
        {
            try
            {
                var cfg = await TranscriptionConfig.FromFileAsync(configPathFull, ct).ConfigureAwait(false);
                splitChunksDir = cfg.SplitChunksDir;
                overlapSec = cfg.ChunkOverlapSec;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Operator split: optional transcription config not loaded from {Path}", configPathFull);
            }
        }

        var ext = Path.GetExtension(inputPath);
        var baseName = Path.GetFileNameWithoutExtension(inputPath);
        var outRoot = Path.Combine(artifactRoot, splitChunksDir, $"chunk_{request.ChunkIndex}", "sub_chunks");
        Directory.CreateDirectory(outRoot);

        IReadOnlyList<OperatorChunkSplitPlanner.Segment> plan;
        try
        {
            plan = OperatorChunkSplitPlanner.PlanEqualSegmentsWithOverlap(durSec, parts, overlapSec);
        }
        catch (Exception ex)
        {
            return new ChunkCommandResponse { Ok = false, Message = ex.Message };
        }

        try
        {
            await Task.Run(() =>
            {
                for (var k = 0; k < plan.Count; k++)
                {
                    var seg = plan[k];
                    var outName = $"{baseName}_sub_{k:D2}{ext}";
                    var outPath = Path.Combine(outRoot, outName);
                    _audioUtils.ExtractAudioSegmentCopyOrReencode(ffmpeg, inputPath, seg.StartSec, seg.DurationSec, outPath);
                }
            }, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Operator split failed for job {JobId} chunk {Chunk}", request.JobId, request.ChunkIndex);
            return new ChunkCommandResponse { Ok = false, Message = ex.Message };
        }

        _logger.LogInformation("Operator split ok: job {JobId} chunk {Chunk} -> {Parts} parts under {Dir}",
            request.JobId, request.ChunkIndex, parts, outRoot);
        return new ChunkCommandResponse { Ok = true, Message = "split_ok" };
    }

    /// <summary>
    /// Config path from the client is relative. Prefer <paramref name="workspaceRoot"/> (shared jobs folder), then Agent04 content root
    /// so <c>config/default.json</c> next to the app works when WorkspaceRoot points only at job data (e.g. agent-browser/runtime).
    /// </summary>
    private string ResolveChunkCancelBase(string agent04JobId, string jobDirectoryRelativeFromRequest)
    {
        var root = Path.GetFullPath(_workspaceRoot.RootPath);
        if (_artifactRootRegistry.TryGet(agent04JobId, out var registered) && !string.IsNullOrEmpty(registered))
            return registered;

        if (!string.IsNullOrWhiteSpace(jobDirectoryRelativeFromRequest))
        {
            var rel = jobDirectoryRelativeFromRequest.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (rel.Contains("..", StringComparison.Ordinal) || rel.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "job_directory_relative must be a single path segment"));
            var combined = Path.GetFullPath(Path.Combine(root, rel));
            var back = Path.GetRelativePath(root, combined);
            if (back.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(back))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "job_directory_relative resolves outside workspace_root"));
            return combined;
        }

        var strict = _configuration.GetValue("Agent04:StrictChunkCancelPath", false);
        if (strict)
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "Chunk cancel requires job_directory_relative after restart or unknown job; set Agent04:StrictChunkCancelPath=false to allow legacy workspace-root fallback."));

        _logger.LogWarning(
            "ChunkCommand: cancel signals use workspace root for Agent04 job {JobId}; worker may not see them if artifacts are under a job subfolder. Pass job_directory_relative.",
            agent04JobId);
        return root;
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

    private JobStatusResponse ToResponse(JobStatus job)
    {
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
            ErrorMessage = job.ErrorMessage ?? ""
        };
        foreach (var e in BuildChunkVirtualModel(job.JobId, job.TotalChunks))
            r.ChunkVirtualModel.Add(e);
        return r;
    }

    private IReadOnlyList<ChunkVirtualModelEntry> BuildChunkVirtualModel(string agent04JobId, int totalChunks)
    {
        var list = new List<ChunkVirtualModelEntry>();
        if (_nodeQuery == null || totalChunks <= 0)
            return list;
        for (var i = 0; i < totalChunks; i++)
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
                SubChunkIndex = 0
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
                SubChunkIndex = sub
            });
        }

        return list;
    }

    private static string TruncateForChunkVm(string? message, int maxLen = 400)
    {
        if (string.IsNullOrEmpty(message)) return "";
        var t = message.Replace('\r', ' ').Replace('\n', ' ');
        return t.Length <= maxLen ? t : t[..maxLen] + "…";
    }

    private static string ChunkVirtualModelSignature(IReadOnlyList<ChunkVirtualModelEntry> entries)
    {
        if (entries.Count == 0)
            return "";
        return string.Join("|", entries.Select(e =>
            $"{e.ChunkIndex}:{e.IsSubChunk}:{e.ParentChunkIndex}:{e.SubChunkIndex}:{e.StartedAt}:{e.CompletedAt}:{e.State}:{e.ErrorMessage}"));
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
