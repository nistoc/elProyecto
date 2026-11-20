using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.LessonPackages.Application;
using XtractManager.Features.Sessions.Application;
using XtractManager.Features.Sessions.Infrastructure;

namespace XtractManager.Controllers;

/// <summary>
/// Lessons as they lie in the recordings folder (<c>Recordings:RootPath</c>): lesson → period → track, with the
/// jobs that already transcribed each track. A run of a track is an ordinary job on a copy of the file;
/// the recordings folder itself is only read.
/// </summary>
[ApiController]
public class SessionsController : ControllerBase
{
    // The defaults of a run live in RecordingRunDefaults (a batch of days reads them too); these names stay for callers.
    public const double DefaultEstimateUsdPerMinute = RecordingRunDefaults.EstimateUsdPerMinute;

    public const string EstimateKey = RecordingRunDefaults.EstimateKey;

    /// <summary>The model a participant track goes to when the run names none, see <see cref="RecordingRunDefaults.ParticipantTrackModel"/>.</summary>
    public const string DefaultParticipantTrackModel = RecordingRunDefaults.ParticipantTrackModel;

    /// <summary>Config key of that model; an empty value leaves participant tracks on the shared model.</summary>
    public const string ParticipantTrackModelKey = RecordingRunDefaults.ParticipantTrackModelKey;

    public const double DefaultParticipantTrackEstimateUsdPerMinute = RecordingRunDefaults.ParticipantTrackEstimateUsdPerMinute;

    public const string ParticipantTrackEstimateKey = RecordingRunDefaults.ParticipantTrackEstimateKey;

    private readonly IRecordingsCatalog _catalog;
    private readonly IJobStore _store;
    private readonly IJobWorkspace _workspace;
    private readonly IPipelineQueue _pipelineQueue;
    private readonly ILessonPagesStore _pages;
    private readonly ILessonPackageStore _packages;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SessionsController> _logger;
    private readonly XtractManager.Features.LessonNotes.Application.ILessonNotesReader? _notes;

    public SessionsController(
        IRecordingsCatalog catalog,
        IJobStore store,
        IJobWorkspace workspace,
        IPipelineQueue pipelineQueue,
        ILessonPagesStore pages,
        ILessonPackageStore packages,
        IConfiguration configuration,
        ILogger<SessionsController> logger,
        XtractManager.Features.LessonNotes.Application.ILessonNotesReader? notes = null)
    {
        _catalog = catalog;
        _store = store;
        _workspace = workspace;
        _pipelineQueue = pipelineQueue;
        _pages = pages;
        _packages = packages;
        _configuration = configuration;
        _logger = logger;
        _notes = notes;
    }

    /// <summary>
    /// Lessons of the recordings folder, newest first. <c>enabled: false</c> with an empty list when the folder
    /// is not configured.
    /// </summary>
    [HttpGet("api/sessions")]
    public async Task<ActionResult<SessionsResponse>> List(CancellationToken ct = default)
    {
        var estimate = EstimateUsdPerMinute();
        var participantModel = ParticipantTrackModel();
        var participantEstimate = Price(ParticipantTrackEstimateKey, DefaultParticipantTrackEstimateUsdPerMinute);
        if (!_catalog.Enabled)
        {
            return Ok(new SessionsResponse(false, null, estimate, Array.Empty<RecordingSession>())
            {
                ParticipantTrackModel = participantModel,
                ParticipantTrackEstimateUsdPerMinute = participantEstimate,
            });
        }

        var pages = _pages.Load();
        var packages = _packages.List();
        var notes = _notes?.Infos();
        var withRuns = (await LessonSessions.WithRunsAsync(_catalog, _store, _workspace, ct))
            .Select(s => s with
            {
                Pages = pages.TryGetValue(s.Date, out var ofLesson) ? ofLesson : Array.Empty<LessonPage>(),
                Package = packages.TryGetValue(s.Date, out var package) ? package : null,
                Stage = packages.ContainsKey(s.Date) ? LessonStage.Package : LessonBatchPlanner.StageOf(s),
                Notes = notes != null && notes.TryGetValue(s.Date, out var ofNotes) ? ofNotes : null,
            })
            .ToList();
        return Ok(new SessionsResponse(true, _catalog.RootPath, estimate, withRuns)
        {
            ParticipantTrackModel = participantModel,
            ParticipantTrackEstimateUsdPerMinute = participantEstimate,
        });
    }

