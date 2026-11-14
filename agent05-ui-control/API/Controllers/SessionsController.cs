using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using XtractManager.Features.Jobs.Application;
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
    /// <summary>Upper bound of the price of the diarize model per minute of audio, measured by tokens on 2026-11-13.</summary>
    public const double DefaultEstimateUsdPerMinute = 0.017;

    public const string EstimateKey = "Recordings:EstimateUsdPerMinute";

    /// <summary>
    /// The model a participant track goes to when the run names none. A participant track is one voice, so
    /// it needs no speakers; this model writes each language in its own script (the transcriber asks it for
    /// a chunk whole and in pieces). The mixed track and a plain upload keep the transcriber's shared model.
    /// </summary>
    public const string DefaultParticipantTrackModel = "gpt-transcribe";

    /// <summary>Config key of that model; an empty value leaves participant tracks on the shared model.</summary>
    public const string ParticipantTrackModelKey = "Recordings:ParticipantTrackModel";

    /// <summary>
    /// Upper bound of the price of that model per minute of a track: the audio is asked twice at $0.0045 a
    /// minute, and a few pieces once more. The runs of 2026-11-14 cost about $0.006 a minute.
    /// </summary>
    public const double DefaultParticipantTrackEstimateUsdPerMinute = 0.01;

    public const string ParticipantTrackEstimateKey = "Recordings:ParticipantTrackEstimateUsdPerMinute";

    private readonly IRecordingsCatalog _catalog;
    private readonly IJobStore _store;
    private readonly IJobWorkspace _workspace;
    private readonly IPipelineQueue _pipelineQueue;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        IRecordingsCatalog catalog,
        IJobStore store,
        IJobWorkspace workspace,
        IPipelineQueue pipelineQueue,
        IConfiguration configuration,
        ILogger<SessionsController> logger)
    {
        _catalog = catalog;
        _store = store;
        _workspace = workspace;
        _pipelineQueue = pipelineQueue;
        _configuration = configuration;
        _logger = logger;
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

        var sessions = _catalog.Scan();
        var jobs = await _store.ListAsync(new JobListFilter(Limit: int.MaxValue), ct);
        var index = TrackRunsIndex.Build(_workspace.WorkspaceRootPath, jobs, _catalog.RootPath);
        var withRuns = sessions
            .Select(s => s with { Periods = s.Periods.Select(index.Attach).ToList() })
            .ToList();
        return Ok(new SessionsResponse(true, _catalog.RootPath, estimate, withRuns)
        {
            ParticipantTrackModel = participantModel,
            ParticipantTrackEstimateUsdPerMinute = participantEstimate,
        });
    }

    /// <summary>
    /// Starts a job for one track: the file is copied into a new job folder and goes through the usual
    /// pipeline (the transcription is paid). Body:
    /// <c>{ "track": string, "settings"?: { "silence_keep_sec"?, "silence_compress_enabled"?, "model"? } }</c>.
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

        var tags = new List<string> { "lesson:" + found.Session.Date, "track:" + found.Track.Kind };
        var jobId = await _store.CreateAsync(new JobCreateInput(found.Track.FileName, tags), ct);
        try
        {
            await using (var source = new FileStream(
                             found.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            {
                await _workspace.SaveUploadedFileAsync(jobId, source, found.Track.FileName, ct);
            }

            var jobDir = _workspace.GetJobDirectoryPath(jobId);
            await TrackSourceFile.SaveAsync(
                jobDir,
                new TrackSource
                {
                    Track = found.Track.Id,
                    FileName = found.Track.FileName,
                    SizeBytes = found.Track.SizeBytes,
                    Session = found.Session.Id,
                    Period = found.Period.Id,
                    Kind = found.Track.Kind,
                    Participant = found.Track.Participant,
                    CreatedAt = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                },
                ct);

            if (settingsFileJson != null)
            {
                await System.IO.File.WriteAllTextAsync(
                    Path.Combine(jobDir, JobSettingsDocument.FileName),
                    settingsFileJson,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare job {JobId} for track {Track}", jobId, trackId);
            await _store.DeleteAsync(jobId, ct);
            return StatusCode(500, ProblemOf(500, "Failed to copy the track into a job folder."));
        }

        _logger.LogInformation("Job {JobId} started for track {Track}", jobId, found.Track.Id);
        _pipelineQueue.Enqueue(jobId);
        return Accepted(new CreateJobResponse(jobId));
    }

    private double EstimateUsdPerMinute() => Price(EstimateKey, DefaultEstimateUsdPerMinute);

    private double Price(string key, double fallback)
    {
        var raw = _configuration[key];
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : fallback;
    }

    /// <summary>
    /// The model of a participant track that names none: the default when the key is absent, null when the
    /// key is empty (participant tracks then stay on the shared model) or is not a model id.
    /// </summary>
    private string? ParticipantTrackModel()
    {
        var raw = _configuration[ParticipantTrackModelKey];
        if (raw == null)
            return DefaultParticipantTrackModel;
        var model = raw.Trim();
        if (model.Length == 0)
            return null;
        if (JobSettingsDocument.IsModelId(model))
            return model;
        _logger.LogWarning("{Key} is not a model id; participant tracks stay on the shared model", ParticipantTrackModelKey);
        return null;
    }

    private static ProblemDetails ProblemOf(int status, string detail) => new()
    {
        Title = status switch { 400 => "Bad Request", 404 => "Not Found", _ => "Error" },
        Status = status,
        Detail = detail,
    };
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
