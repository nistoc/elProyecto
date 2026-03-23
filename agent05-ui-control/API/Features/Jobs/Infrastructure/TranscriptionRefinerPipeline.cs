using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Text.RegularExpressions;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XtractManager.Features.Jobs.Application;
using XtractManager.Infrastructure;

namespace XtractManager.Features.Jobs.Infrastructure;

public sealed class TranscriptionRefinerPipeline : IPipeline, IRefinerOrchestration
{
    private readonly IJobStore _store;
    private readonly IJobWorkspace _workspace;
    private readonly IBroadcaster _broadcaster;
    private readonly ITranscriptionServiceClient _transcription;
    private readonly IRefinerServiceClient _refiner;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<TranscriptionRefinerPipeline> _logger;
    /// <summary>Agent06 refine job id between SubmitRefineJob return and snapshot persistence — used so Pause works immediately.</summary>
    private readonly ConcurrentDictionary<string, string> _pendingAgent06RefineJobIdByXtractJob = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> StartRefinerGates =
        new(StringComparer.OrdinalIgnoreCase);

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

        var streamSeedSnap = await _store.GetAsync(jobId, ct);
        var streamSeedVm = streamSeedSnap?.Chunks?.ChunkVirtualModel;

        await foreach (var update in _transcription.StreamJobStatusAsync(transcriptionJobId, streamSeedVm, ct))
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
            }, ct);
            Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
            var evtPhase = update.State == "Completed"
                ? "awaiting_refiner"
                : (update.State is "Failed" or "Cancelled" ? "idle" : "transcriber");
            Broadcast(jobId, "status", JsonSerializer.Serialize(new { status = MapState(update.State), phase = evtPhase, progress_percent = update.ProgressPercent }, ApiJson.CamelCase));
            if (update.State is "Completed" or "Failed" or "Cancelled")
                break;
        }

        var snap2 = await _store.GetAsync(jobId, ct);
        if (snap2?.Phase != "awaiting_refiner")
        {
            Broadcast(jobId, "done", null);
            return;
        }

        Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
        // No "done" — refiner is started manually or skipped.
    }

    public async Task StartRefinerAsync(string jobId, string? transcriptRelativePath = null, CancellationToken ct = default)
    {
        var gate = StartRefinerGates.GetOrAdd(jobId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await StartRefinerCoreAsync(jobId, transcriptRelativePath, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task StartRefinerCoreAsync(string jobId, string? transcriptRelativePath, CancellationToken ct)
    {
        var snap = await _store.GetAsync(jobId, ct);
        if (snap == null) return;
        var jobDir = _workspace.GetJobDirectoryPath(jobId);

        if (string.Equals(snap.Phase, "refiner", StringComparison.OrdinalIgnoreCase)
            || string.Equals(snap.Phase, "refiner_paused", StringComparison.OrdinalIgnoreCase))
        {
            await TeardownActiveRefinerForRestartAsync(jobId, jobDir, snap, ct).ConfigureAwait(false);
            snap = await _store.GetAsync(jobId, ct);
            if (snap == null) return;
        }

        if (snap.Phase != "awaiting_refiner" && snap.Phase != "completed" && snap.Phase != "idle")
            throw new InvalidOperationException(
                $"Start refiner requires phase awaiting_refiner, completed, idle, or restart from refiner/refiner_paused, got {snap.Phase}");
        if (string.Equals(snap.Phase, "idle", StringComparison.OrdinalIgnoreCase)
            && !JobSnapshotDiskEnricher.JobDirectoryHasTranscriptMarkdown(jobDir))
            throw new InvalidOperationException(
                "Start refiner from idle requires a transcript markdown in the job directory.");

        var workspaceRoot = _configuration["Agent04:WorkspaceRoot"]?.Trim();
        var jobsPath = Path.GetFullPath(_configuration["Jobs:WorkspacePath"] ?? "./runtime");
        if (string.IsNullOrEmpty(workspaceRoot))
            workspaceRoot = jobsPath;

        var transcriptContent = await LoadTranscriptContentAsync(jobId, workspaceRoot, jobDir, snap, transcriptRelativePath, ct);
        if (string.IsNullOrWhiteSpace(transcriptContent))
        {
            _logger.LogWarning(
                "Job {JobId}: transcript text is empty after all resolve attempts (MdOutputPath={Md}, JobDir={JobDir}). Refiner input will be blank.",
                jobId, snap.MdOutputPath ?? "null", jobDir);
        }

        await UpdateAndBroadcastAsync(jobId, s =>
        {
            s.Phase = "refiner";
            s.Status = "running";
            s.Agent06RefineJobId = null;
            s.RefinerOpenAiRequestPreview = null;
            s.RefinerThreadBatches = Array.Empty<RefinerThreadBatchEntry>();
        }, ct);
        Broadcast(jobId, "status", JsonSerializer.Serialize(new { phase = "refiner", status = "running" }, ApiJson.CamelCase));
        Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));

        var outputRelForRefiner = TryGetRefinerOutputRelativePath(jobId, jobDir);
        var tagsList = snap.Tags?.ToList();
        var refineBatchSize = _configuration.GetValue("Agent06:Refine:BatchSize", 15);
        var refineContextLines = _configuration.GetValue("Agent06:Refine:ContextLines", 5);
        Application.SubmitRefineJobResult refinerResult;
        try
        {
            var refineInput = outputRelForRefiner != null
                ? new RefineJobInput(
                    InputFilePath: null,
                    InputContent: transcriptContent ?? "",
                    OutputFilePath: "transcript_fixed.md",
                    BatchSize: refineBatchSize,
                    ContextLines: refineContextLines,
                    Tags: tagsList,
                    JobDirectoryRelative: jobId,
                    WorkspaceRootOverride: _workspace.WorkspaceRootPath)
                : new RefineJobInput(
                    InputFilePath: null,
                    InputContent: transcriptContent ?? "",
                    OutputFilePath: null,
                    BatchSize: refineBatchSize,
                    ContextLines: refineContextLines,
                    Tags: tagsList,
                    JobDirectoryRelative: jobId,
                    WorkspaceRootOverride: _workspace.WorkspaceRootPath);

            refinerResult = await _refiner.SubmitRefineJobAsync(refineInput, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId}: Agent06 SubmitRefineJob failed", jobId);
            await UpdateAndBroadcastAsync(jobId, s =>
            {
                s.Phase = "awaiting_refiner";
                s.Status = "done";
            }, ct);
            Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
            return;
        }

        _pendingAgent06RefineJobIdByXtractJob[jobId] = refinerResult.JobId;
        try
        {
            await UpdateAndBroadcastAsync(jobId, s => { s.Agent06RefineJobId = refinerResult.JobId; }, ct);
            Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
        }
        catch
        {
            _pendingAgent06RefineJobIdByXtractJob.TryRemove(jobId, out _);
            throw;
        }

        RunRefinerStatusStreamInBackground(jobId, refinerResult.JobId);
    }

    /// <summary>
    /// Pause after current batch (if running), wait for Paused/terminal, cancel Agent06 job, delete transcript_fixed*; reset snapshot for a new run.
    /// </summary>
    private async Task TeardownActiveRefinerForRestartAsync(
        string jobId,
        string jobDir,
        JobSnapshot snap,
        CancellationToken ct)
    {
        var agentId = snap.Agent06RefineJobId?.Trim();
        if (string.IsNullOrEmpty(agentId) && _pendingAgent06RefineJobIdByXtractJob.TryGetValue(jobId, out var pending))
            agentId = pending?.Trim();
        agentId = string.IsNullOrWhiteSpace(agentId) ? null : agentId.Trim();

        if (!string.IsNullOrEmpty(agentId))
        {
            if (string.Equals(snap.Phase, "refiner", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var pauseOk = await _refiner.PauseRefineJobAsync(agentId, ct).ConfigureAwait(false);
                    if (!pauseOk)
                        _logger.LogInformation(
                            "Job {JobId}: PauseRefine before restart returned false (Agent06 may already be terminal)",
                            jobId);
                }
                catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
                {
                    _logger.LogInformation(
                        ex,
                        "Job {JobId}: Agent06 refine job {AgentId} not found on pause (e.g. Agent06 restarted) — continuing local restart cleanup",
                        jobId,
                        agentId);
                }
            }

            try
            {
                await WaitForRefinePausedOrTerminalAsync(agentId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Job {JobId}: wait for refiner pause/terminal before restart", jobId);
            }

            try
            {
                await _refiner.CancelRefineJobAsync(agentId, ct).ConfigureAwait(false);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                _logger.LogDebug(ex, "Job {JobId}: CancelRefineJob NotFound for {AgentId}", jobId, agentId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Job {JobId}: CancelRefineJob failed for {AgentId}", jobId, agentId);
            }
        }

        _pendingAgent06RefineJobIdByXtractJob.TryRemove(jobId, out _);
        TryDeleteRefinerOutputMarkdownFiles(jobDir);

        await UpdateAndBroadcastAsync(jobId, s =>
        {
            s.Phase = "awaiting_refiner";
            s.Status = "done";
            s.Agent06RefineJobId = null;
            s.RefinerOpenAiRequestPreview = null;
            s.RefinerThreadBatches = Array.Empty<RefinerThreadBatchEntry>();
            s.TranscriptionError = null;
        }, ct).ConfigureAwait(false);
        Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
    }

    private async Task WaitForRefinePausedOrTerminalAsync(string agent06JobId, CancellationToken ct)
    {
        var timeout = TimeSpan.FromMinutes(15);
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            RefineStatusUpdate st;
            try
            {
                st = await _refiner.GetRefineStatusAsync(agent06JobId, ct).ConfigureAwait(false);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
            {
                return;
            }

            var s = st.State ?? "";
            if (s.Equals("Paused", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Completed", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Failed", StringComparison.OrdinalIgnoreCase)
                || s.Equals("Cancelled", StringComparison.OrdinalIgnoreCase)
                || s.Equals("NotFound", StringComparison.OrdinalIgnoreCase))
                return;

            await Task.Delay(200, ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"Agent06 refine job {agent06JobId} did not reach Paused or terminal state within {timeout.TotalMinutes} minutes.");
    }

    private void TryDeleteRefinerOutputMarkdownFiles(string jobDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(jobDirectoryPath) || !Directory.Exists(jobDirectoryPath))
            return;
        try
        {
            var tf = Path.Combine(jobDirectoryPath, "transcript_fixed.md");
            if (File.Exists(tf))
            {
                File.Delete(tf);
                _logger.LogInformation("Deleted {File} for refiner restart", tf);
            }

            foreach (var f in Directory.GetFiles(jobDirectoryPath, "transcript_fixed_*.md"))
            {
                try
                {
                    File.Delete(f);
                    _logger.LogInformation("Deleted {File} for refiner restart", f);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not delete {File}", f);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Refiner output cleanup failed under {Dir}", jobDirectoryPath);
        }
    }

    private async void RunRefinerStatusStreamInBackground(string jobId, string agent06RefineJobId)
    {
        try
        {
            try
            {
                await RunRefinerStatusStreamAsync(jobId, agent06RefineJobId, CancellationToken.None);
            }
            finally
            {
                _pendingAgent06RefineJobIdByXtractJob.TryRemove(jobId, out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refiner status stream failed for job {JobId}", jobId);
        }
    }

    public async Task PauseRefinerAsync(string jobId, CancellationToken ct = default)
    {
        var snap = await _store.GetAsync(jobId, ct);
        if (snap == null) return;
        if (snap.Phase != "refiner" && snap.Phase != "refiner_paused")
        {
            _logger.LogDebug("Job {JobId}: PauseRefiner skipped (phase={Phase})", jobId, snap.Phase);
            return;
        }
        string? agentId = snap.Agent06RefineJobId?.Trim();
        if (string.IsNullOrEmpty(agentId) && _pendingAgent06RefineJobIdByXtractJob.TryGetValue(jobId, out var pendingPause))
            agentId = pendingPause;
        agentId = agentId?.Trim();
        if (string.IsNullOrEmpty(agentId))
        {
            _logger.LogDebug("Job {JobId}: PauseRefiner skipped (no Agent06 refine job id yet)", jobId);
            return;
        }
        var ok = await _refiner.PauseRefineJobAsync(agentId, ct);
        if (!ok)
            _logger.LogWarning(
                "Job {JobId}: Agent06 PauseRefineJob returned false — pause was not queued (Agent06 job may not be Running/Pending)",
                jobId);
    }

    public async Task ResumeRefinerAsync(string jobId, CancellationToken ct = default)
    {
        var snap = await _store.GetAsync(jobId, ct);
        if (snap == null) return;
        if (snap.Phase != "refiner_paused")
            throw new InvalidOperationException($"Resume requires phase refiner_paused, got {snap.Phase}");

        var jobDir = _workspace.GetJobDirectoryPath(jobId);
        string? agentId = snap.Agent06RefineJobId?.Trim();
        if (string.IsNullOrEmpty(agentId) && _pendingAgent06RefineJobIdByXtractJob.TryGetValue(jobId, out var pendingResume))
            agentId = pendingResume;
        agentId = agentId?.Trim();

        await UpdateAndBroadcastAsync(jobId, s =>
        {
            s.Phase = "refiner";
            s.Status = "running";
        }, ct);
        Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));

        string streamAgentId;
        if (!string.IsNullOrEmpty(agentId))
        {
            try
            {
                await _refiner.ResumeRefineJobAsync(agentId, ct);
                streamAgentId = agentId;
            }
            catch (RpcException ex) when (ex.StatusCode is StatusCode.NotFound or StatusCode.FailedPrecondition)
            {
                _logger.LogInformation(
                    ex,
                    "Job {JobId}: Agent06 ResumeRefineJob failed ({Code}); resuming from refiner_threads/checkpoint.json",
                    jobId,
                    ex.StatusCode);
                streamAgentId = await ResumeRefinerFromCheckpointFileAsync(jobId, ct);
            }
        }
        else
            streamAgentId = await ResumeRefinerFromCheckpointFileAsync(jobId, ct);

        await UpdateAndBroadcastAsync(jobId, s => { s.Agent06RefineJobId = streamAgentId; }, ct);
        Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));

        await RunRefinerStatusStreamAsync(jobId, streamAgentId, ct);
    }

    private async Task<string> ResumeRefinerFromCheckpointFileAsync(string jobId, CancellationToken ct)
    {
        var jobDir = _workspace.GetJobDirectoryPath(jobId);
        if (!RefinerCheckpointProgressReader.TryRead(jobDir, out var sum) || sum == null || !sum.CanResume)
            throw new InvalidOperationException(
                "No resumable refiner checkpoint (refiner_threads/checkpoint.json with nextBatchIndex < totalBatches).");
        var result = await _refiner.ResumeRefineFromCheckpointAsync(jobId, _workspace.WorkspaceRootPath, ct);
        return result.JobId;
    }

    public async Task SkipRefinerAsync(string jobId, CancellationToken ct = default)
    {
        var snap = await _store.GetAsync(jobId, ct);
        if (snap == null) return;
        if (snap.Phase != "awaiting_refiner" && snap.Phase != "refiner_paused")
            throw new InvalidOperationException($"Skip refiner requires awaiting_refiner or refiner_paused, got {snap.Phase}");

        var completedAt = DateTime.UtcNow.ToString("O");
        await UpdateAndBroadcastAsync(jobId, s =>
        {
            s.Phase = "completed";
            s.Status = "done";
            s.CompletedAt = completedAt;
        }, ct);
        Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
        Broadcast(jobId, "done", null);
    }

    private async Task RunRefinerStatusStreamAsync(string jobId, string agent06RefineJobId, CancellationToken ct)
    {
        try
        {
            await foreach (var update in _refiner.StreamRefineStatusAsync(agent06RefineJobId, ct))
            {
                if (string.Equals(update.State, "NotFound", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Job {JobId}: Agent06 StreamRefineStatus NotFound for {RefId}",
                        jobId,
                        agent06RefineJobId);
                    return;
                }

                var shouldAppendRefinerLog =
                    !string.IsNullOrEmpty(update.BatchEventKind)
                    || update.State is "Paused" or "Completed" or "Failed" or "Cancelled"
                    || !string.IsNullOrWhiteSpace(update.RefinerLogLine)
                    || (update.TotalBatches > 0
                        && string.Equals(update.State, "Running", StringComparison.OrdinalIgnoreCase));
                await UpdateAndBroadcastAsync(jobId, s =>
                {
                    if (!string.IsNullOrEmpty(update.OpenAiRequestPreview))
                        s.RefinerOpenAiRequestPreview = update.OpenAiRequestPreview;
                    if (update.State == "Paused")
                    {
                        s.Phase = "refiner_paused";
                        s.Status = "running";
                    }
                    else if (update.State == "Completed")
                    {
                        s.Phase = "completed";
                        s.Status = "done";
                        if (string.IsNullOrEmpty(s.CompletedAt))
                            s.CompletedAt = DateTime.UtcNow.ToString("O");
                    }
                    else if (update.State == "Failed" || update.State == "Cancelled")
                    {
                        s.Phase = "awaiting_refiner";
                        s.Status = update.State == "Failed" ? "failed" : "done";
                        if (update.State == "Failed" && !string.IsNullOrEmpty(update.ErrorMessage))
                            s.TranscriptionError = update.ErrorMessage;
                    }

                    RefinerThreadBatchMerge.Apply(s, update);

                    if (shouldAppendRefinerLog)
                    {
                        if (!string.IsNullOrWhiteSpace(update.RefinerLogLine))
                            AppendJobLog(s, "Refiner: " + update.RefinerLogLine.Trim());
                        else
                        {
                            var parts = new List<string> { $"state={update.State}" };
                            if (update.TotalBatches > 0)
                                parts.Add($"batch {update.CurrentBatch}/{update.TotalBatches}");
                            if (!string.IsNullOrEmpty(update.BatchEventKind))
                                parts.Add($"{update.BatchEventKind} idx={update.BatchEventIndex0}");
                            AppendJobLog(s, "Refiner: " + string.Join(", ", parts));
                        }
                    }
                }, ct);
                Broadcast(jobId, "snapshot", await GetSnapshotJsonAsync(jobId));
                var after = await _store.GetAsync(jobId, ct);
                Broadcast(jobId, "status", JsonSerializer.Serialize(new
                {
                    phase = after?.Phase,
                    status = after?.Status,
                    progress_percent = update.ProgressPercent
                }, ApiJson.CamelCase));

                if (update.State == "Paused")
                    return;

                if (update.State == "Completed")
                {
                    Broadcast(jobId, "done", null);
                    return;
                }

                if (update.State is "Failed" or "Cancelled")
                    return;
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            _logger.LogWarning(ex, "Job {JobId}: Agent06 refine stream NotFound for {RefId}", jobId, agent06RefineJobId);
        }
        catch (OperationCanceledException)
        {
            /* normal shutdown */
        }
    }

    private static readonly Regex StemVersionSuffix = new(@"_\d{8}_\d{6}_\d{3}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string TranscriptStemFromMdRelative(string? mdRel)
    {
        if (string.IsNullOrWhiteSpace(mdRel)) return "transcript";
        var baseName = Path.GetFileNameWithoutExtension(mdRel.Trim());
        var stem = StemVersionSuffix.Replace(baseName, "");
        return string.IsNullOrEmpty(stem) ? "transcript" : stem;
    }

    /// <summary>
    /// Latest job-root transcript: <c>{stem}_yyyyMMdd_HHmmss_fff.md</c> (lexicographic max) or <c>{stem}.md</c>,
    /// excluding refiner outputs. Mirrors UI <c>transcriptStemLatest</c>.
    /// </summary>
    private static string? TryFindLatestStemMarkdownPath(string jobDir, string stem)
    {
        if (!Directory.Exists(jobDir)) return null;
        var versionedPattern = new Regex(
            "^" + Regex.Escape(stem) + @"_\d{8}_\d{6}_\d{3}\.md$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var candidates = new List<string>();
        foreach (var fi in new DirectoryInfo(jobDir).EnumerateFiles("*.md"))
        {
            var n = fi.Name;
            if (n.Equals("transcript_fixed.md", StringComparison.OrdinalIgnoreCase)) continue;
            if (Regex.IsMatch(n, @"^transcript_fixed_\d+\.md$", RegexOptions.IgnoreCase)) continue;
            if (versionedPattern.IsMatch(n))
                candidates.Add(fi.FullName);
        }
        if (candidates.Count > 0)
        {
            candidates.Sort(StringComparer.OrdinalIgnoreCase);
            return candidates[^1];
        }
        var plain = Path.Combine(jobDir, stem + ".md");
        return File.Exists(plain) ? plain : null;
    }

    private static bool IsFullPathUnderDirectory(string path, string directory)
    {
        var dir = Path.GetFullPath(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var p = Path.GetFullPath(path);
        return p.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || string.Equals(p, dir, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string?> LoadTranscriptContentAsync(
        string jobId,
        string workspaceRoot,
        string jobDir,
        JobSnapshot snap,
        string? transcriptRelativePath,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(transcriptRelativePath))
        {
            var rel = transcriptRelativePath.Trim().TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            var preferred = Path.GetFullPath(Path.Combine(jobDir, rel));
            if (!IsFullPathUnderDirectory(preferred, jobDir))
            {
                _logger.LogWarning("Job {JobId}: transcript path {Rel} is not under job dir; ignoring", jobId, rel);
            }
            else if (File.Exists(preferred))
            {
                try
                {
                    _logger.LogInformation("Job {JobId}: using UI-selected transcript {Path}", jobId, preferred);
                    return await File.ReadAllTextAsync(preferred, Encoding.UTF8, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Job {JobId}: could not read transcript at {Path}", jobId, preferred);
                }
            }
            else
            {
                _logger.LogWarning("Job {JobId}: UI-selected transcript not found: {Path}", jobId, preferred);
            }
        }

        var mdRel = snap.MdOutputPath?.Trim().TrimStart('/', '\\');
        _logger.LogInformation("Job {JobId}: resolving transcript, MdOutputPath(rel)={MdRel}, workspaceRoot={WorkspaceRoot}, jobDir={JobDir}",
            jobId, mdRel ?? "null", workspaceRoot, jobDir);
        if (!string.IsNullOrEmpty(mdRel))
        {
            var mdFull = Path.Combine(workspaceRoot, mdRel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(mdFull))
            {
                try
                {
                    return await File.ReadAllTextAsync(mdFull, Encoding.UTF8, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Job {JobId}: could not read transcript at {Path}", jobId, mdFull);
                }
            }
            var sameNameInJobDir = Path.Combine(jobDir, Path.GetFileName(mdRel));
            if (File.Exists(sameNameInJobDir))
            {
                try
                {
                    _logger.LogInformation("Job {JobId}: using transcript in job dir {Path}", jobId, sameNameInJobDir);
                    return await File.ReadAllTextAsync(sameNameInJobDir, Encoding.UTF8, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Job {JobId}: could not read transcript at {Path}", jobId, sameNameInJobDir);
                }
            }
        }
        var fallbackMd = Path.Combine(jobDir, "transcript.md");
        if (File.Exists(fallbackMd))
        {
            try
            {
                return await File.ReadAllTextAsync(fallbackMd, Encoding.UTF8, ct);
            }
            catch { /* ignore */ }
        }
        var stem = TranscriptStemFromMdRelative(mdRel);
        var latest = TryFindLatestStemMarkdownPath(jobDir, stem);
        if (latest != null)
        {
            try
            {
                _logger.LogInformation("Job {JobId}: using latest stem transcript {Path} (stem={Stem})", jobId, latest, stem);
                return await File.ReadAllTextAsync(latest, Encoding.UTF8, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Job {JobId}: could not read transcript at {Path}", jobId, latest);
            }
        }
        return null;
    }

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

    private static Task CleanupRefinerArtifactsAsync(string jobDir, CancellationToken ct)
    {
        _ = ct;
        var threads = Path.Combine(jobDir, "refiner_threads");
        if (Directory.Exists(threads))
        {
            try
            {
                foreach (var fi in new DirectoryInfo(threads).EnumerateFileSystemInfos())
                {
                    if (fi is FileInfo f)
                        f.Delete();
                    else if (fi is DirectoryInfo d)
                        d.Delete(true);
                }
            }
            catch
            {
                /* best-effort */
            }
        }
        foreach (var name in new[] { "transcript_fixed.md", "refiner_debug.log" })
        {
            try
            {
                var p = Path.Combine(jobDir, name);
                if (File.Exists(p))
                    File.Delete(p);
            }
            catch
            {
                /* ignore */
            }
        }
        return Task.CompletedTask;
    }

    private static void AppendJobLog(JobSnapshot s, string message)
    {
        var list = new List<LogEntry>();
        if (s.Logs != null && s.Logs.Count > 0)
            list.AddRange(s.Logs);
        list.Add(new LogEntry
        {
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Level = "info",
            Message = message
        });
        s.Logs = list;
    }

    private async Task<bool> UpdateAndBroadcastAsync(string jobId, Action<JobSnapshot> update, CancellationToken ct = default)
    {
        var ok = await _store.UpdateAsync(jobId, update, ct);
        if (!ok)
            _logger.LogWarning("Job {JobId}: IJobStore.UpdateAsync returned false — snapshot mutation skipped (unknown job id?)", jobId);
        var fresh = await _store.GetAsync(jobId, ct);
        if (fresh != null)
            await JobSnapshotDiskEnricher.TryWriteUiStateAsync(_workspace.GetJobDirectoryPath(jobId), fresh, ct);
        return ok;
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
