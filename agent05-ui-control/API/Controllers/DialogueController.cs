using Microsoft.AspNetCore.Mvc;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Controllers;

/// <summary>
/// Dialogue out of participant tracks: every track was transcribed as a job of its own, and the dialogue
/// is their lines under the participants' names in time order. It lives in a job folder of its own.
/// </summary>
[ApiController]
public class DialogueController : ControllerBase
{
    private readonly IDialogueBuilder _dialogues;
    private readonly ILogger<DialogueController> _logger;

    public DialogueController(IDialogueBuilder dialogues, ILogger<DialogueController> logger)
    {
        _dialogues = dialogues;
        _logger = logger;
    }

    /// <summary>
    /// Creates a dialogue job from the transcripts of the given track jobs. Body:
    /// <c>{ "title"?: string, "tracks": [{ "jobId": string, "speaker": string, "file"?: string }] }</c>.
    /// No model is called; the refiner is not started.
    /// </summary>
    [HttpPost("api/jobs/dialogue")]
    public async Task<IActionResult> Create([FromBody] DialogueCreateBody? body, CancellationToken ct = default)
    {
        var tracks = (body?.Tracks ?? new List<DialogueTrackBody>())
            .Select(t => new DialogueTrackRequest(t?.JobId ?? "", t?.Speaker ?? "", t?.File))
            .ToList();
        try
        {
            var result = await _dialogues.CreateAsync(new DialogueCreateRequest(body?.Title, tracks), ct);
            return Created($"/api/jobs/{result.JobId}", result);
        }
        catch (DialogueRequestException ex)
        {
            return ToError(ex);
        }
    }

    /// <summary>
    /// Merges the tracks of a dialogue job again and writes a new <c>transcript_dialogue_*.md</c>;
    /// earlier merges stay as they are.
    /// </summary>
    [HttpPost("api/jobs/{id}/dialogue/rebuild")]
    public async Task<IActionResult> Rebuild(string id, CancellationToken ct = default)
    {
        try
        {
            return Ok(await _dialogues.RebuildAsync(id, ct));
        }
        catch (DialogueRequestException ex)
        {
            return ToError(ex);
        }
    }

    private IActionResult ToError(DialogueRequestException ex)
    {
        _logger.LogInformation("Dialogue request rejected: {Message}", ex.Message);
        return ex.NotFound
            ? NotFound(new ProblemDetails { Title = "Not Found", Status = 404, Detail = ex.Message })
            : BadRequest(new ProblemDetails { Title = "Bad Request", Status = 400, Detail = ex.Message });
    }
}

public sealed class DialogueCreateBody
{
    public string? Title { get; set; }
    public List<DialogueTrackBody>? Tracks { get; set; }
}

public sealed class DialogueTrackBody
{
    public string? JobId { get; set; }
    public string? Speaker { get; set; }
    public string? File { get; set; }
}
