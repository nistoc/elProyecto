using System.Text;
using System.Text.Json;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XtractManager.Features.Jobs.Application;
using XtractManager.Infrastructure;

namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class TranscriptionRefinerPipeline : Application.IPipeline
{
    private readonly Application.IJobStore _store;
    private readonly Application.IJobWorkspace _workspace;
    private readonly Application.IBroadcaster _broadcaster;
    private readonly Application.ITranscriptionServiceClient _transcription;
    private readonly Application.IRefinerServiceClient _refiner;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<TranscriptionRefinerPipeline> _logger;

    public TranscriptionRefinerPipeline(
        IJobStore store,
        IJobWorkspace workspace,
        IBroadcaster broadcaster,
        ITranscriptionServiceClient transcription,
        IRefinerServiceClient refiner,
        IConfiguration configuration,
        IHostEnvironment hostEnvironment,
        ILogger<TranscriptionRefinerPipeline> logger)
    {
        _store = store;
        _workspace = workspace;
        _broadcaster = broadcaster;
        _transcription = transcription;
        _refiner = refiner;
        _configuration = configuration;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public async Task RunAsync(string jobId, CancellationToken ct = default)
    {
        var snap = await _store.GetAsync(jobId, ct);
        if (snap == null) return;

        var configPath = _configuration["Agent04:ConfigPath"] ?? "config/default.json";
        var workspaceRoot = _configuration["Agent04:WorkspaceRoot"]?.Trim();
        var jobsPath = Path.GetFullPath(_configuration["Jobs:WorkspacePath"] ?? "./runtime");
        if (string.IsNullOrEmpty(workspaceRoot))
            workspaceRoot = jobsPath;
        var ext = Path.GetExtension(snap.OriginalFilename ?? "");
        if (string.IsNullOrEmpty(ext)) ext = ".bin";
        var jobDir = _workspace.GetJobDirectoryPath(jobId);
        var audioFileName = JobWorkspace.SanitizeUploadedFileName(snap.OriginalFilename);
        var audioFullPath = Path.Combine(jobDir, audioFileName);
        if (!File.Exists(audioFullPath))
        {
            var legacyPath = Path.Combine(jobDir, "audio" + ext);
            if (File.Exists(legacyPath))
                audioFullPath = legacyPath;
        }
        if (!File.Exists(audioFullPath))
        {
            _logger.LogError(
                "Job {JobId}: cannot start transcription — audio file missing. Tried {Path} (workspaceRoot={Root}, originalFilename={Orig})",
                jobId, audioFullPath, workspaceRoot, snap.OriginalFilename ?? "");
            await UpdateAndBroadcastAsync(jobId, s =>
            {
                s.Status = "failed";
                s.Phase = "idle";
                s.TranscriptionError = "Audio file missing for transcription.";
            }, ct);
            return;
        }
        var inputFilePath = Path.GetRelativePath(workspaceRoot, audioFullPath).Replace('\\', '/');

        await UpdateAndBroadcastAsync(jobId, s =>
        {
            s.Status = "running";
            s.Phase = "transcriber";
            s.TranscriptionError = null;
        }, ct);
        Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));

        _logger.LogInformation(
            "Job {JobId}: submitting transcription — ConfigPath={Config}, workspaceRoot={Root}, audioOnDisk={Audio}, inputFilePath(rel)={InputRel}",
            jobId, configPath, workspaceRoot, audioFullPath, inputFilePath);

        string? transcriptionJobId = null;
        try
        {
            var submitResult = await _transcription.SubmitJobAsync(configPath, inputFilePath, snap.Tags?.ToList(), ct);
            transcriptionJobId = submitResult.JobId;
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex,
                "Job {JobId}: Agent04 SubmitJob failed — RPC {Code}: {Detail}",
                jobId, ex.StatusCode, ex.Status.Detail);
            await UpdateAndBroadcastAsync(jobId, s =>
            {
                s.Status = "failed";
                s.Phase = "idle";
                s.TranscriptionError = $"Agent04 SubmitJob RPC failed: {ex.StatusCode} {ex.Status.Detail}";
            }, ct);
            Broadcast(jobId, "done", null);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: Agent04 SubmitJob failed — {Message}", jobId, ex.Message);
            await UpdateAndBroadcastAsync(jobId, s =>
            {
                s.Status = "failed";
                s.Phase = "idle";
                s.TranscriptionError = ex.Message;
            }, ct);
            Broadcast(jobId, "done", null);
            return;
        }

        await UpdateAndBroadcastAsync(jobId, s => { s.Agent04JobId = transcriptionJobId; }, ct);
        Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));

        await foreach (var update in _transcription.StreamJobStatusAsync(transcriptionJobId, ct))
        {
            if (update.State == "Failed")
                _logger.LogWarning(
                    "Job {JobId}: Agent04 job {Agent04JobId} reported Failed — ErrorMessage={Error}, Phase={Phase}, Progress={Pct}%, ProcessedChunks={Processed}, TotalChunks={Total}",
                    jobId, transcriptionJobId, update.ErrorMessage ?? "(empty)", update.CurrentPhase ?? "", update.ProgressPercent,
                    update.ProcessedChunks, update.TotalChunks);
            _logger.LogDebug("Job {JobId}: Agent04 status update State={State}, MdOutputPath={Md}, TotalChunks={Total}, Processed={Processed}",
                jobId, update.State, update.MdOutputPath ?? "null", update.TotalChunks, update.ProcessedChunks);
            await UpdateAndBroadcastAsync(jobId, s =>
            {
                s.Agent04JobId = transcriptionJobId;
                s.Status = MapState(update.State);
                s.Chunks ??= new ChunkState();
                if (update.TotalChunks > 0)
                    s.Chunks.Total = update.TotalChunks;
                var tc = update.TotalChunks;
                var pc = update.ProcessedChunks;
                if (tc > 0 && pc >= 0)
                {
                    var completed = new List<int>();
                    var n = Math.Min(pc, tc);
                    for (var i = 0; i < n; i++)
                        completed.Add(i);
                    s.Chunks.Completed = completed;
                    s.Chunks.Active = update.State == "Running" && pc < tc
                        ? new List<int> { pc }
                        : new List<int>();
                }

                if (update.ChunkVirtualModel is { Count: > 0 })
                    s.Chunks.ChunkVirtualModel = update.ChunkVirtualModel;
                if (update.State == "Completed")
                {
                    s.Phase = "awaiting_refiner";
                    s.MdOutputPath = update.MdOutputPath;
                    s.TranscriptionError = null;
                }
                else if (update.State == "Failed" || update.State == "Cancelled")
                {
                    s.Phase = "idle";
                    s.TranscriptionError = string.IsNullOrEmpty(update.ErrorMessage)
                        ? (update.State == "Cancelled" ? "Transcription cancelled." : "Transcription failed.")
                        : update.ErrorMessage;
                }
            }, ct);
            Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
            var evtPhase = update.State == "Completed"
                ? "awaiting_refiner"
                : (update.State is "Failed" or "Cancelled" ? "idle" : "transcriber");
            Broadcast(jobId, "status", JsonSerializer.Serialize(new { status = MapState(update.State), phase = evtPhase, progress_percent = update.ProgressPercent }, ApiJson.CamelCase));
            if (update.State is "Completed" or "Failed" or "Cancelled")
                break;
        }

        snap = await _store.GetAsync(jobId, ct);
        if (snap?.Phase != "awaiting_refiner")
        {
            Broadcast(jobId, "done", null);
            return;
        }

        string? transcriptContent = null;
        snap = await _store.GetAsync(jobId, ct);
        var mdRel = snap?.MdOutputPath?.Trim().TrimStart('/', '\\');
        _logger.LogInformation("Job {JobId}: resolving transcript, MdOutputPath(rel)={MdRel}, workspaceRoot={WorkspaceRoot}, jobDir={JobDir}",
            jobId, mdRel ?? "null", workspaceRoot, jobDir);
        if (!string.IsNullOrEmpty(mdRel))
        {
            var mdFull = Path.Combine(workspaceRoot, mdRel.Replace('/', Path.DirectorySeparatorChar));
            _logger.LogInformation("Job {JobId}: transcript path (from Agent04)={Path}, exists={Exists}", jobId, mdFull, File.Exists(mdFull));
            if (File.Exists(mdFull))
            {
                try
                {
                    transcriptContent = await File.ReadAllTextAsync(mdFull, Encoding.UTF8, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Job {JobId}: could not read transcript at {Path}", jobId, mdFull);
                }
            }
        }
        if (string.IsNullOrEmpty(transcriptContent))
        {
            var fallbackMd = Path.Combine(jobDir, "transcript.md");
            _logger.LogInformation("Job {JobId}: fallback transcript path={Path}, exists={Exists}", jobId, fallbackMd, File.Exists(fallbackMd));
            if (File.Exists(fallbackMd))
            {
                try { transcriptContent = await File.ReadAllTextAsync(fallbackMd, Encoding.UTF8, ct); } catch { /* ignore */ }
            }
        }

        await UpdateAndBroadcastAsync(jobId, s => s.Phase = "refiner", ct);
        Broadcast(jobId, "status", JsonSerializer.Serialize(new { phase = "refiner" }, ApiJson.CamelCase));

        var outputRelForRefiner = TryGetRefinerOutputRelativePath(jobId, jobDir);
        if (outputRelForRefiner != null)
            _logger.LogInformation("Job {JobId}: Agent06 output under job dir (legacy rel was {LegacyRel})", jobId, outputRelForRefiner);

        Application.SubmitRefineJobResult? refinerResult = null;
        var tagsList = snap?.Tags?.ToList();
        try
        {
            // Per-job artifact root on Agent06: same physical file as legacy "{jobId}/transcript_fixed.md" under shared workspace.
            var refineInput = outputRelForRefiner != null
                ? new RefineJobInput(
                    InputFilePath: null,
                    InputContent: transcriptContent ?? "",
                    OutputFilePath: "transcript_fixed.md",
                    BatchSize: 5,
                    ContextLines: 2,
                    Tags: tagsList,
                    JobDirectoryRelative: jobId)
                : new RefineJobInput(
                    InputFilePath: null,
                    InputContent: transcriptContent ?? "",
                    OutputFilePath: null,
                    BatchSize: 5,
                    ContextLines: 2,
                    Tags: tagsList,
                    JobDirectoryRelative: null);

            refinerResult = await _refiner.SubmitRefineJobAsync(refineInput, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: Agent06 SubmitRefineJob failed", jobId);
            await UpdateAndBroadcastAsync(jobId, s => { s.Phase = "awaiting_refiner"; s.Status = "done"; }, ct);
            Broadcast(jobId, "done", null);
            return;
        }

        var refineJobId = refinerResult.JobId;
        await foreach (var update in _refiner.StreamRefineStatusAsync(refineJobId, ct))
        {
            await UpdateAndBroadcastAsync(jobId, s =>
            {
                if (update.State == "Completed")
                { s.Phase = "completed"; s.Status = "done"; }
                else if (update.State == "Failed" || update.State == "Cancelled")
                { s.Phase = "awaiting_refiner"; s.Status = update.State == "Failed" ? "failed" : "done"; }
            }, ct);
            Broadcast(jobId, "status", JsonSerializer.Serialize(new { phase = update.State == "Completed" ? "completed" : snap?.Phase, status = update.State == "Completed" ? "done" : snap?.Status, progress_percent = update.ProgressPercent }, ApiJson.CamelCase));
            if (update.State is "Completed" or "Failed" or "Cancelled")
                break;
        }

        var completedAt = DateTime.UtcNow.ToString("O");
        await UpdateAndBroadcastAsync(jobId, s => { if (s.Phase != "completed") s.Phase = "completed"; s.Status = "done"; s.CompletedAt = completedAt; }, ct);
        Broadcast(jobId, "done", null);
    }

    /// <summary>
    /// agent06 requires <c>output_file_path</c> relative to its WorkspaceRoot.
    /// When <c>Agent06:WorkspaceRoot</c> is unset, we assume it matches <see cref="IJobWorkspace.WorkspaceRootPath"/> (same folder as Jobs:WorkspacePath).
    /// </summary>
    private string? TryGetRefinerOutputRelativePath(string jobId, string jobDir)
    {
        var refinerRoot = ResolveRefinerWorkspaceRootFullPath();
        var outputFull = Path.GetFullPath(Path.Combine(jobDir, "transcript_fixed.md"));
        try
        {
            var rel = Path.GetRelativePath(refinerRoot, outputFull);
            var normalized = rel.Replace('\\', '/');
            if (string.IsNullOrEmpty(normalized) || normalized == ".")
            {
                _logger.LogWarning("Job {JobId}: unexpected relative path for transcript_fixed.md (rel empty or '.'); OutputFilePath omitted", jobId);
                return null;
            }

            if (normalized.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            {
                _logger.LogWarning(
                    "Job {JobId}: transcript_fixed.md is not under Agent06 workspace root {Root} (relative={Rel}). Set Agent06:WorkspaceRoot to the same path as Jobs:WorkspacePath to write the file next to the job.",
                    jobId, refinerRoot, rel);
                return null;
            }

            return normalized;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Job {JobId}: could not compute OutputFilePath for refiner", jobId);
            return null;
        }
    }

    private string ResolveRefinerWorkspaceRootFullPath()
    {
        var raw = _configuration["Agent06:WorkspaceRoot"]?.Trim();
        if (string.IsNullOrEmpty(raw))
            return _workspace.WorkspaceRootPath;

        if (Path.IsPathRooted(raw))
            return Path.GetFullPath(raw);

        var contentRoot = _hostEnvironment.ContentRootPath ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(contentRoot, raw));
    }

    private static string MapState(string state) =>
        state switch
        {
            "Completed" => "done",
            "Failed" => "failed",
            "Cancelled" => "done",
            "Running" => "running",
            "Pending" => "queued",
            _ => "running"
        };

    private async Task UpdateAndBroadcastAsync(string jobId, Action<JobSnapshot> update, CancellationToken ct = default)
    {
        await _store.UpdateAsync(jobId, update);
        var fresh = await _store.GetAsync(jobId, ct);
        if (fresh != null)
            await JobSnapshotDiskEnricher.TryWriteUiStateAsync(_workspace.GetJobDirectoryPath(jobId), fresh, ct);
    }

    private void Broadcast(string jobId, string eventType, string? payloadJson)
    {
        var data = string.IsNullOrEmpty(payloadJson)
            ? $"{{\"type\":\"{eventType}\"}}"
            : $"{{\"type\":\"{eventType}\",\"payload\":{payloadJson}}}";
        _broadcaster.Publish(jobId, data);
    }

    private async Task<string> GetSnapshotJsonAsync(string jobId)
    {
        var snap = await _store.GetAsync(jobId);
        if (snap == null) return "{}";
        snap.JobDirectoryPath ??= _workspace.GetJobDirectoryPath(jobId);
        return JsonSerializer.Serialize(snap, ApiJson.CamelCase);
    }
}
