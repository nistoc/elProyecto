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

    public TranscriptionController(ITranscriptionPipeline pipeline, IJobStatusStore store)
    {
        _pipeline = pipeline;
        _store = store;
    }

    /// <summary>Submit a transcription job. Returns 202 with jobId.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SubmitJob([FromBody] SubmitJobRequest request, CancellationToken cancellationToken)
    {
        var configPath = request?.ConfigPath ?? "config/default.json";
        if (!System.IO.File.Exists(configPath))
            return BadRequest(new { error = "Config file not found", configPath });

        var config = await TranscriptionConfig.FromFileAsync(configPath, cancellationToken);
        var files = config.GetFiles();
        var rawPath = !string.IsNullOrEmpty(request?.InputFilePath)
            ? request.InputFilePath
            : (files.Count > 0 ? files[0] : null);
        if (string.IsNullOrEmpty(rawPath))
            return BadRequest(new { error = "Input file not specified (inputFilePath or config.file/files)" });
        var inputPath = Path.IsPathRooted(rawPath)
            ? rawPath
            : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath)) ?? ".", rawPath);
        if (!System.IO.File.Exists(inputPath))
            return BadRequest(new { error = "Input file not found", inputFilePath = inputPath });

        var jobId = _store.Create(request?.Tags);
        _ = RunJobAsync(jobId, config, inputPath, cancellationToken);
        return AcceptedAtAction(nameof(GetJob), new { id = jobId }, new { jobId });
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(JobStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetJob(string id)
    {
        var job = _store.Get(id);
        if (job == null) return NotFound();
        return Ok(job);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<JobStatus>), StatusCodes.Status200OK)]
    public IActionResult ListJobs([FromQuery] JobState? status, [FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        var list = _store.List(new JobListFilter { Status = status, Limit = limit, Offset = offset });
        return Ok(list);
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

    private async Task RunJobAsync(string jobId, TranscriptionConfig config, string inputPath, CancellationToken cancellationToken)
    {
        try
        {
            await _pipeline.ProcessFileAsync(config, inputPath, jobId, _store, cancellationToken);
        }
        catch (Exception ex)
        {
            _store.Update(jobId, new JobStatusUpdate { State = JobState.Failed, ErrorMessage = ex.Message });
        }
    }
}
