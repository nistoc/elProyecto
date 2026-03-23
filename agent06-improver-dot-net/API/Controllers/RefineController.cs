using System.Text;
using Microsoft.AspNetCore.Mvc;
using TranslationImprover.Application;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;
using TranslationImprover.Features.Refine.Infrastructure;
using TranslationImprover.Features.RefineJobQuery.Application;

namespace TranslationImprover.Controllers;

[ApiController]
[Route("api/refine/jobs")]
public class RefineController : ControllerBase
{
    private readonly IRefineJobStore _store;
    private readonly IRefinePipeline _pipeline;
    private readonly IRefineJobCancellation _cancellation;
    private readonly IRefineJobQueryService _queryService;
    private readonly INodeQuery? _nodeQuery;
    private readonly WorkspaceRoot _workspaceRoot;

    public RefineController(
        IRefineJobStore store,
        IRefinePipeline pipeline,
        IRefineJobCancellation cancellation,
        IRefineJobQueryService queryService,
        WorkspaceRoot workspaceRoot,
        INodeQuery? nodeQuery = null)
    {
        _store = store;
        _pipeline = pipeline;
        _cancellation = cancellation;
        _queryService = queryService;
        _workspaceRoot = workspaceRoot;
        _nodeQuery = nodeQuery;
    }

