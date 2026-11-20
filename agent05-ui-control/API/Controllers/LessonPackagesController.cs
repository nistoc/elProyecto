using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using XtractManager.Features.LessonPackages.Application;
using XtractManager.Features.Sessions.Application;

namespace XtractManager.Controllers;

/// <summary>
/// The package of a lesson (topics, grammar, examples, words) as the UI shows it. The lists come from
/// <c>package.json</c> in the packages folder; which words the earlier lessons already had is counted on
/// every read from the transcripts in the job folders, and the owner's edits are kept next to the package.
/// </summary>
[ApiController]
public class LessonPackagesController : ControllerBase
{
    private readonly ILessonPackageStore _packages;
    private readonly ILessonWordIndex _words;
    private readonly IRecordingsCatalog _catalog;
    private readonly ILogger<LessonPackagesController> _logger;

    public LessonPackagesController(
        ILessonPackageStore packages,
        ILessonWordIndex words,
        IRecordingsCatalog catalog,
        ILogger<LessonPackagesController> logger)
    {
        _packages = packages;
        _words = words;
        _catalog = catalog;
        _logger = logger;
    }

    /// <summary>The package of the lesson of this date with the edits put on and the earlier lessons counted.</summary>
    [HttpGet("api/sessions/{date}/package")]
    public async Task<ActionResult<LessonPackageView>> Get(string date, CancellationToken ct = default)
    {
        if (!LessonPageRules.TryDate(date, out var day))
            return BadRequest(ProblemOf(400, "The date of the lesson must be written YYYY-MM-DD."));
        try
        {
            var view = await ViewAsync(day, ct);
            return view == null ? NotFound(ProblemOf(404, $"The lesson of {day} has no package.")) : Ok(view);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ProblemOf(409, ex.Message));
        }
    }

    /// <summary>
    /// Writes fields of one item anew, or takes the item out of the lists. Body:
    /// <c>{ "fields"?: { name: text | null }, "leftOut"?: bool }</c> — a field given as null (or the very
    /// text the builder wrote) goes back to the builder's text, a field not named stays as it is. The
    /// package file is not changed: the edit is kept next to it. Answers with the package as it reads now.
    /// </summary>
    [HttpPut("api/sessions/{date}/package/marks/{id}")]
    public async Task<ActionResult<LessonPackageView>> PutMark(
        string date, string id, [FromBody] LessonMarkBody? body, CancellationToken ct = default)
    {
        if (!LessonPageRules.TryDate(date, out var day))
            return BadRequest(ProblemOf(400, "The date of the lesson must be written YYYY-MM-DD."));
        if (!LessonPackageViewBuilder.TryMarkId(id, out var itemId))
            return BadRequest(ProblemOf(400, "The id of the item is small Latin letters, digits and dashes."));
        if (body == null || (body.Fields == null && body.LeftOut == null))
            return BadRequest(ProblemOf(400, "fields or leftOut is required."));

        try
        {
            var package = _packages.Read(day);
            if (package == null)
                return NotFound(ProblemOf(404, $"The lesson of {day} has no package."));
            if (!LessonPackageViewBuilder.Items(package).TryGetValue(itemId, out var item))
                return NotFound(ProblemOf(404, $"The package of {day} has no item '{itemId}'."));

            var at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            _packages.ChangeMark(day, itemId, known => LessonPackageViewBuilder.Changed(item, known, body.Fields, body.LeftOut, at));
            _logger.LogInformation("Item {Id} of the package of {Date} edited", itemId, day);
            return Ok(await ViewAsync(day, ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ProblemOf(400, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ProblemOf(409, ex.Message));
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "The edit of item {Id} of the package of {Date} was not saved", itemId, day);
            return StatusCode(500, ProblemOf(500, ex.Message));
        }
    }

    /// <summary>Takes every edit off one item: it reads as the builder wrote it. Answers with the package as it reads now.</summary>
    [HttpDelete("api/sessions/{date}/package/marks/{id}")]
    public async Task<ActionResult<LessonPackageView>> RemoveMark(string date, string id, CancellationToken ct = default)
    {
        if (!LessonPageRules.TryDate(date, out var day))
            return BadRequest(ProblemOf(400, "The date of the lesson must be written YYYY-MM-DD."));
        if (!LessonPackageViewBuilder.TryMarkId(id, out var itemId))
            return BadRequest(ProblemOf(400, "The id of the item is small Latin letters, digits and dashes."));

        try
        {
            if (_packages.Read(day) == null)
                return NotFound(ProblemOf(404, $"The lesson of {day} has no package."));
            var had = false;
            _packages.ChangeMark(day, itemId, known =>
            {
                // What a hand added to the entry is not an edit of the owner: it stays in the file.
                had = known is { HasEdit: true };
                return had ? known!.WithoutEdit() : known;
            });
            if (!had)
                return NotFound(ProblemOf(404, $"Item '{itemId}' of the package of {day} has no edit."));
            _logger.LogInformation("Edits taken off item {Id} of the package of {Date}", itemId, day);
            return Ok(await ViewAsync(day, ct));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ProblemOf(409, ex.Message));
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "The edits of item {Id} of the package of {Date} were not taken off", itemId, day);
            return StatusCode(500, ProblemOf(500, ex.Message));
        }
    }

    /// <summary>
    /// The word forms of every lesson before this date (lower case, no accents), oldest first — what a
    /// package builder needs to tell the words a new lesson says for the first time. They are the forms the
    /// screen counts "said before" with (<see cref="LessonWordRules.KnownForm"/>), so the builder and the
    /// screen agree on which words are new.
    /// </summary>
    [HttpGet("api/sessions/words")]
    public async Task<ActionResult<LessonFormsResponse>> Words([FromQuery] string? before, CancellationToken ct = default)
    {
        if (!LessonPageRules.TryDate(before, out var day))
            return BadRequest(ProblemOf(400, "before is the date of a lesson, written YYYY-MM-DD."));
        var read = await _words.ReadAsync(ct);
        var lessons = read.Lessons
            .Where(l => string.CompareOrdinal(l.Date, day) < 0)
            .OrderBy(l => l.Date, StringComparer.Ordinal)
            .Select(l => new LessonForms(l.Date, l.Forms.OrderBy(f => f, StringComparer.Ordinal).ToList()))
            .ToList();
        return Ok(new LessonFormsResponse(day, lessons));
    }

    /// <summary>
    /// The audio of one track of the recordings folder, for playing a phrase of a package from its time.
    /// The file is only read, and a part of it can be asked for (HTTP range), so playing starts at once
    /// anywhere in a long recording.
    /// </summary>
    [HttpGet("api/sessions/tracks/audio")]
    public IActionResult TrackAudio([FromQuery] string? track)
    {
        var found = _catalog.Enabled ? _catalog.FindTrack(track?.Trim()) : null;
        if (found == null || !System.IO.File.Exists(found.FullPath))
            return NotFound(ProblemOf(404, "This track is not in the recordings folder."));
        return PhysicalFile(found.FullPath, AudioType(found.FullPath), enableRangeProcessing: true);
    }

    private async Task<LessonPackageView?> ViewAsync(string day, CancellationToken ct)
    {
        var package = _packages.Read(day);
        if (package == null)
            return null;
        var marks = _packages.Marks(day);
        var read = await _words.ReadAsync(ct);
        return LessonPackageViewBuilder.Build(day, package, marks, read.Lessons, read.MixedTrackByJob);
    }

    private static string AudioType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".m4a" or ".mp4" or ".aac" => "audio/mp4",
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".ogg" or ".oga" or ".opus" => "audio/ogg",
        ".flac" => "audio/flac",
        ".webm" => "audio/webm",
        _ => "application/octet-stream",
    };

    private static ProblemDetails ProblemOf(int status, string detail) => new()
    {
        Title = status switch { 400 => "Bad Request", 404 => "Not Found", 409 => "Conflict", _ => "Error" },
        Status = status,
        Detail = detail,
    };
}

/// <summary>The word forms of one lesson.</summary>
public sealed record LessonForms(string Date, IReadOnlyList<string> Forms);

/// <summary>The answer of <c>GET /api/sessions/words</c>: the lessons before <see cref="Before"/>, oldest first.</summary>
public sealed record LessonFormsResponse(string Before, IReadOnlyList<LessonForms> Lessons);

public sealed class LessonMarkBody
{
    /// <summary>Name of the field — the text to write there; null — back to what the builder wrote.</summary>
    public Dictionary<string, string?>? Fields { get; set; }

    /// <summary>True — take the item out of the lists; false — put it back; not given — as it was.</summary>
    public bool? LeftOut { get; set; }
}
