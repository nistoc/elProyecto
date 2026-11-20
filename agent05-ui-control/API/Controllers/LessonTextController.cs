using Microsoft.AspNetCore.Mvc;
using XtractManager.Features.LessonText.Application;
using XtractManager.Features.Sessions.Application;

namespace XtractManager.Controllers;

/// <summary>
/// The text of a lesson in one answer, every line with its speaker and the speaker's role, and the roles a
/// person gives the speakers. What a package of the lesson is built from.
/// </summary>
[ApiController]
public class LessonTextController : ControllerBase
{
    /// <summary>A name of a speaker longer than this is not one.</summary>
    public const int MaxSpeakerLength = 120;

    private readonly ILessonTextReader _reader;
    private readonly ILessonSpeakersStore _speakers;
    private readonly IRecordingsCatalog _catalog;
    private readonly ILogger<LessonTextController> _logger;

    public LessonTextController(
        ILessonTextReader reader, ILessonSpeakersStore speakers, IRecordingsCatalog catalog, ILogger<LessonTextController> logger)
    {
        _reader = reader;
        _speakers = speakers;
        _catalog = catalog;
        _logger = logger;
    }

    [HttpGet("api/sessions/{date}/text")]
    public async Task<ActionResult<LessonTextResponse>> Get(string date, CancellationToken ct = default)
    {
        if (!LessonPageRules.TryDate(date, out var day))
            return BadRequest(ProblemOf(400, "The date of the lesson must be written YYYY-MM-DD."));
        if (!_catalog.Enabled)
            return NotFound(ProblemOf(404, "The recordings folder is not configured (Recordings:RootPath)."));
        try
        {
            var text = await _reader.ReadAsync(day, ct);
            if (text == null)
                return NotFound(ProblemOf(404, $"The recordings folder has no lesson of {day}."));
            return Ok(LessonTextResponse.Of(text));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ProblemOf(409, ex.Message));
        }
    }

    /// <summary>
    /// Body: <c>{ "recording": "&lt;id&gt;" | "*", "speaker": "...", "role": "teacher" | "student" | "other" | null }</c>.
    /// <c>*</c> gives the role in every recording of the lesson (a participant's name); null takes the given role away.
    /// </summary>
    [HttpPut("api/sessions/{date}/text/speakers")]
    public async Task<ActionResult<LessonTextResponse>> SetRole(string date, [FromBody] LessonSpeakerRoleBody? body, CancellationToken ct = default)
    {
        if (!LessonPageRules.TryDate(date, out var day))
            return BadRequest(ProblemOf(400, "The date of the lesson must be written YYYY-MM-DD."));
        var recording = body?.Recording?.Trim() ?? "";
        var speaker = body?.Speaker?.Trim() ?? "";
        var role = string.IsNullOrWhiteSpace(body?.Role) ? null : body!.Role!.Trim();
        if (recording.Length == 0 || speaker.Length == 0 || speaker.Length > MaxSpeakerLength)
            return BadRequest(ProblemOf(400, $"recording and speaker are required (a speaker up to {MaxSpeakerLength} characters)."));
        if (role != null && !LessonRole.IsRole(role))
            return BadRequest(ProblemOf(400, $"role is one of {LessonRole.Teacher}, {LessonRole.Student}, {LessonRole.Other}, or null."));
        if (!_catalog.Enabled)
            return NotFound(ProblemOf(404, "The recordings folder is not configured (Recordings:RootPath)."));

        try
        {
            var before = await _reader.ReadAsync(day, ct);
            if (before == null)
                return NotFound(ProblemOf(404, $"The recordings folder has no lesson of {day}."));
            if (recording != ILessonSpeakersStore.AllRecordings && before.Recordings.All(r => r.Id != recording))
                return NotFound(ProblemOf(404, $"The lesson of {day} has no recording '{recording}' with text."));

            _speakers.SetRole(day, recording, speaker, role);
            var after = await _reader.ReadAsync(day, ct);
            return Ok(LessonTextResponse.Of(after!));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ProblemOf(409, ex.Message));
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "The role of a speaker of the lesson of {Date} could not be written", day);
            return StatusCode(500, ProblemOf(500, ex.Message));
        }
    }

    private static ProblemDetails ProblemOf(int status, string detail) => new()
    {
        Title = status switch { 400 => "Bad Request", 404 => "Not Found", 409 => "Conflict", 500 => "Internal Server Error", _ => "Error" },
        Status = status,
        Detail = detail,
    };
}

public sealed class LessonSpeakerRoleBody
{
    public string? Recording { get; set; }

    public string? Speaker { get; set; }

    public string? Role { get; set; }
}

/// <param name="Lines">All lines of the lesson.</param>
/// <param name="LinesWithSpeaker">Lines whose speaker is named.</param>
/// <param name="LinesWithRole">Lines whose speaker has a role, given or guessed.</param>
public sealed record LessonTextResponse(
    string Date,
    int Lines,
    int LinesWithSpeaker,
    int LinesWithRole,
    IReadOnlyList<LessonTextRecording> Recordings)
{
    public static LessonTextResponse Of(LessonTextDocument text) =>
        new(text.Date, text.Lines, text.LinesWithSpeaker, text.LinesWithRole, text.Recordings);
}