    /// <summary>Submit a refine job. Returns 202 Accepted with Location header. input_file_path or input_content required; paths relative to workspace_root. Optional: X-Caller-Id header; callback_url in body.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult Submit(
        [FromBody] RefineJobRequest request,
        [FromHeader(Name = "X-Caller-Id")] string? xCallerId,
        CancellationToken cancellationToken)
    {
        var root = _workspaceRoot.RootPath;
        string artifactRoot;
        try
        {
            artifactRoot = RefineWorkspacePaths.ResolveEffectiveArtifactRoot(root, request?.JobDirectoryRelative);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ProblemDetailsFor(400, "Bad Request", ex.Message));
        }

        if (string.IsNullOrEmpty(request?.InputFilePath) && string.IsNullOrEmpty(request?.InputContent))
            return BadRequest(ProblemDetailsFor(400, "Bad Request", "Either input_file_path or input_content must be set"));

        if (!string.IsNullOrEmpty(request.InputFilePath))
        {
            var raw = request.InputFilePath.Trim();
            if (Path.IsPathRooted(raw))
                return BadRequest(ProblemDetailsFor(400, "Bad Request", "input_file_path must be relative to workspace_root; absolute paths are not allowed", new Dictionary<string, object?> { ["inputFilePath"] = raw }));
            var rel = raw.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var full = Path.Combine(artifactRoot, rel);
            if (!System.IO.File.Exists(full))
                return BadRequest(ProblemDetailsFor(400, "Bad Request", "Input file not found", new Dictionary<string, object?> { ["inputFilePath"] = rel }));
        }

        if (!string.IsNullOrEmpty(request.OutputFilePath))
        {
            var raw = request.OutputFilePath.Trim();
            if (Path.IsPathRooted(raw))
                return BadRequest(ProblemDetailsFor(400, "Bad Request", "output_file_path must be relative to workspace_root", new Dictionary<string, object?> { ["outputFilePath"] = raw }));
        }

        var tags = request.Tags?.Count > 0 ? request.Tags.ToList() : null;
        var callbackUrl = !string.IsNullOrWhiteSpace(request.CallbackUrl) ? request.CallbackUrl.Trim() : null;
        var jobId = _store.Create(tags, callbackUrl);

        var req = new RefineJobRequest
        {
            InputFilePath = string.IsNullOrEmpty(request.InputFilePath) ? null : request.InputFilePath.Trim(),
            InputContent = request.InputContent,
            OutputFilePath = string.IsNullOrEmpty(request.OutputFilePath) ? null : request.OutputFilePath.Trim(),
            BatchSize = request.BatchSize > 0 ? request.BatchSize : 10,
            ContextLines = request.ContextLines >= 0 ? request.ContextLines : 3,
            Model = request.Model ?? "gpt-4o-mini",
            Temperature = request.Temperature,
            PromptFile = string.IsNullOrEmpty(request.PromptFile) ? null : request.PromptFile.Trim(),
            OpenAIBaseUrl = string.IsNullOrEmpty(request.OpenAIBaseUrl) ? null : request.OpenAIBaseUrl.Trim(),
            OpenAIOrganization = string.IsNullOrEmpty(request.OpenAIOrganization) ? null : request.OpenAIOrganization.Trim(),
            SaveIntermediate = request.SaveIntermediate,
            IntermediateDir = string.IsNullOrEmpty(request.IntermediateDir) ? null : request.IntermediateDir.Trim(),
            CallbackUrl = callbackUrl,
            Tags = request.Tags,
            JobDirectoryRelative = string.IsNullOrWhiteSpace(request.JobDirectoryRelative) ? null : request.JobDirectoryRelative.Trim()
        };

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cancellation.Register(jobId, cts);
        _ = _pipeline.RunAsync(jobId, req, root, artifactRoot, cts.Token);

        return AcceptedAtAction(nameof(Get), new { id = jobId }, new { jobId });
    }

    /// <summary>Get job status (state, progress, output_file_path when completed, error_message when failed).</summary>
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(RefineJobStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult Get(string id)
    {
        var job = _store.Get(id);
        if (job == null)
            return NotFound(ProblemDetailsFor(404, "Not Found", "Job not found", new Dictionary<string, object?> { ["jobId"] = id }));
        return Ok(job);
    }

    /// <summary>SSE stream of job progress until completed, failed, or cancelled.</summary>
    [HttpGet("{id}/stream")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task Stream(string id, CancellationToken cancellationToken)
    {
        var job = _store.Get(id);
        if (job == null)
        {
            Response.StatusCode = 404;
            await Response.WriteAsync("Job not found", cancellationToken);
            return;
        }
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        await Response.StartAsync(cancellationToken);
        var delay = TimeSpan.FromMilliseconds(500);
        while (!cancellationToken.IsCancellationRequested)
        {
            job = _store.Get(id);
            if (job == null) break;
            var state = job.State.ToString().ToLowerInvariant();
            await Response.WriteAsync($"event: progress\n", cancellationToken);
            await Response.WriteAsync($"data: {{\"state\":\"{state}\",\"progress_percent\":{job.ProgressPercent},\"current_batch\":{job.CurrentBatch},\"total_batches\":{job.TotalBatches},\"current_phase\":\"{EscapeJson(job.CurrentPhase)}\",\"error_message\":\"{EscapeJson(job.ErrorMessage)}\"}}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            if (job.State is RefineJobState.Completed or RefineJobState.Failed or RefineJobState.Cancelled)
            {
                await Response.WriteAsync($"event: {(job.State == RefineJobState.Completed ? "completed" : job.State == RefineJobState.Failed ? "failed" : "cancelled")}\n", cancellationToken);
                await Response.WriteAsync($"data: {{\"state\":\"{state}\"}}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                break;
            }
            await Task.Delay(delay, cancellationToken);
        }
    }

    /// <summary>Get result content when job is Completed. Returns file content when output_file_path was set.</summary>
    [HttpGet("{id}/result")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetResult(string id, CancellationToken cancellationToken)
    {
        var job = _store.Get(id);
        if (job == null)
            return NotFound(ProblemDetailsFor(404, "Not Found", "Job not found", new Dictionary<string, object?> { ["jobId"] = id }));
        if (job.State != RefineJobState.Completed)
            return BadRequest(ProblemDetailsFor(400, "Bad Request", "Job is not completed", new Dictionary<string, object?> { ["jobId"] = id, ["state"] = job.State.ToString() }));
        if (string.IsNullOrEmpty(job.OutputFilePath))
            return NotFound(ProblemDetailsFor(404, "Not Found", "No output file path for this job", new Dictionary<string, object?> { ["jobId"] = id }));
        var fullPath = Path.Combine(_workspaceRoot.RootPath, job.OutputFilePath.Trim().TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!System.IO.File.Exists(fullPath))
            return NotFound(ProblemDetailsFor(404, "Not Found", "Output file not found", new Dictionary<string, object?> { ["jobId"] = id }));
        var content = await System.IO.File.ReadAllTextAsync(fullPath, Encoding.UTF8, cancellationToken);
        return Content(content, "text/plain; charset=utf-8");
    }

    /// <summary>Cancel a running or pending job. Sets state to Cancelled.</summary>
    [HttpPost("{id}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult Cancel(string id)
    {
        var job = _store.Get(id);
        if (job == null)
            return NotFound(ProblemDetailsFor(404, "Not Found", "Job not found", new Dictionary<string, object?> { ["jobId"] = id }));
        if (job.State != RefineJobState.Running && job.State != RefineJobState.Pending)
            return BadRequest(ProblemDetailsFor(400, "Bad Request", "Job cannot be cancelled in current state", new Dictionary<string, object?> { ["jobId"] = id, ["state"] = job.State.ToString() }));
        var cancelled = _cancellation.TryCancel(id);
        if (cancelled)
            _store.Update(id, new RefineJobStatusUpdate { State = RefineJobState.Cancelled, ErrorMessage = "Cancelled" });
        return Ok(new { cancelled });
    }

    /// <summary>Query jobs by semantic key and filters — part of RENTGEN virtual model.</summary>
    /// <remarks>Filter by semanticKey (one of the job's Tags from Submit), status, time range (from/to), with limit/offset.</remarks>
    [HttpGet("query")]
    [Tags("Virtual model (RENTGEN)")]
    [ProducesResponseType(typeof(IReadOnlyList<RefineJobStatus>), StatusCodes.Status200OK)]
    public IActionResult Query(
        [FromQuery] string? semanticKey,
        [FromQuery] RefineJobState? status,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0)
    {
        var filter = new RefineJobListFilter { SemanticKey = semanticKey, Status = status, From = from, To = to, Limit = limit, Offset = offset };
        var list = !string.IsNullOrEmpty(semanticKey)
            ? _queryService.QueryBySemanticKey(semanticKey, status, from, to, limit, offset)
            : _queryService.Query(filter);
        return Ok(list);
    }

    /// <summary>Get virtual model (RENTGEN) nodes for a job. Optional tag = node id to return only that node.</summary>
    /// <remarks>Part of the RENTGEN virtual model. Use tree=true for nested structure; otherwise flat list. Optional tag = node id. scopeId = job id.</remarks>
    [HttpGet("{id}/nodes")]
    [Tags("Virtual model (RENTGEN)")]
    [ProducesResponseType(typeof(IReadOnlyList<NodeInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult GetNodes(string id, [FromQuery] bool tree = false, [FromQuery] string? tag = null)
    {
        if (_store.Get(id) == null)
            return NotFound(ProblemDetailsFor(404, "Not Found", "Job not found", new Dictionary<string, object?> { ["jobId"] = id }));
        if (_nodeQuery == null)
            return Ok(Array.Empty<NodeInfo>());
        if (!string.IsNullOrEmpty(tag))
        {
            var node = _nodeQuery.GetNodeByScopeAndId(id, tag);
            if (node == null)
                return NotFound(ProblemDetailsFor(404, "Not Found", "Node not found", new Dictionary<string, object?> { ["jobId"] = id, ["tag"] = tag }));
            return Ok(new[] { node });
        }
        var nodes = tree ? _nodeQuery.GetTreeByScope(id) : _nodeQuery.GetByScope(id);
        return Ok(nodes);
    }

    private static string EscapeJson(string? s) => s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

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
}
