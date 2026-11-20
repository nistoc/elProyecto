using Microsoft.AspNetCore.Mvc;
using XtractManager.Features.LessonNotes.Application;
using XtractManager.Features.LessonNotes.Infrastructure;
using XtractManager.Features.Sessions.Application;

namespace XtractManager.Controllers;

/// <summary>
/// The notes of a lesson from the vault copy in its day folder (<c>&lt;day folder&gt;/obsidian/</c>): the homework an
/// earlier parse made of the lesson, a summary, cards. Only read — the recordings folder is the owner's.
/// </summary>
[ApiController]
public class LessonNotesController : ControllerBase
{
    /// <summary>A note longer than this is not sent (the longest note of the vault is under 100 KB).</summary>
    public const long MaxNoteBytes = 4 * 1024 * 1024;

    private readonly ILessonNotesReader _notes;
    private readonly IRecordingsCatalog _catalog;

    public LessonNotesController(ILessonNotesReader notes, IRecordingsCatalog catalog)
    {
        _notes = notes;
        _catalog = catalog;
    }

    [HttpGet("api/sessions/{date}/notes")]
    public ActionResult<LessonNotesResponse> List(string date)
    {
        if (!LessonPageRules.TryDate(date, out var day))
            return BadRequest(ProblemOf(400, "The date of the lesson must be written YYYY-MM-DD."));
        if (!_catalog.Enabled)
            return NotFound(ProblemOf(404, "The recordings folder is not configured (Recordings:RootPath)."));
        return Ok(new LessonNotesResponse(day, _notes.List(day)));
    }

    /// <summary>The text of one note (markdown), by its path from the lessons list of notes.</summary>
    [HttpGet("api/sessions/{date}/notes/text")]
    public ActionResult Text(string date, [FromQuery] string? path)
    {
        if (!LessonPageRules.TryDate(date, out var day))
            return BadRequest(ProblemOf(400, "The date of the lesson must be written YYYY-MM-DD."));
        var file = _notes.NoteFile(day, path);
        if (file == null)
            return NotFound(ProblemOf(404, $"The lesson of {day} has no note '{path}'."));
        if (new FileInfo(file).Length > MaxNoteBytes)
            return StatusCode(413, ProblemOf(413, $"The note is longer than {MaxNoteBytes / (1024 * 1024)} MB."));
        return Content(System.IO.File.ReadAllText(file), "text/markdown; charset=utf-8");
    }

    /// <summary>A picture or a sound a note of the lesson shows, by its file name; sounds are streamed with HTTP range.</summary>
    [HttpGet("api/sessions/{date}/notes/file")]
    public ActionResult EmbeddedFile(string date, [FromQuery] string? name)
    {
        if (!LessonPageRules.TryDate(date, out var day))
            return BadRequest(ProblemOf(400, "The date of the lesson must be written YYYY-MM-DD."));
        var file = _notes.EmbeddedFile(day, name);
        if (file == null)
            return NotFound(ProblemOf(404, $"The notes of the lesson of {day} have no picture or sound '{name}'."));
        return PhysicalFile(file, LessonNotesReader.EmbeddedTypes[Path.GetExtension(file)], enableRangeProcessing: true);
    }

    private static ProblemDetails ProblemOf(int status, string detail) => new()
    {
        Title = status switch { 400 => "Bad Request", 404 => "Not Found", 413 => "Payload Too Large", _ => "Error" },
        Status = status,
        Detail = detail,
    };
}
