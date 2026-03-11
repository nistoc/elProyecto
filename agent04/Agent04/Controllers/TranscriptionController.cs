using Agent04.Application;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Agent04.Controllers;

[ApiController]
[Route("api/transcription/jobs")]
public class TranscriptionController : ControllerBase
{
    private readonly ITranscriptionPipeline _pipeline;
    private readonly IJobStatusStore _store;
    private readonly WorkspaceRoot _workspaceRoot;
    private readonly INodeModel? _nodeModel;
    private readonly INodeQuery? _nodeQuery;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly IOutboundJobNotifier? _outboundNotifier;

    public TranscriptionController(ITranscriptionPipeline pipeline, IJobStatusStore store, WorkspaceRoot workspaceRoot, INodeModel? nodeModel = null, INodeQuery? nodeQuery = null, IHttpClientFactory? httpClientFactory = null, IOutboundJobNotifier? outboundNotifier = null)
    {
        _pipeline = pipeline;
        _store = store;
        _workspaceRoot = workspaceRoot;
        _nodeModel = nodeModel;
        _nodeQuery = nodeQuery;
        _httpClientFactory = httpClientFactory;
        _outboundNotifier = outboundNotifier;
    }

    /// <summary>Submit a transcription job. Returns 202 with jobId and Location header. configPath and inputFilePath are relative to workspace_root. Optional: X-Caller-Id header (propagated for logging/correlation); callbackUrl in body (HTTP POST when job completes or fails).</summary>
    /// <param name="xCallerId">Optional. Caller identifier from header X-Caller-Id (for logging and correlation).</param>
    [HttpPost]
    [Tags("Jobs")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitJob(
        [FromBody] SubmitJobRequest request,
        [FromHeader(Name = "X-Caller-Id")] string? xCallerId,
        CancellationToken cancellationToken)
    {
        var root = _workspaceRoot.RootPath;
        var configPathRel = (request?.ConfigPath ?? "config/default.json").Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configPathFull = Path.Combine(root, configPathRel);
        if (!System.IO.File.Exists(configPathFull))
            return BadRequest(ProblemDetailsFor(400, "Bad Request", "Config file not found", extensions: new Dictionary<string, object?> { ["configPath"] = configPathRel }));

        var config = await TranscriptionConfig.FromFileAsync(configPathFull, cancellationToken);
        var files = config.GetFiles();
        var rawPath = !string.IsNullOrEmpty(request?.InputFilePath)
            ? request.InputFilePath!.Trim()
            : (files.Count > 0 ? files[0] : null);
        if (string.IsNullOrEmpty(rawPath))
            return BadRequest(ProblemDetailsFor(400, "Bad Request", "Input file not specified (inputFilePath or config.file/files)"));
        if (Path.IsPathRooted(rawPath))
            return BadRequest(ProblemDetailsFor(400, "Bad Request", "inputFilePath must be relative to workspace_root; absolute paths are not allowed", extensions: new Dictionary<string, object?> { ["inputFilePath"] = rawPath }));

        var inputPathRel = rawPath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var inputPathFull = Path.Combine(root, inputPathRel);
        if (!System.IO.File.Exists(inputPathFull))
            return BadRequest(ProblemDetailsFor(400, "Bad Request", "Input file not found", extensions: new Dictionary<string, object?> { ["inputFilePath"] = inputPathRel }));

        var callbackUrl = !string.IsNullOrWhiteSpace(request?.CallbackUrl) ? request.CallbackUrl!.Trim() : null;
        var jobId = _store.Create(request?.Tags, callbackUrl);
        _ = RunJobAsync(jobId, config, inputPathFull, cancellationToken);
        return AcceptedAtAction(nameof(GetJob), new { id = jobId }, new { jobId });
    }

    /// <summary>Get job status (state, progress, phase, artifact paths when completed).</summary>
    [HttpGet("{id}")]
    [Tags("Jobs")]
    [ProducesResponseType(typeof(JobStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult GetJob(string id)
    {
        var job = _store.Get(id);
        if (job == null)
            return NotFound(ProblemDetailsFor(404, "Not Found", "Job not found", extensions: new Dictionary<string, object?> { ["jobId"] = id }));
        return Ok(job);
    }

    /// <summary>List jobs with optional status filter and pagination.</summary>
    [HttpGet]
    [Tags("Jobs")]
    [ProducesResponseType(typeof(IReadOnlyList<JobStatus>), StatusCodes.Status200OK)]
    public IActionResult ListJobs([FromQuery] JobState? status, [FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        var list = _store.List(new JobListFilter { Status = status, Limit = limit, Offset = offset });
        return Ok(list);
    }

    /// <summary>Get virtual model (RENTGEN) nodes for a job: hierarchy of steps (job → chunking → transcribe → chunk-0..N → merge).</summary>
    /// <remarks>Part of the RENTGEN virtual abstract model. Use tree=true to get nested structure with children; otherwise returns flat list with parentId. scopeId equals job id. Each node has id, parentId, scopeId, kind (job|phase|chunk), status, startedAt, completedAt, updatedAt. See docs/RENTGEN_IMPLEMENTATION.md.</remarks>
    [HttpGet("{id}/nodes")]
    [Tags("Virtual model (RENTGEN)")]
    [ProducesResponseType(typeof(IReadOnlyList<NodeInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult GetJobNodes(string id, [FromQuery] bool tree = false)
    {
        if (_store.Get(id) == null)
            return NotFound(ProblemDetailsFor(404, "Not Found", "Job not found", extensions: new Dictionary<string, object?> { ["jobId"] = id }));
        if (_nodeQuery == null)
            return Ok(Array.Empty<NodeInfo>());
        var nodes = tree ? _nodeQuery.GetTreeByScope(id) : _nodeQuery.GetByScope(id);
        return Ok(nodes);
    }

    /// <summary>Query jobs by semantic key (tag) and filters — part of RENTGEN virtual model. Returns full status per job. Suitable for 0.01–10 Hz polling.</summary>
    /// <remarks>Filter by tag (from SubmitJob), status, time range (from/to), with limit/offset. Each item in the list has full job status (state, progress, phase, paths).</remarks>
    [HttpGet("query")]
    [Tags("Virtual model (RENTGEN)")]
    [ProducesResponseType(typeof(IReadOnlyList<JobStatus>), StatusCodes.Status200OK)]
    public IActionResult QueryJobs(
        [FromQuery] string? tag,
        [FromQuery] JobState? status,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        var filter = new JobListFilter { Tag = tag, Status = status, From = from, To = to, Limit = limit, Offset = offset };
        var list = _store.List(filter);
        return Ok(list);
    }

    private static ProblemDetails ProblemDetailsFor(int status, string title, string detail, Dictionary<string, object?>? extensions = null)
    {
        var p = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = status switch { 400 => "https://tools.ietf.org/html/rfc9110#section-15.5.1", 404 => "https://tools.ietf.org/html/rfc9110#section-15.5.5", _ => null }
        };
        if (extensions != null)
            foreach (var kv in extensions)
                p.Extensions[kv.Key] = kv.Value;
        return p;
    }

    private async Task RunJobAsync(string jobId, TranscriptionConfig config, string inputPath, CancellationToken cancellationToken)
    {
        try
        {
            await _pipeline.ProcessFileAsync(config, inputPath, _workspaceRoot.RootPath, jobId, _store, _nodeModel, cancellationToken);
        }
        catch (Exception ex)
        {
            _store.Update(jobId, new JobStatusUpdate { State = JobState.Failed, ErrorMessage = ex.Message });
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
            var json = System.Text.Json.JsonSerializer.Serialize(job);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            await client.PostAsync(job.CallbackUrl!, content);
        }
        catch
        {
            // Fire-and-forget; log if needed
        }
    }
}
