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
    private readonly IBroadcaster _broadcaster;
    private readonly ITranscriptionServiceClient _transcription;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        IJobStore store,
        IJobWorkspace workspace,
        IPipeline pipeline,
        IBroadcaster broadcaster,
        ITranscriptionServiceClient transcription,
        ILogger<JobsController> logger)
    {
        _store = store;
        _workspace = workspace;
        _pipeline = pipeline;
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

    private async Task PublishEnrichedSnapshotAsync(string jobId, CancellationToken ct)
    {
        var jobSnap = await _store.GetAsync(jobId, ct);
        if (jobSnap == null) return;
        jobSnap.JobDirectoryPath ??= _workspace.GetJobDirectoryPath(jobId);
        var snapshotJson = JsonSerializer.Serialize(new { type = "snapshot", payload = jobSnap }, ApiJson.CamelCase);
        _broadcaster.Publish(jobId, snapshotJson);
    }

    private void ScheduleTranscribeSubFollowUpSnapshots(string jobId)
    {
        foreach (var ms in new[] { 600, 2000, 6000, 20000, 60000 })
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

        var files = JobProjectFilesScanner.Scan(jobDir);
        _logger.LogDebug("GetProjectFiles({Id}): jobDir={Path}, original={O}, chunks={C}", id, jobDir, files.Original.Count, files.Chunks.Count);
        return Ok(new { files, jobDir });
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

    /// <summary>Forward chunk operator action to Agent04 (cancel implemented; skip/retranscribe/split may return not_implemented).</summary>
    [HttpPost("{id}/chunk-actions")]
    public async Task<ActionResult<ChunkActionResponse>> PostChunkAction(
        string id,
        [FromBody] ChunkActionRequest? body,
        CancellationToken ct = default)
    {
        if (body == null || string.IsNullOrWhiteSpace(body.Action))
            return BadRequest(new { error = "action is required (cancel | skip | retranscribe | split | transcribe_sub)" });
        if (body.ChunkIndex < 0)
            return BadRequest(new { error = "chunkIndex must be >= 0" });

        var job = await _store.GetAsync(id, ct);
        if (job == null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(job.Agent04JobId))
            return Conflict(new { error = "Agent04 job id not available yet; wait for transcription to start" });

        var action = ParseChunkAction(body.Action.Trim());
        if (action == null)
            return BadRequest(new { error = "unknown action", allowed = new[] { "cancel", "skip", "retranscribe", "split", "transcribe_sub" } });

        var allowAfterDone = action == TranscriptionChunkAction.Split
            || action == TranscriptionChunkAction.TranscribeSub
            || action == TranscriptionChunkAction.Retranscribe;
        if (!allowAfterDone && (job.Phase != "transcriber" || job.Status != "running"))
            return Conflict(new { error = "this action requires phase=transcriber and status=running" });
        if (allowAfterDone
            && !string.Equals(job.Status, "running", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(job.Status, "done", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(job.Status, "completed", StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = "split / transcribe_sub / retranscribe require status running, done, or completed" });

        if (action == TranscriptionChunkAction.Split && (body.SplitParts is null || body.SplitParts < 2))
            return BadRequest(new { error = "split requires splitParts >= 2" });
        if (action == TranscriptionChunkAction.TranscribeSub && (body.SubChunkIndex is null || body.SubChunkIndex < 0))
            return BadRequest(new { error = "transcribe_sub requires subChunkIndex >= 0" });
        if (action == TranscriptionChunkAction.Cancel && body.SubChunkIndex is int badSub && badSub < 0)
            return BadRequest(new { error = "subChunkIndex must be >= 0 when provided for cancel" });

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
                    || string.Equals(result.Message, "retranscribe_started", StringComparison.OrdinalIgnoreCase))
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
            "skip" => TranscriptionChunkAction.Skip,
            "retranscribe" => TranscriptionChunkAction.Retranscribe,
            "split" => TranscriptionChunkAction.Split,
            "transcribe_sub" => TranscriptionChunkAction.TranscribeSub,
            _ => null
        };

    [HttpGet("{id}")]
    public async Task<ActionResult<JobSnapshot>> Get(string id, CancellationToken ct = default)
    {
        var job = await _store.GetAsync(id, ct);
        if (job == null)
            return NotFound();
        job.JobDirectoryPath ??= _workspace.GetJobDirectoryPath(id);
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
