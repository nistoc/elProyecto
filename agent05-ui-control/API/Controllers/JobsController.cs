using System.Text;
using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using XtractManager.Infrastructure;

namespace XtractManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly IJobStore _store;
    private readonly IJobWorkspace _workspace;
    private readonly IPipelineQueue _pipelineQueue;
    private readonly IBroadcaster _broadcaster;
    private readonly ITranscriptionServiceClient _transcription;
    private readonly ILogger<JobsController> _logger;
    private readonly IHostApplicationLifetime? _lifetime;

    public JobsController(
        IJobStore store,
        IJobWorkspace workspace,
        IPipelineQueue pipelineQueue,
        IBroadcaster broadcaster,
        ITranscriptionServiceClient transcription,
        ILogger<JobsController> logger,
        IHostApplicationLifetime? lifetime = null)
    {
        _store = store;
        _workspace = workspace;
        _pipelineQueue = pipelineQueue;
        _broadcaster = broadcaster;
        _transcription = transcription;
        _logger = logger;
        _lifetime = lifetime;
    }

    [HttpPost]
    [RequestSizeLimit(1024 * 1024 * 512)] // 512 MB
    [RequestFormLimits(MultipartBodyLengthLimit = 1024 * 1024 * 512)]
    public async Task<ActionResult<CreateJobResponse>> Create(
        [FromForm] IFormFile file,
        [FromForm] string? tags,
        [FromForm] string? settings = null,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ToProblemDetails("File is required and must not be empty."));

        // Settings of this one job (pause length, compression, model); checked before anything is stored.
        if (!JobSettingsDocument.TryBuild(settings, out var settingsFileJson, out var settingsError))
            return BadRequest(ToProblemDetails(settingsError ?? "settings are not valid."));

        var tagsList = ParseTags(tags);
        var originalFilename = file.FileName ?? "audio.bin";

        var jobId = await _store.CreateAsync(new JobCreateInput(originalFilename, tagsList), ct);
        try
        {
            await _workspace.EnsureJobDirectoryAsync(jobId, ct);
            await using var stream = file.OpenReadStream();
            await _workspace.SaveUploadedFileAsync(jobId, stream, originalFilename, ct);
            if (settingsFileJson != null)
            {
                // The transcriber reads this file from the job folder and puts it on top of its shared config.
                await System.IO.File.WriteAllTextAsync(
                    Path.Combine(_workspace.GetJobDirectoryPath(jobId), JobSettingsDocument.FileName),
                    settingsFileJson,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save upload for job {JobId}", jobId);
            await _store.DeleteAsync(jobId, ct);
            return StatusCode(500, ToProblemDetails("Failed to save uploaded file."));
        }

        _pipelineQueue.Enqueue(jobId);

        return Accepted(new CreateJobResponse(jobId));
    }

    private static IReadOnlyList<string>? ParseTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return null;
        var list = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return list.Length > 0 ? list : null;
    }

    private async Task PublishEnrichedSnapshotAsync(string jobId, CancellationToken ct)
    {
        var jobSnap = await _store.GetAsync(jobId, ct);
        if (jobSnap == null) return;
        jobSnap.JobDirectoryPath ??= _workspace.GetJobDirectoryPath(jobId);
        await MergeAgent04LiveIntoSnapshotAsync(jobSnap, ct).ConfigureAwait(false);
        var snapshotJson = JsonSerializer.Serialize(new { type = "snapshot", payload = jobSnap }, ApiJson.CamelCase);
        _broadcaster.Publish(jobId, new BroadcastEvent("snapshot", snapshotJson));
    }

    /// <summary>
    /// Fills <see cref="JobSnapshot.Chunks.ChunkVirtualModel"/> and footer from Agent04 after main job Completed (retranscribe, long HTTP).
    /// </summary>
    private async Task MergeAgent04LiveIntoSnapshotAsync(JobSnapshot snap, CancellationToken ct)
    {
        var agentId = snap.Agent04JobId?.Trim();
        if (string.IsNullOrEmpty(agentId))
            return;
        try
        {
            var prevVm = snap.Chunks?.ChunkVirtualModel;
            var live = await _transcription
                .GetJobStatusAsync(agentId, prevVm, ct)
                .ConfigureAwait(false);
            if (live == null)
                return;
            if (live.ChunkVirtualModel is { Count: > 0 })
            {
                snap.Chunks ??= new ChunkState();
                var prevCount = prevVm?.Count ?? 0;
                var mergedVm = live.ChunkVirtualModel;
                snap.Chunks.ChunkVirtualModel = mergedVm;
                snap.TranscriptionSyncDebug =
                    $"{DateTime.UtcNow:O} liveVm={live.ChunkVirtualModel.Count} prevVm={prevCount} mergedVm={mergedVm.Count} (server) " +
                    $"footer={(string.IsNullOrWhiteSpace(live.TranscriptionFooterHint) ? 0 : 1)}";
            }
            else
                snap.TranscriptionSyncDebug =
                    $"{DateTime.UtcNow:O} liveVm=0 prevVm={(snap.Chunks?.ChunkVirtualModel?.Count ?? 0)} mergedVm=skip";

            if (!string.IsNullOrWhiteSpace(live.TranscriptionFooterHint))
                snap.TranscriptionFooterHint = live.TranscriptionFooterHint;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MergeAgent04LiveIntoSnapshotAsync failed for Xtract job (Agent04 {AgentId})", agentId);
        }
    }

    private void ScheduleTranscribeSubFollowUpSnapshots(string jobId)
    {
        // One task walking the delay ladder (same absolute marks as before: 0.6s … 120s),
        // cancelled on host shutdown and stopped once the job disappears from the store.
        var ct = _lifetime?.ApplicationStopping ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var deltaMs in new[] { 600, 1400, 4000, 9000, 15000, 30000, 60000 })
                {
                    await Task.Delay(deltaMs, ct).ConfigureAwait(false);
                    if (await _store.GetAsync(jobId, ct).ConfigureAwait(false) == null)
                        return;
                    await PublishEnrichedSnapshotAsync(jobId, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // host shutting down
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Follow-up snapshot after transcribe_sub for job {JobId}", jobId);
            }
        }, CancellationToken.None);
    }

    private static ProblemDetails ToProblemDetails(string detail) =>
        new() { Title = "Bad Request", Status = 400, Detail = detail };

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<JobListItem>>> List(
        [FromQuery] string? semanticKey,
        [FromQuery] string? status,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var filter = new JobListFilter(semanticKey, status, from, to, limit, offset);
        var list = await _store.ListAsync(filter, ct);
        return Ok(new { jobs = list });
    }

    /// <summary>Structured project files for UI (same categories as agent-browser). Job must exist in store or as a directory on disk.</summary>
    [HttpGet("{id}/files")]
    public async Task<ActionResult> GetProjectFiles(string id, CancellationToken ct = default)
    {
        var job = await _store.GetAsync(id, ct);
        var jobDir = _workspace.GetJobDirectoryPath(id);
        if (!Directory.Exists(jobDir))
        {
            if (job == null)
                return NotFound();
            return NotFound(new { error = "job directory not found" });
        }

        if (job == null)
        {
            _logger.LogDebug("GetProjectFiles({Id}): archive job (directory only)", id);
        }

        var scopeJobId = job != null && !string.IsNullOrWhiteSpace(job.Agent04JobId)
            ? job.Agent04JobId.Trim()
            : id;
        var remote = await _transcription
            .GetProjectFilesAsync(scopeJobId, id, ct)
            .ConfigureAwait(false);
        JobProjectFiles files;
        if (remote != null)
        {
            files = remote;
            _logger.LogDebug(
                "GetProjectFiles({Id}): from Agent04 scope={Scope}, original={O}, chunks={C}",
                id,
                scopeJobId,
                files.Original.Count,
                files.Chunks.Count);
        }
        else
        {
            files = JobProjectFilesScanner.Scan(jobDir);
            _logger.LogDebug(
                "GetProjectFiles({Id}): gRPC unavailable, local scan jobDir={Path}",
                id,
                jobDir);
        }

        return Ok(new { files, jobDir });
    }

    /// <summary>Chunk/split artifact groups from Agent04 (file metadata). Uses <c>Agent04JobId</c> when set; otherwise resolves artifacts by Xtract job folder <paramref name="id"/> (archives). UI merges <c>chunkVirtualModel</c> from the job snapshot.</summary>
    [HttpGet("{id}/chunk-artifact-groups")]
    public async Task<ActionResult> GetChunkArtifactGroups(string id, CancellationToken ct = default)
    {
        var job = await _store.GetAsync(id, ct);
        var jobDir = _workspace.GetJobDirectoryPath(id);
        if (job == null && !Directory.Exists(jobDir))
            return NotFound();

        var totalChunks = job?.Chunks?.Total ?? 0;
        var scopeJobId = job != null && !string.IsNullOrWhiteSpace(job.Agent04JobId)
            ? job.Agent04JobId.Trim()
            : id;
        var clientVm = job?.Chunks?.ChunkVirtualModel;
        try
        {
            var result = await _transcription
                .GetChunkArtifactGroupsAsync(scopeJobId, id, totalChunks, clientVm, ct)
                .ConfigureAwait(false);
            if (result == null)
                return StatusCode(502, new { error = "agent04_chunk_groups_unavailable" });
            return Ok(new { groups = result.Groups });
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "GetChunkArtifactGroups gRPC failed for job {JobId}", id);
            return ex.StatusCode switch
            {
                Grpc.Core.StatusCode.InvalidArgument => BadRequest(new { error = ex.Status.Detail }),
                Grpc.Core.StatusCode.NotFound => NotFound(new { error = ex.Status.Detail }),
                _ => StatusCode(502, new { error = ex.Status.Detail })
            };
        }
    }

    /// <summary>Stream a file from the job directory (relative path only). Supports range requests for audio.</summary>
    [HttpGet("{id}/files/content")]
    public IActionResult GetProjectFileContent(string id, [FromQuery] string? path)
    {
        if (!TryResolveJobContentPath(id, path, out var fullPath, out var error))
            return error!;
        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { error = "file not found" });

        var contentType = GuessContentType(fullPath);
        return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
    }

    /// <summary>Overwrite an existing text file under the job directory (UTF-8). Same allowed extensions as agent-browser PUT /files/*.</summary>
    [HttpPut("{id}/files/content")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> PutProjectFileContent(string id, [FromQuery] string? path, CancellationToken ct = default)
    {
        if (!TryResolveJobContentPath(id, path, out var fullPath, out var error))
            return error!;
        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { error = "file not found" });
        if (!IsEditableTextExtension(fullPath))
            return BadRequest(new { error = "only text-like files can be saved (md, txt, json, log, …)" });

        string body;
        try
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            body = await reader.ReadToEndAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PutProjectFileContent({Id}): failed to read body", id);
            return StatusCode(400, new { error = "failed to read request body" });
        }

        try
        {
            await System.IO.File.WriteAllTextAsync(fullPath, body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PutProjectFileContent({Id}): write failed for {Path}", id, fullPath);
            return StatusCode(500, new { error = "failed to write file" });
        }

        _logger.LogDebug("PutProjectFileContent({Id}): saved {Path}, bytes={Len}", id, fullPath, body.Length);
        return Ok(new { ok = true, message = "file saved successfully" });
    }

    /// <summary>Delete a single file under the job directory (not a directory).</summary>
    [HttpDelete("{id}/files/content")]
    public async Task<IActionResult> DeleteProjectFileContent(string id, [FromQuery] string? path, CancellationToken ct = default)
    {
        if (!TryResolveJobContentPath(id, path, out var fullPath, out var error))
            return error!;
        if (!System.IO.File.Exists(fullPath))
            return NotFound(new { error = "file not found" });
        var attrs = System.IO.File.GetAttributes(fullPath);
        if ((attrs & FileAttributes.Directory) != 0)
            return BadRequest(new { error = "path must be a file, not a directory" });
        try
        {
            System.IO.File.Delete(fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DeleteProjectFileContent({Id}): failed for {Path}", id, fullPath);
            return StatusCode(500, new { error = "failed to delete file" });
        }

        await PublishEnrichedSnapshotAsync(id, ct).ConfigureAwait(false);
        return Ok(new { ok = true, message = "file deleted" });
    }

    /// <summary>Delete operator-split sub-chunk artifacts (audio, result json, cancel flag, work-state row).</summary>
    [HttpDelete("{id}/chunks/{parentIndex:int}/sub-chunks/{subIndex:int}")]
    public async Task<IActionResult> DeleteSubChunkArtifacts(
        string id,
        int parentIndex,
        int subIndex,
        CancellationToken ct = default)
    {
        if (parentIndex < 0 || subIndex < 0)
            return BadRequest(new { error = "parentIndex and subIndex must be >= 0" });
        var job = await _store.GetAsync(id, ct);
        if (job == null)
            return NotFound();
        var jobDir = _workspace.GetJobDirectoryPath(id);
        if (!Directory.Exists(jobDir))
            return NotFound(new { error = "job directory not found" });

        if (OperatorSubChunkArtifacts.IsSubChunkRunning(job, parentIndex, subIndex))
            return Conflict(new { error = "sub_chunk_running" });

        var (ok, message) = await OperatorSubChunkArtifacts.TryDeleteBundleAsync(
            jobDir,
            job.Agent04JobId,
            parentIndex,
            subIndex,
            "split_chunks",
            job,
            _logger,
            ct).ConfigureAwait(false);
        if (!ok)
            return StatusCode(500, new { error = message });

        await PublishEnrichedSnapshotAsync(id, ct).ConfigureAwait(false);
        return Ok(new { ok = true, message });
    }

    /// <summary>Resolves <paramref name="path"/> to an absolute file path under the job directory, or sets <paramref name="error"/>.</summary>
    private bool TryResolveJobContentPath(string id, string? path, out string fullPath, out IActionResult? error)
    {
        error = null;
        fullPath = "";

        if (string.IsNullOrWhiteSpace(path))
        {
            error = BadRequest(new { error = "path query parameter is required" });
            return false;
        }

        var jobDir = Path.GetFullPath(_workspace.GetJobDirectoryPath(id));
        if (!Directory.Exists(jobDir))
        {
            error = NotFound(new { error = "job directory not found" });
            return false;
        }

        var normalized = path.Replace('\\', '/').TrimStart('/');
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "." or "..")
            {
                error = BadRequest(new { error = "invalid path" });
                return false;
            }
        }

        fullPath = Path.GetFullPath(Path.Combine(jobDir, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var jobDirWithSep = jobDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(jobDirWithSep, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, jobDir, StringComparison.OrdinalIgnoreCase))
        {
            error = StatusCode(403, new { error = "access denied" });
            return false;
        }

        return true;
    }

    private static bool IsEditableTextExtension(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        return EditableTextExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] EditableTextExtensions =
    {
        ".md", ".txt", ".json", ".log", ".text", ".srt", ".vtt", ".csv", ".xml", ".flag"
    };

    private static string GuessContentType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".ogg" => "audio/ogg",
            ".flac" => "audio/flac",
            ".md" => "text/markdown; charset=utf-8",
            ".txt" => "text/plain; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".log" => "text/plain; charset=utf-8",
            ".srt" => "text/plain; charset=utf-8",
            ".vtt" => "text/vtt; charset=utf-8",
            _ => "application/octet-stream",
        };
    }

    /// <summary>Forward chunk operator action to Agent04. Actions: cancel, retranscribe, split, transcribe_sub, rebuild_combined, rebuild_split_merged (see body.action).</summary>
    [HttpPost("{id}/chunk-actions")]
    public async Task<ActionResult<ChunkActionResponse>> PostChunkAction(
        string id,
        [FromBody] ChunkActionRequest? body,
        CancellationToken ct = default)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.Action))
            return BadRequest(new { error = "action is required (cancel | retranscribe | split | transcribe_sub | rebuild_combined | rebuild_split_merged)" });
        if (body.ChunkIndex < 0)
            return BadRequest(new { error = "chunkIndex must be >= 0" });

        var job = await _store.GetAsync(id, ct);
        if (job == null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(job.Agent04JobId))
            return Conflict(new { error = "Agent04 job id not available yet; wait for transcription to start" });

        var action = ParseChunkAction(body.Action.Trim());
        if (action == null)
            return BadRequest(new { error = "unknown action", allowed = new[] { "cancel", "retranscribe", "split", "transcribe_sub", "rebuild_combined", "rebuild_split_merged" } });

        var allowAfterDone = action == TranscriptionChunkAction.Split
            || action == TranscriptionChunkAction.TranscribeSub
            || action == TranscriptionChunkAction.Retranscribe
            || action == TranscriptionChunkAction.RebuildCombined
            || action == TranscriptionChunkAction.RebuildSplitMerged;
        if (action == TranscriptionChunkAction.Cancel)
        {
            var st = job.Status ?? "";
            var running = string.Equals(st, "running", StringComparison.OrdinalIgnoreCase);
            var doneLike = string.Equals(st, "done", StringComparison.OrdinalIgnoreCase)
                || string.Equals(st, "completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(st, "failed", StringComparison.OrdinalIgnoreCase);
            if (!running && !doneLike)
                return Conflict(new { error = "cancel requires status running, done, completed, or failed" });
            if (running && !string.Equals(job.Phase, "transcriber", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { error = "this action requires phase=transcriber and status=running" });
        }
        else if (!allowAfterDone && (job.Phase != "transcriber" || job.Status != "running"))
            return Conflict(new { error = "this action requires phase=transcriber and status=running" });
        if (allowAfterDone
            && !string.Equals(job.Status, "running", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(job.Status, "done", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(job.Status, "failed", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = "split / transcribe_sub / retranscribe / rebuild_combined / rebuild_split_merged require status running, done, completed, or failed" });

        if (action == TranscriptionChunkAction.Split && (body.SplitParts is null || body.SplitParts < 2))
            return BadRequest(new { error = "split requires splitParts >= 2" });
        if (action == TranscriptionChunkAction.TranscribeSub && (body.SubChunkIndex is null || body.SubChunkIndex < 0))
            return BadRequest(new { error = "transcribe_sub requires subChunkIndex >= 0" });
        if (action == TranscriptionChunkAction.Cancel && body.SubChunkIndex is int badSub && badSub < 0)
            return BadRequest(new { error = "subChunkIndex must be >= 0 when provided for cancel" });

        if (action == TranscriptionChunkAction.Retranscribe)
        {
            var jobDir = _workspace.GetJobDirectoryPath(id);
            if (Directory.Exists(jobDir) &&
                OperatorSplitArtifactPresence.HasArtifactsForChunk(jobDir, body.ChunkIndex))
                return Conflict(new { error = "retranscribe_blocked_operator_split_present" });
        }

        try
        {
            var splitParts = body.SplitParts ?? 0;
            var subChunkIndexForGrpc = action == TranscriptionChunkAction.Cancel
                ? (body.SubChunkIndex is { } csi && csi >= 0 ? csi : -1)
                : (body.SubChunkIndex ?? 0);
            var result = await _transcription.ChunkCommandAsync(
                job.Agent04JobId!,
                action.Value,
                body.ChunkIndex,
                id,
                splitParts,
                subChunkIndexForGrpc,
                ct);
            if (result.Ok)
            {
                await PublishEnrichedSnapshotAsync(id, ct);
                if (string.Equals(result.Message, "transcribe_sub_started", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(result.Message, "retranscribe_started", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(result.Message, "rebuild_combined_started", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(result.Message, "rebuild_split_merged_ok", StringComparison.OrdinalIgnoreCase))
                    ScheduleTranscribeSubFollowUpSnapshots(id);
            }
            return Ok(new ChunkActionResponse(result.Ok, result.Message));
        }
        catch (RpcException ex)
        {
            _logger.LogWarning(ex, "ChunkCommand gRPC failed for job {JobId}", id);
            return ex.StatusCode switch
            {
                Grpc.Core.StatusCode.InvalidArgument => BadRequest(new { error = ex.Status.Detail }),
                Grpc.Core.StatusCode.NotFound => NotFound(new { error = ex.Status.Detail }),
                Grpc.Core.StatusCode.FailedPrecondition => Conflict(new { error = ex.Status.Detail }),
                _ => StatusCode(502, new { error = ex.Status.Detail })
            };
        }
    }

    private static TranscriptionChunkAction? ParseChunkAction(string a) =>
        a.ToLowerInvariant() switch
        {
            "cancel" => TranscriptionChunkAction.Cancel,
            "retranscribe" => TranscriptionChunkAction.Retranscribe,
            "split" => TranscriptionChunkAction.Split,
            "transcribe_sub" => TranscriptionChunkAction.TranscribeSub,
            "rebuild_combined" => TranscriptionChunkAction.RebuildCombined,
            "rebuild_split_merged" => TranscriptionChunkAction.RebuildSplitMerged,
            _ => null
        };

    [HttpGet("{id}")]
    public async Task<ActionResult<JobSnapshot>> Get(string id, CancellationToken ct = default)
    {
        var job = await _store.GetAsync(id, ct);
        if (job == null)
            return NotFound();
        job.JobDirectoryPath ??= _workspace.GetJobDirectoryPath(id);
        await MergeAgent04LiveIntoSnapshotAsync(job, ct).ConfigureAwait(false);
        _logger.LogDebug("Get({Id}): JobDirectoryPath={Path}, Chunks={Chunks}, Result={Result}, MdOutputPath={Md}",
            id, job.JobDirectoryPath, job.Chunks != null ? "set" : "null", job.Result != null ? "set" : "null", job.MdOutputPath ?? "null");
        return Ok(job);
    }

    /// <summary>
    /// How much of the job's speech is Spanish and how much Russian, counted from the chunk answers in the job
    /// folder (<see cref="JobLanguageShareReader"/>). A dialogue job gets one entry per track and their sum.
    /// Read on request, not with every snapshot: it opens every chunk answer of the job.
    /// </summary>
    [HttpGet("{id}/language-share")]
    public ActionResult<JobLanguageShareReport> GetLanguageShare(string id)
    {
        var jobDir = _workspace.GetJobDirectoryPath(id);
        if (!Directory.Exists(jobDir))
            return NotFound();
        return Ok(JobLanguageShareReader.ReadReport(jobDir, _workspace.GetJobDirectoryPath));
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id, CancellationToken ct = default)
    {
        var removed = await _store.DeleteAsync(id, ct);
        if (!removed)
            return NotFound();
        return NoContent();
    }

    /// <summary>
    /// Manually starts the refiner (agent06) for a job whose transcription finished, or skips it.
    /// The refiner never runs automatically — the operator reviews the transcript first.
    /// Body: <c>{ "skip": bool, "inputFile"?: string, "prompt"?: "lesson" | "saved" }</c> (<see cref="RefinerRunPlan"/>).
    /// Allowed when phase is <c>awaiting_refiner</c> or <c>completed</c> (re-run), or when a transcript file exists on disk.
    /// </summary>
    [HttpPost("{id}/refine")]
    public async Task<IActionResult> StartRefine(string id, [FromBody] RefineActionRequest? body, CancellationToken ct = default)
    {
        var job = await _store.GetAsync(id, ct);
        if (job == null)
            return NotFound();

        var phase = job.Phase ?? "";
        var phaseAllows = string.Equals(phase, "awaiting_refiner", StringComparison.OrdinalIgnoreCase)
            || string.Equals(phase, "completed", StringComparison.OrdinalIgnoreCase);
        var jobDir = _workspace.GetJobDirectoryPath(id);
        var transcriptOnDisk = Directory.Exists(jobDir) &&
            new DirectoryInfo(jobDir).EnumerateFiles("transcript*.md")
                .Any(f => !f.Name.Contains("_fixed", StringComparison.OrdinalIgnoreCase));
        if (!phaseAllows && !transcriptOnDisk)
            return Conflict(new { error = "refiner can start only after transcription produced a transcript (phase awaiting_refiner/completed or transcript*.md on disk)" });

        if (string.Equals(phase, "refiner", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = "refiner is already running for this job" });

        var lessonPrompt = RefinerRunPlan.TryParsePrompt(body?.Prompt);
        if (lessonPrompt == null)
            return BadRequest(new { error = $"prompt must be \"{RefinerRunPlan.PromptLesson}\" or \"{RefinerRunPlan.PromptSaved}\"" });

        if (body?.Skip == true)
        {
            await _store.UpdateAsync(id, s => { s.Phase = "completed"; s.Status = "done"; s.CompletedAt = DateTime.UtcNow.ToString("O"); });
            await PublishEnrichedSnapshotAsync(id, ct);
            _broadcaster.Publish(id, new BroadcastEvent("done", "{\"type\":\"done\"}"));
            return Ok(new { ok = true, message = "refiner_skipped" });
        }

        // Validate an explicitly chosen input file: must be a bare name of an existing
        // combined transcript (not a _fixed output) directly in the job folder.
        string? chosen = null;
        var requested = body?.InputFile?.Trim();
        if (!string.IsNullOrEmpty(requested))
        {
            var name = Path.GetFileName(requested);
            if (name.Contains("_fixed", StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || !System.IO.File.Exists(Path.Combine(jobDir, name)))
                return BadRequest(new { error = "inputFile must be an existing combined transcript*.md (not a _fixed output) in the job folder" });
            chosen = name;
        }

        // Flip to refiner/running BEFORE queueing so a client that reopens its stream right after this
        // response sees a non-terminal snapshot (a stale 'completed' would make it close the stream again).
        await _store.UpdateAsync(id, s => { s.Phase = "refiner"; s.Status = "running"; });
        await PublishEnrichedSnapshotAsync(id, ct);
        _pipelineQueue.EnqueueRefiner(id, chosen, lessonPrompt.Value);
        return Accepted(new
        {
            ok = true,
            message = "refiner_started",
            inputFile = chosen,
            prompt = lessonPrompt.Value ? RefinerRunPlan.PromptLesson : RefinerRunPlan.PromptSaved,
        });
    }

    /// <summary>SSE stream: snapshot first, then status/snapshot/done events; ": ping" comments keep the connection alive.</summary>
    [HttpGet("{id}/stream")]
    public async Task Stream(string id, CancellationToken ct = default)
    {
        var job = await _store.GetAsync(id, ct);
        if (job == null)
        {
            Response.StatusCode = 404;
            return;
        }
        job.JobDirectoryPath ??= _workspace.GetJobDirectoryPath(id);
        _logger.LogInformation("Stream({Id}): sending snapshot, JobDirectoryPath={Path}, Chunks={Chunks}, MdOutputPath={Md}",
            id, job.JobDirectoryPath, job.Chunks != null ? "set" : "null", job.MdOutputPath ?? "null");
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        await Response.StartAsync(ct);

        var channel = System.Threading.Channels.Channel.CreateBounded<BroadcastEvent>(
            new System.Threading.Channels.BoundedChannelOptions(256)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest,
            });
        // Subscribe BEFORE writing the snapshot so events published in between are not lost.
        using var subscription = _broadcaster.Subscribe(id, channel.Writer);

        var snapshotJson = JsonSerializer.Serialize(new { type = "snapshot", payload = job }, ApiJson.CamelCase);
        await Response.WriteAsync($"data: {snapshotJson}\n\n", ct);
        await Response.Body.FlushAsync(ct);

        using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var heartbeatTask = Task.Run(async () =>
        {
            try
            {
                while (await heartbeat.WaitForNextTickAsync(ct))
                    channel.Writer.TryWrite(new BroadcastEvent("ping", ""));
            }
            catch (OperationCanceledException) { }
        }, CancellationToken.None);

        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                if (evt.Type == "ping")
                    await Response.WriteAsync(": ping\n\n", ct);
                else
                    await Response.WriteAsync($"data: {evt.Json}\n\n", ct);
                await Response.Body.FlushAsync(ct);
                if (evt.Type == "done")
                    break;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            // Client went away — normal termination for an SSE request.
        }
        finally
        {
            heartbeat.Dispose();
            await heartbeatTask;
        }
    }
}

public record CreateJobResponse(string JobId);

public sealed class ChunkActionRequest
{
    public string Action { get; set; } = "";
    public int ChunkIndex { get; set; }
    public int? SplitParts { get; set; }
    /// <summary>For transcribe_sub: 0-based sub-chunk index (parent chunk = <see cref="ChunkIndex"/>).</summary>
    public int? SubChunkIndex { get; set; }
}

public record ChunkActionResponse(bool Ok, string Message);

public sealed class RefineActionRequest
{
    /// <summary>When true, mark the job completed without running the refiner.</summary>
    public bool Skip { get; set; }

    /// <summary>Optional job-relative transcript file name to refine (e.g. <c>transcript_20260718_194306_499.md</c>). When null, the newest combined transcript is used.</summary>
    public string? InputFile { get; set; }

    /// <summary>
    /// What the run goes with: <c>lesson</c> — the refiner asks the model only about the words in doubt and
    /// writes their spelling itself; <c>saved</c> or nothing — the settings saved on the Refiner tab, every line
    /// sent to the model (<see cref="RefinerRunPlan"/>).
    /// </summary>
    public string? Prompt { get; set; }
}