    /// <summary>
    /// Puts the address of a page made of the lesson of this date (the lesson package), so the lesson shows
    /// it as a link. Body: <c>{ "url": string, "title"?: string }</c>. A page with this address that the
    /// lesson already has only gets the new title. The page itself lives elsewhere: nothing is fetched from
    /// the address, and the recordings folder is not written — the addresses are kept in the jobs folder.
    /// </summary>
    [HttpPut("api/sessions/{date}/pages")]
    public ActionResult<LessonPagesResponse> PutPage(string date, [FromBody] LessonPageBody? body)
    {
        if (!LessonPageRules.TryDate(date, out var day))
            return BadRequest(ProblemOf(400, "The date of the lesson must be written YYYY-MM-DD."));
        if (!LessonPageRules.TryUrl(body?.Url, out var url, out var urlError))
            return BadRequest(ProblemOf(400, urlError ?? "url is not valid."));
        if (!LessonPageRules.TryTitle(body?.Title, out var title, out var titleError))
            return BadRequest(ProblemOf(400, titleError ?? "title is not valid."));
        if (!_catalog.Enabled || _catalog.Scan().All(s => s.Date != day))
            return NotFound(ProblemOf(404, $"No lesson of {day} is in the recordings folder."));

        try
        {
            var pages = _pages.Put(day, url, title);
            _logger.LogInformation("Page put for the lesson of {Date}; it has {Count} now", day, pages.Count);
            return Ok(new LessonPagesResponse(day, pages));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ProblemOf(409, ex.Message));
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "The page of the lesson of {Date} was not saved", day);
            return StatusCode(500, ProblemOf(500, ex.Message));
        }
    }

    /// <summary>
    /// Takes the page with this address off the lesson. Only the link is removed; the page stays where it is.
    /// </summary>
    [HttpDelete("api/sessions/{date}/pages")]
    public ActionResult<LessonPagesResponse> RemovePage(string date, [FromQuery] string? url)
    {
        if (!LessonPageRules.TryDate(date, out var day))
            return BadRequest(ProblemOf(400, "The date of the lesson must be written YYYY-MM-DD."));
        if (!LessonPageRules.TryUrl(url, out var address, out var urlError))
            return BadRequest(ProblemOf(400, urlError ?? "url is not valid."));

        try
        {
            var left = _pages.Remove(day, address);
            if (left == null)
                return NotFound(ProblemOf(404, $"The lesson of {day} has no page with this address."));
            _logger.LogInformation("Page taken off the lesson of {Date}; {Count} left", day, left.Count);
            return Ok(new LessonPagesResponse(day, left));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ProblemOf(409, ex.Message));
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "The page of the lesson of {Date} was not taken off", day);
            return StatusCode(500, ProblemOf(500, ex.Message));
        }
    }

    /// <summary>
    /// Starts a job for one track: the file is copied into a new job folder and goes through the usual
    /// pipeline (the transcription is paid). Body:
    /// <c>{ "track": string, "settings"?: { "silence_keep_sec"?, "silence_compress_enabled"?, "model"?, "chunk_overlap_sec"? } }</c>.
    /// A participant track whose settings name no model gets <see cref="ParticipantTrackModelKey"/>.
    /// </summary>
    [HttpPost("api/sessions/runs")]
    public async Task<ActionResult<CreateJobResponse>> StartRun([FromBody] StartTrackRunBody? body, CancellationToken ct = default)
    {
        if (!_catalog.Enabled)
            return NotFound(ProblemOf(404, "The recordings folder is not configured (Recordings:RootPath)."));

        var trackId = body?.Track?.Trim();
        if (string.IsNullOrEmpty(trackId))
            return BadRequest(ProblemOf(400, "track is required."));

        string? settingsText = null;
        if (body!.Settings is { } settings && settings.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            settingsText = settings.GetRawText();
        if (!JobSettingsDocument.TryBuild(settingsText, out var settingsFileJson, out var settingsError))
            return BadRequest(ProblemOf(400, settingsError ?? "settings are not valid."));

        var found = _catalog.FindTrack(trackId);
        if (found == null)
            return NotFound(ProblemOf(404, $"Track '{trackId}' is not in the recordings folder."));

        // One voice needs no speakers: a participant track goes to the model that writes each language in
        // its own script, unless the run names a model itself.
        if (found.Track.Kind == RecordingsScanner.KindParticipant && ParticipantTrackModel() is { } trackModel)
        {
            var withModel = JobSettingsDocument.WithModelWhenNoneIsNamed(settingsText, trackModel);
            if (!JobSettingsDocument.TryBuild(withModel, out settingsFileJson, out settingsError))
                return BadRequest(ProblemOf(400, settingsError ?? "settings are not valid."));
        }

        string jobId;
        try
        {
            jobId = await TrackRunStarter.StartAsync(_store, _workspace, _pipelineQueue, found, settingsFileJson, ct);
        }
        catch (TrackRunStartException ex)
        {
            _logger.LogError(ex.InnerException, "Failed to prepare job {JobId} for track {Track}", ex.JobId, trackId);
            return StatusCode(500, ProblemOf(500, ex.Message));
        }

        _logger.LogInformation("Job {JobId} started for track {Track}", jobId, found.Track.Id);
        return Accepted(new CreateJobResponse(jobId));
    }

    private double EstimateUsdPerMinute() => Price(EstimateKey, DefaultEstimateUsdPerMinute);

    private double Price(string key, double fallback) => RecordingRunDefaults.Price(_configuration, key, fallback);

    /// <summary>
    /// The model of a participant track that names none: the default when the key is absent, null when the
    /// key is empty (participant tracks then stay on the shared model) or is not a model id.
    /// </summary>
    private string? ParticipantTrackModel()
    {
        var model = RecordingRunDefaults.Model(
            _configuration, ParticipantTrackModelKey, DefaultParticipantTrackModel, out var notAModel);
        if (notAModel)
            _logger.LogWarning("{Key} is not a model id; participant tracks stay on the shared model", ParticipantTrackModelKey);
        return model;
    }

    private static ProblemDetails ProblemOf(int status, string detail) => new()
    {
        Title = status switch { 400 => "Bad Request", 404 => "Not Found", 409 => "Conflict", _ => "Error" },
        Status = status,
        Detail = detail,
    };
}

