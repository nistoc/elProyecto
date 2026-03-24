using System.Linq;
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
    private readonly IPipeline _pipeline;
    private readonly IRefinerOrchestration _refinerOrchestration;
    private readonly IBroadcaster _broadcaster;
    private readonly ITranscriptionServiceClient _transcription;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        IJobStore store,
        IJobWorkspace workspace,
        IPipeline pipeline,
        IRefinerOrchestration refinerOrchestration,
        IBroadcaster broadcaster,
        ITranscriptionServiceClient transcription,
        ILogger<JobsController> logger)
    {
        _store = store;
        _workspace = workspace;
        _pipeline = pipeline;
        _refinerOrchestration = refinerOrchestration;
        _broadcaster = broadcaster;
        _transcription = transcription;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(1024 * 1024 * 512)] // 512 MB
    [RequestFormLimits(MultipartBodyLengthLimit = 1024 * 1024 * 512)]
    public async Task<ActionResult<CreateJobResponse>> Create(
        [FromForm] IFormFile file,
        [FromForm] string? tags,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest(ToProblemDetails("File is required and must not be empty."));

        var tagsList = ParseTags(tags);
        var originalFilename = file.FileName ?? "audio.bin";

        var jobId = await _store.CreateAsync(new JobCreateInput(originalFilename, tagsList), ct);
        try
        {
            await _workspace.EnsureJobDirectoryAsync(jobId, ct);
            await using var stream = file.OpenReadStream();
            await _workspace.SaveUploadedFileAsync(jobId, stream, originalFilename, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save upload for job {JobId}", jobId);
            await _store.DeleteAsync(jobId, ct);
            return StatusCode(500, ToProblemDetails("Failed to save uploaded file."));
        }

        RunPipelineInBackground(jobId);

        return Accepted(new CreateJobResponse(jobId));
    }

    private static IReadOnlyList<string>? ParseTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return null;
        var list = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return list.Length > 0 ? list : null;
    }

    private async void RunPipelineInBackground(string jobId)
    {
        try
        {
            await _pipeline.RunAsync(jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pipeline failed for job {JobId}", jobId);
        }
    }

    public sealed record RefinerStartBody(string? TranscriptRelativePath);

    [HttpPost("{id}/refiner/start")]
    public async Task<ActionResult> StartRefiner(string id, [FromBody] RefinerStartBody? body = null, CancellationToken ct = default)
    {
        var rel = body?.TranscriptRelativePath?.Trim();
        if (rel is { Length: > 0 })
            _logger.LogInformation("Refiner start requested for job {JobId} with transcript path {Path}", id, rel);
        try
        {
            await _refinerOrchestration.StartRefinerAsync(id, string.IsNullOrEmpty(rel) ? null : rel, ct);
            return Accepted(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refiner start failed for job {JobId}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("{id}/refiner/pause")]
    public async Task<ActionResult> PauseRefiner(string id, CancellationToken ct = default)
    {
        try
        {
            await _refinerOrchestration.PauseRefinerAsync(id, ct);
            return Ok(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PauseRefiner failed for {JobId}", id);
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("{id}/refiner/resume")]
    public ActionResult ResumeRefiner(string id)
    {
        RunResumeInBackground(id);
        return Ok(new { ok = true });
    }

    private async void RunResumeInBackground(string jobId)
    {
        try
        {
            await _refinerOrchestration.ResumeRefinerAsync(jobId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResumeRefiner failed for job {JobId}", jobId);
        }
    }

    [HttpPost("{id}/refiner/skip")]
    public async Task<ActionResult> SkipRefiner(string id, CancellationToken ct = default)
    {
        try
        {
            await _refinerOrchestration.SkipRefinerAsync(id, ct);
            return Ok(new { ok = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SkipRefiner failed for {JobId}", id);
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>Load persisted Refiner Threads batch files from <c>refiner_threads/</c> under the job directory.</summary>
    [HttpGet("{id}/refiner-threads")]
    public async Task<ActionResult> GetRefinerThreads(string id, CancellationToken ct = default)
    {
        var job = await _store.GetAsync(id, ct);
        var jobDir = _workspace.GetJobDirectoryPath(id);
        if (job == null && !Directory.Exists(jobDir))
            return NotFound();
        var threadsDir = Path.Combine(jobDir, "refiner_threads");
        if (!Directory.Exists(threadsDir))
            return Ok(new { batches = Array.Empty<object>() });

        var files = Directory.GetFiles(threadsDir, "batch_*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        var batches = new List<object>();
        foreach (var f in files)
        {
            try
            {
                var text = await System.IO.File.ReadAllTextAsync(f, ct);
                batches.Add(JsonSerializer.Deserialize<JsonElement>(text));
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Skip invalid refiner thread file {Path}", f);
            }
        }
        return Ok(new { batches });
    }

    private async Task PublishEnrichedSnapshotAsync(string jobId, CancellationToken ct)
    {
        var jobSnap = await _store.GetAsync(jobId, ct);
        if (jobSnap == null) return;
        jobSnap.JobDirectoryPath ??= _workspace.GetJobDirectoryPath(jobId);
        await MergeAgent04LiveIntoSnapshotAsync(jobSnap, ct).ConfigureAwait(false);
        var snapshotJson = JsonSerializer.Serialize(new { type = "snapshot", payload = jobSnap }, ApiJson.CamelCase);
        _broadcaster.Publish(jobId, snapshotJson);
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

            if (string.Equals(live.State, "Running", StringComparison.OrdinalIgnoreCase)
                || string.Equals(live.State, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                snap.TranscriptionProgressPercent = live.ProgressPercent;
                snap.TranscriptionPhaseDetail = string.IsNullOrWhiteSpace(live.CurrentPhase) ? null : live.CurrentPhase;

                snap.TranscriptionSilenceTimeline = live.SilenceTimeline == null
                    ? null
                    : new TranscriptionSilenceTimelineSnapshotDto
                    {
                        SourceDurationSec = live.SilenceTimeline.SourceDurationSec,
                        Regions = live.SilenceTimeline.Regions
                            .Select(r => new TranscriptionSilenceRegionDto
                            {
                                StartSec = r.StartSec,
                                EndSec = r.EndSec
                            })
                            .ToList()
                    };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MergeAgent04LiveIntoSnapshotAsync failed for Xtract job (Agent04 {AgentId})", agentId);
        }
    }

    private void ScheduleTranscribeSubFollowUpSnapshots(string jobId)
    {
        foreach (var ms in new[] { 600, 2000, 6000, 15000, 30000, 60000, 120000 })
        {
            var delayMs = ms;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(delayMs).ConfigureAwait(false);
                    await PublishEnrichedSnapshotAsync(jobId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Follow-up snapshot after transcribe_sub for job {JobId}", jobId);
                }
            });
        }
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
        var totalChunks = job?.Chunks?.Total ?? 0;
        var remote = await _transcription
            .GetProjectFilesAsync(scopeJobId, id, totalChunks, ct)
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
            return BadRequest(new { error = "unknown action", allowed = new[] { "cancel", "retranscribe", "split", "transcribe_sub", "rebuild_combined", "rebuild_split_merged", "write_chunk_md" } });

        var allowAfterDone = action == TranscriptionChunkAction.Split
            || action == TranscriptionChunkAction.TranscribeSub
            || action == TranscriptionChunkAction.Retranscribe
            || action == TranscriptionChunkAction.RebuildCombined
            || action == TranscriptionChunkAction.RebuildSplitMerged
            || action == TranscriptionChunkAction.WriteChunkMd;
        if (action == TranscriptionChunkAction.Cancel)
        {
            var st = job.Status ?? "";
            var running = string.Equals(st, "running", StringComparison.OrdinalIgnoreCase);
            var doneLike = string.Equals(st, "done", StringComparison.OrdinalIgnoreCase)
                || string.Equals(st, "completed", StringComparison.OrdinalIgnoreCase);
            if (!running && !doneLike)
                return Conflict(new { error = "cancel requires status running, done, or completed" });
            if (running && !string.Equals(job.Phase, "transcriber", StringComparison.OrdinalIgnoreCase))
                return Conflict(new { error = "this action requires phase=transcriber and status=running" });
        }
        else if (!allowAfterDone && (job.Phase != "transcriber" || job.Status != "running"))
            return Conflict(new { error = "this action requires phase=transcriber and status=running" });
        if (allowAfterDone
            && !string.Equals(job.Status, "running", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(job.Status, "done", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = "split / transcribe_sub / retranscribe / rebuild_combined / rebuild_split_merged / write_chunk_md require status running, done, or completed" });

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
                    || string.Equals(result.Message, "rebuild_split_merged_ok", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(result.Message, "write_chunk_md_started", StringComparison.OrdinalIgnoreCase))
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
            "write_chunk_md" => TranscriptionChunkAction.WriteChunkMd,
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

    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(string id, CancellationToken ct = default)
    {
        var removed = await _store.DeleteAsync(id, ct);
        if (!removed)
            return NotFound();
        return NoContent();
    }

    /// <summary>SSE stream: snapshot then status/log/chunk/done events.</summary>
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
        var snapshotJson = JsonSerializer.Serialize(new { type = "snapshot", payload = job }, ApiJson.CamelCase);
        await Response.WriteAsync($"data: {snapshotJson}\n\n", ct);
        await Response.Body.FlushAsync(ct);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Send(string payload)
        {
            try
            {
                Response.WriteAsync($"data: {payload}\n\n", ct).GetAwaiter().GetResult();
                Response.Body.FlushAsync(ct).GetAwaiter().GetResult();
                if (payload.Contains("\"type\":\"done\"", StringComparison.Ordinal))
                    done.TrySetResult();
            }
            catch { done.TrySetResult(); }
        }
        _broadcaster.Subscribe(id, Send);
        try
        {
            await Task.WhenAny(done.Task, Task.Delay(Timeout.Infinite, ct));
        }
        catch (OperationCanceledException) { }
        finally
        {
            _broadcaster.Unsubscribe(id, Send);
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
