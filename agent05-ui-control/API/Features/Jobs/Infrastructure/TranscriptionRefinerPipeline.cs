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
        try
        {
            await RunCoreAsync(jobId, ct);
        }
        catch (Exception ex)
        {
            // The pipeline must never die silently: finalize the job as failed and close the SSE stream.
            _logger.LogError(ex, "Job {JobId}: pipeline crashed; finalizing as failed", jobId);
            try
            {
                await UpdateAndBroadcastAsync(jobId, s =>
                {
                    s.Status = "failed";
                    s.Phase = "idle";
                    s.TranscriptionError = ex.Message;
                }, CancellationToken.None);
                Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
                Broadcast(jobId, "done", null);
            }
            catch (Exception finalizeEx)
            {
                _logger.LogError(finalizeEx, "Job {JobId}: failed to finalize crashed pipeline", jobId);
            }
        }
    }

    private async Task RunCoreAsync(string jobId, CancellationToken ct)
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

        var streamOk = await ConsumeAgent04StatusAsync(jobId, transcriptionJobId, ct);
        if (!streamOk)
        {
            Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
            Broadcast(jobId, "done", null);
            return;
        }

        // Transcription finished. The refiner is NOT started automatically — it waits for an explicit
        // POST /api/jobs/{id}/refine so the operator can review the transcript first (RunRefinerAsync).
        // The SSE stream is deliberately left open (no 'done') while the job sits at awaiting_refiner.
        snap = await _store.GetAsync(jobId, ct);
        if (snap?.Phase == "awaiting_refiner")
        {
            _logger.LogInformation("Job {JobId}: transcription complete — awaiting manual refiner start", jobId);
            Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
            return;
        }

        Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
        Broadcast(jobId, "done", null);
    }

    public async Task RunRefinerAsync(string jobId, CancellationToken ct = default)
    {
        try
        {
            await RunRefinerCoreAsync(jobId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: refiner run crashed; finalizing as awaiting_refiner/failed", jobId);
            try
            {
                await UpdateAndBroadcastAsync(jobId, s => { s.Phase = "awaiting_refiner"; s.Status = "failed"; }, CancellationToken.None);
                Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
                Broadcast(jobId, "done", null);
            }
            catch (Exception finalizeEx)
            {
                _logger.LogError(finalizeEx, "Job {JobId}: failed to finalize crashed refiner run", jobId);
            }
        }
    }

    private async Task RunRefinerCoreAsync(string jobId, CancellationToken ct)
    {
        var snap = await _store.GetAsync(jobId, ct);
        if (snap == null) return;

        var workspaceRoot = _configuration["Agent04:WorkspaceRoot"]?.Trim();
        var jobsPath = Path.GetFullPath(_configuration["Jobs:WorkspacePath"] ?? "./runtime");
        if (string.IsNullOrEmpty(workspaceRoot))
            workspaceRoot = jobsPath;
        var jobDir = _workspace.GetJobDirectoryPath(jobId);

        await UpdateAndBroadcastAsync(jobId, s => { s.Phase = "refiner"; s.Status = "running"; }, ct);
        Broadcast(jobId, "status", JsonSerializer.Serialize(new { phase = "refiner", status = "running" }, ApiJson.CamelCase));

        string? transcriptContent = null;
        var mdRel = snap.MdOutputPath?.Trim().TrimStart('/', '\\');
        _logger.LogInformation("Job {JobId}: refiner resolving transcript, MdOutputPath(rel)={MdRel}, workspaceRoot={Root}, jobDir={JobDir}",
            jobId, mdRel ?? "null", workspaceRoot, jobDir);
        if (!string.IsNullOrEmpty(mdRel))
        {
            var mdFull = Path.Combine(workspaceRoot, mdRel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(mdFull))
            {
                try { transcriptContent = await File.ReadAllTextAsync(mdFull, Encoding.UTF8, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Job {JobId}: could not read transcript at {Path}", jobId, mdFull); }
            }
        }
        if (string.IsNullOrEmpty(transcriptContent))
        {
            var fallbackMd = Path.Combine(jobDir, "transcript.md");
            if (File.Exists(fallbackMd))
            {
                try { transcriptContent = await File.ReadAllTextAsync(fallbackMd, Encoding.UTF8, ct); } catch { /* ignore */ }
            }
        }
        // Recovered/legacy jobs name the combined file transcript_<agent04JobId>.md — take the newest one.
        if (string.IsNullOrEmpty(transcriptContent) && Directory.Exists(jobDir))
        {
            var candidate = new DirectoryInfo(jobDir).EnumerateFiles("transcript*.md")
                .Where(f => !f.Name.Contains("_fixed", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (candidate != null)
            {
                try { transcriptContent = await File.ReadAllTextAsync(candidate.FullName, Encoding.UTF8, ct); } catch { /* ignore */ }
            }
        }

        var outputRelForRefiner = TryGetRefinerOutputRelativePath(jobId, jobDir);
        var tagsList = snap.Tags?.ToList();
        Application.SubmitRefineJobResult refinerResult;
        try
        {
            var refineInput = outputRelForRefiner != null
                ? new RefineJobInput(null, transcriptContent ?? "", "transcript_fixed.md", 5, 2, tagsList, jobId)
                : new RefineJobInput(null, transcriptContent ?? "", null, 5, 2, tagsList, null);
            refinerResult = await _refiner.SubmitRefineJobAsync(refineInput, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: Agent06 SubmitRefineJob failed", jobId);
            await UpdateAndBroadcastAsync(jobId, s => { s.Phase = "awaiting_refiner"; s.Status = "failed"; }, ct);
            Broadcast(jobId, "status", JsonSerializer.Serialize(new { phase = "awaiting_refiner", status = "failed" }, ApiJson.CamelCase));
            Broadcast(jobId, "done", null);
            return;
        }

        var refineOk = await ConsumeRefinerStatusAsync(jobId, refinerResult.JobId, ct);
        if (!refineOk)
        {
            await UpdateAndBroadcastAsync(jobId, s => { s.Phase = "awaiting_refiner"; s.Status = "failed"; }, ct);
            Broadcast(jobId, "status", JsonSerializer.Serialize(new { phase = "awaiting_refiner", status = "failed" }, ApiJson.CamelCase));
            Broadcast(jobId, "done", null);
            return;
        }

        var completedAt = DateTime.UtcNow.ToString("O");
        await UpdateAndBroadcastAsync(jobId, s => { s.Phase = "completed"; s.Status = "done"; s.CompletedAt = completedAt; }, ct);
        Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
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

    /// <summary>
    /// Consumes Agent04 status updates with reconnects: on stream failure — backoff, then one GetJobStatus
    /// fallback (a terminal poll result is handled as the final update), otherwise the stream is re-opened
    /// seeded with the stored ChunkVirtualModel. Returns false after 10 consecutive failed cycles (job marked failed).
    /// </summary>
    private async Task<bool> ConsumeAgent04StatusAsync(string jobId, string transcriptionJobId, CancellationToken ct)
    {
        var failures = 0;
        while (true)
        {
            var seedSnap = await _store.GetAsync(jobId, ct);
            var seedVm = seedSnap?.Chunks?.ChunkVirtualModel;
            try
            {
                await foreach (var update in _transcription.StreamJobStatusAsync(transcriptionJobId, seedVm, ct))
                {
                    failures = 0;
                    await ApplyTranscriptionUpdateAsync(jobId, transcriptionJobId, update, ct);
                    if (IsTerminalTranscriptionState(update.State))
                        return true;
                }
                throw new IOException("Agent04 status stream ended without a terminal state.");
            }
            catch (Exception ex) when (ex is RpcException or IOException or HttpRequestException)
            {
                failures++;
                _logger.LogWarning(ex,
                    "Job {JobId}: Agent04 status stream dropped (attempt {Attempt}/10); retrying",
                    jobId, failures);
                await Task.Delay(BackoffDelay(failures), ct);
                var fallbackSnap = await _store.GetAsync(jobId, ct);
                var polled = await _transcription.GetJobStatusAsync(
                    transcriptionJobId, fallbackSnap?.Chunks?.ChunkVirtualModel, ct);
                if (polled != null)
                {
                    failures = 0;
                    await ApplyTranscriptionUpdateAsync(jobId, transcriptionJobId, polled, ct);
                    if (IsTerminalTranscriptionState(polled.State))
                        return true;
                }
                else if (failures >= 10)
                {
                    await UpdateAndBroadcastAsync(jobId, s =>
                    {
                        s.Status = "failed";
                        s.Phase = "idle";
                        s.TranscriptionError = $"Lost connection to Agent04 after {failures} attempts: {ex.Message}";
                    }, ct);
                    return false;
                }
            }
        }
    }

    private async Task ApplyTranscriptionUpdateAsync(string jobId, string transcriptionJobId, JobStatusUpdate update, CancellationToken ct)
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
            if (!string.IsNullOrWhiteSpace(update.TranscriptionFooterHint))
                s.TranscriptionFooterHint = update.TranscriptionFooterHint;
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
            else if (update.State == "NotFound")
            {
                s.Phase = "idle";
                s.TranscriptionError = "Agent04 no longer knows this job (restart?)";
            }
        }, ct);
        Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
        var evtPhase = update.State == "Completed"
            ? "awaiting_refiner"
            : (update.State is "Failed" or "Cancelled" or "NotFound" ? "idle" : "transcriber");
        Broadcast(jobId, "status", JsonSerializer.Serialize(new { status = MapState(update.State), phase = evtPhase, progress_percent = update.ProgressPercent }, ApiJson.CamelCase));
    }

    /// <summary>
    /// Consumes Agent06 refine status updates with stream reconnects (no unary fallback on the refiner client).
    /// Returns false after 10 consecutive failed cycles.
    /// </summary>
    private async Task<bool> ConsumeRefinerStatusAsync(string jobId, string refineJobId, CancellationToken ct)
    {
        var failures = 0;
        while (true)
        {
            try
            {
                await foreach (var update in _refiner.StreamRefineStatusAsync(refineJobId, ct))
                {
                    failures = 0;
                    await ApplyRefinerUpdateAsync(jobId, update, ct);
                    if (update.State is "Completed" or "Failed" or "Cancelled")
                        return true;
                }
                throw new IOException("Agent06 refine status stream ended without a terminal state.");
            }
            catch (Exception ex) when (ex is RpcException or IOException or HttpRequestException)
            {
                failures++;
                _logger.LogWarning(ex,
                    "Job {JobId}: Agent06 refine stream dropped (attempt {Attempt}/10); retrying",
                    jobId, failures);
                if (failures >= 10)
                    return false;
                await Task.Delay(BackoffDelay(failures), ct);
            }
        }
    }

    private async Task ApplyRefinerUpdateAsync(string jobId, RefineStatusUpdate update, CancellationToken ct)
    {
        await UpdateAndBroadcastAsync(jobId, s =>
        {
            if (update.State == "Completed")
            { s.Phase = "completed"; s.Status = "done"; }
            else if (update.State == "Failed" || update.State == "Cancelled")
            { s.Phase = "awaiting_refiner"; s.Status = update.State == "Failed" ? "failed" : "done"; }
        }, ct);
        var fresh = await _store.GetAsync(jobId, ct);
        Broadcast(jobId, "status", JsonSerializer.Serialize(
            new { phase = fresh?.Phase, status = fresh?.Status, progress_percent = update.ProgressPercent },
            ApiJson.CamelCase));
    }

    private static bool IsTerminalTranscriptionState(string state) =>
        state is "Completed" or "Failed" or "Cancelled" or "NotFound";

    private static TimeSpan BackoffDelay(int attempt) => attempt switch
    {
        <= 1 => TimeSpan.FromSeconds(1),
        2 => TimeSpan.FromSeconds(2),
        3 => TimeSpan.FromSeconds(5),
        _ => TimeSpan.FromSeconds(15),
    };

    private static string MapState(string state) =>
        state switch
        {
            "Completed" => "done",
            "Failed" => "failed",
            "Cancelled" => "done",
            "NotFound" => "failed",
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
        _broadcaster.Publish(jobId, new BroadcastEvent(eventType, data));
    }

    private async Task<string> GetSnapshotJsonAsync(string jobId)
    {
        var snap = await _store.GetAsync(jobId);
        if (snap == null) return "{}";
        snap.JobDirectoryPath ??= _workspace.GetJobDirectoryPath(jobId);
        return JsonSerializer.Serialize(snap, ApiJson.CamelCase);
    }
}