/// <summary>The pages of the lesson of one date after a change.</summary>
public sealed record LessonPagesResponse(string Date, IReadOnlyList<LessonPage> Pages);

public sealed class LessonPageBody
{
    public string? Url { get; set; }

    /// <summary>What the link is called; the UI names it itself when this is empty.</summary>
    public string? Title { get; set; }
}

/// <param name="Enabled">False when <c>Recordings:RootPath</c> is empty or the folder does not exist.</param>
/// <param name="EstimateUsdPerMinute">Upper bound used to show what a run may cost before it is started.</param>
public sealed record SessionsResponse(
    bool Enabled,
    string? RootPath,
    double EstimateUsdPerMinute,
    IReadOnlyList<RecordingSession> Sessions)
{
    /// <summary>The model a participant track goes to when the run names none; null when it is the shared one.</summary>
    public string? ParticipantTrackModel { get; init; }

    /// <summary>Upper bound of the price of a minute of a track with <see cref="ParticipantTrackModel"/>.</summary>
    public double ParticipantTrackEstimateUsdPerMinute { get; init; }
}

public sealed class StartTrackRunBody
{
    public string? Track { get; set; }

    /// <summary>Settings of this one job; the same keys as the <c>settings</c> form field of <c>POST /api/jobs</c>.</summary>
    public JsonElement? Settings { get; set; }
}
