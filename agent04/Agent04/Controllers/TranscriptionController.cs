using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Agent04.Controllers;

[ApiController]
[Route("api/transcription/jobs")]
public class TranscriptionController : ControllerBase
{
    private readonly ITranscriptionPipeline _pipeline;
    private readonly IJobStatusStore _store;
    private readonly INodeModel? _nodeModel;
    private readonly INodeQuery? _nodeQuery;

    public TranscriptionController(ITranscriptionPipeline pipeline, IJobStatusStore store, INodeModel? nodeModel = null, INodeQuery? nodeQuery = null)
    {
        _pipeline = pipeline;
        _store = store;
        _nodeModel = nodeModel;
        _nodeQuery = nodeQuery;
    }

    /// <summary>Submit a transcription job. Returns 202 with jobId.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitJob([FromBody] SubmitJobRequest request, CancellationToken cancellationToken)
    {
        var configPath = request?.ConfigPath ?? "config/default.json";
        if (!System.IO.File.Exists(configPath))
            return BadRequest(ProblemDetailsFor(400, "Bad Request", "Config file not found", extensions: new Dictionary<string, object?> { ["configPath"] = configPath }));

        var config = await TranscriptionConfig.FromFileAsync(configPath, cancellationToken);
        var files = config.GetFiles();
        var rawPath = !string.IsNullOrEmpty(request?.InputFilePath)
            ? request.InputFilePath
            : (files.Count > 0 ? files[0] : null);
        if (string.IsNullOrEmpty(rawPath))
            return BadRequest(ProblemDetailsFor(400, "Bad Request", "Input file not specified (inputFilePath or config.file/files)"));
        var inputPath = Path.IsPathRooted(rawPath)
            ? rawPath
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".", rawPath);
        if (!System.IO.File.Exists(inputPath))
            return BadRequest(ProblemDetailsFor(400, "Bad Request", "Input file not found", extensions: new Dictionary<string, object?> { ["inputFilePath"] = inputPath }));

        var jobId = _store.Create(request?.Tags);
        _ = RunJobAsync(jobId, config, inputPath, cancellationToken);
        return AcceptedAtAction(nameof(GetJob), new { id = jobId }, new { jobId });
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(JobStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult GetJob(string id)
    {
        var job = _store.Get(id);
        if (job == null)
            return NotFound(ProblemDetailsFor(404, "Not Found", "Job not found", extensions: new Dictionary<string, object?> { ["jobId"] = id }));
        return Ok(job);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<JobStatus>), StatusCodes.Status200OK)]
    public IActionResult ListJobs([FromQuery] JobState? status, [FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        var list = _store.List(new JobListFilter { Status = status, Limit = limit, Offset = offset });
        return Ok(list);
    }

    /// <summary>Get virtual model nodes for a job (flat list or tree).</summary>
    [HttpGet("{id}/nodes")]
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

    [HttpGet("query")]
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
            await _pipeline.ProcessFileAsync(config, inputPath, jobId, _store, _nodeModel, cancellationToken);
        }
        catch (Exception ex)
        {
            _store.Update(jobId, new JobStatusUpdate { State = JobState.Failed, ErrorMessage = ex.Message });
        }
    }
}
