using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;

namespace XtractManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly IJobStore _store;
    private readonly IJobWorkspace _workspace;
    private readonly IPipeline _pipeline;
    private readonly IBroadcaster _broadcaster;
    private readonly ILogger<JobsController> _logger;

    public JobsController(IJobStore store, IJobWorkspace workspace, IPipeline pipeline, IBroadcaster broadcaster, ILogger<JobsController> logger)
    {
        _store = store;
        _workspace = workspace;
        _pipeline = pipeline;
        _broadcaster = broadcaster;
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

    [HttpGet("{id}")]
    public async Task<ActionResult<JobSnapshot>> Get(string id, CancellationToken ct = default)
    {
        var job = await _store.GetAsync(id, ct);
        if (job == null)
            return NotFound();
        job.JobDirectoryPath ??= _workspace.GetJobDirectoryPath(id);
        if (job.Files == null && !string.IsNullOrEmpty(job.JobDirectoryPath) && Directory.Exists(job.JobDirectoryPath))
            job.Files = JobDirectoryFileScanner.Scan(job.JobDirectoryPath);
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
        if (job.Files == null && !string.IsNullOrEmpty(job.JobDirectoryPath) && Directory.Exists(job.JobDirectoryPath))
            job.Files = JobDirectoryFileScanner.Scan(job.JobDirectoryPath);
        _logger.LogInformation("Stream({Id}): sending snapshot, JobDirectoryPath={Path}, Chunks={Chunks}, MdOutputPath={Md}",
            id, job.JobDirectoryPath, job.Chunks != null ? "set" : "null", job.MdOutputPath ?? "null");
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        await Response.StartAsync(ct);
        var snapshotJson = JsonSerializer.Serialize(new { type = "snapshot", payload = job });
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
