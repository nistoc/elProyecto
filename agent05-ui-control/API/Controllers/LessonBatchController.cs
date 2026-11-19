using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Sessions.Application;
using XtractManager.Features.Sessions.Infrastructure;

namespace XtractManager.Controllers;

/// <summary>
/// The text of whole lessons, by groups of days: one call works out what the lessons of the asked dates
/// still miss — a run of a track, a dialogue of participant tracks — says what that costs, and, when asked
/// to, starts it. It keeps no state of its own: every call reads the folders anew, so calling it again
/// does only what is missing then, and it can be called until every lesson has its text.
/// </summary>
[ApiController]
public class LessonBatchController : ControllerBase
{
    private readonly IRecordingsCatalog _catalog;
    private readonly IJobStore _store;
    private readonly IJobWorkspace _workspace;
    private readonly IPipelineQueue _pipelineQueue;
    private readonly IDialogueBuilder _dialogues;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LessonBatchController> _logger;

    public LessonBatchController(
        IRecordingsCatalog catalog,
        IJobStore store,
        IJobWorkspace workspace,
        IPipelineQueue pipelineQueue,
        IDialogueBuilder dialogues,
        IConfiguration configuration,
        ILogger<LessonBatchController> logger)
    {
        _catalog = catalog;
        _store = store;
        _workspace = workspace;
        _pipelineQueue = pipelineQueue;
        _dialogues = dialogues;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Body: <c>{ "dates": ["YYYY-MM-DD", …], "dryRun"?: bool, "settings"?: {…}, "retryFailed"?: bool, "maxUsd"?: number }</c>.
    /// <c>dryRun</c> is true unless the body says false: the answer is then only the plan with its estimate.
    /// With <c>dryRun: false</c> the runs of the plan are started (paid) and the dialogues that are due are
    /// merged (code only); when the estimate is above <c>maxUsd</c> nothing is started (409).
    /// <c>settings</c> are the settings of one job (<c>POST /api/sessions/runs</c>) given to every run; a run
    /// that gets no model from them takes the model of its kind of track.
    /// </summary>
    [HttpPost("api/sessions/batch")]
    public async Task<ActionResult<LessonBatchResponse>> Run([FromBody] LessonBatchBody? body, CancellationToken ct = default)
    {
        if (!_catalog.Enabled)
            return NotFound(ProblemOf(404, "The recordings folder is not configured (Recordings:RootPath)."));

        var dates = (body?.Dates ?? new List<string>()).Select(d => d?.Trim() ?? "").Where(d => d.Length > 0).ToList();
        if (dates.Count == 0)
            return BadRequest(ProblemOf(400, "dates is required: the dates of the lessons, YYYY-MM-DD."));
        var days = new List<string>();
        foreach (var date in dates)
        {
            if (!LessonPageRules.TryDate(date, out var day))
                return BadRequest(ProblemOf(400, $"'{date}' is not a date written YYYY-MM-DD."));
            days.Add(day);
        }

        string? settingsText = null;
        if (body!.Settings is { } settings && settings.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            settingsText = settings.GetRawText();
        if (!JobSettingsDocument.TryBuild(settingsText, out _, out var settingsError))
            return BadRequest(ProblemOf(400, settingsError ?? "settings are not valid."));

        var options = new LessonBatchOptions(
            ModelNamedIn(body.Settings),
            ModelOf(RecordingRunDefaults.ParticipantTrackModelKey, RecordingRunDefaults.ParticipantTrackModel),
            ModelOf(RecordingRunDefaults.MixedOnlyTrackModelKey, RecordingRunDefaults.MixedOnlyTrackModel),
            (kind, model) => RecordingRunDefaults.UsdPerMinute(_configuration, kind, model),
            body.RetryFailed == true);

        var root = _catalog.RootPath!;
        var sessions = await LessonSessions.WithRunsAsync(_catalog, _store, _workspace, ct);
        var plan = LessonBatchPlanner.Plan(
            sessions, days, options,
            track => TrackRunsIndex.ContentKey(Path.Combine(root, track.Id.Replace('/', Path.DirectorySeparatorChar))));

        var dryRun = body.DryRun != false;
        if (dryRun)
            return Ok(new LessonBatchResponse(true, plan, Array.Empty<string>(), Array.Empty<string>()));

        if (body.MaxUsd is { } cap && plan.EstimateUsd > cap)
        {
            return Conflict(ProblemOf(
                409,
                FormattableString.Invariant($"The runs of the plan may cost up to ${plan.EstimateUsd:0.00}, above maxUsd ${cap:0.00}; nothing was started.")));
        }

        var started = new List<string>();
        var merged = new List<string>();
        var lessons = new List<LessonBatchLesson>();
        foreach (var lesson in plan.Lessons)
        {
            var steps = new List<LessonBatchStep>();
            foreach (var step in lesson.Steps)
            {
                steps.Add(step.Kind switch
                {
                    LessonBatchStepKind.Run => await StartAsync(step, settingsText, started, ct),
                    LessonBatchStepKind.Dialogue => await MergeAsync(step, sessions, merged, ct),
                    _ => step,
                });
            }

            lessons.Add(lesson with { Steps = steps });
        }

        _logger.LogInformation(
            "Batch of {Dates}: {Started} runs started, {Merged} dialogues merged", string.Join(", ", days), started.Count, merged.Count);
        return Accepted(new LessonBatchResponse(false, plan with { Lessons = lessons }, started, merged));
    }

    private async Task<LessonBatchStep> StartAsync(LessonBatchStep step, string? settingsText, List<string> started, CancellationToken ct)
    {
        var found = _catalog.FindTrack(step.Track);
        if (found == null)
            return step with { Kind = LessonBatchStepKind.Failed, Reason = "The track is no longer in the recordings folder." };

        var text = step.Model == null ? settingsText : JobSettingsDocument.WithModelWhenNoneIsNamed(settingsText, step.Model);
        if (!JobSettingsDocument.TryBuild(text, out var settingsFileJson, out var error))
            return step with { Kind = LessonBatchStepKind.Failed, Reason = error ?? "settings are not valid." };

        try
        {
            var jobId = await TrackRunStarter.StartAsync(_store, _workspace, _pipelineQueue, found, settingsFileJson, ct);
            started.Add(jobId);
            return step with { Kind = LessonBatchStepKind.Started, Reason = "The run is queued.", JobIds = new[] { jobId } };
        }
        catch (TrackRunStartException ex)
        {
            _logger.LogError(ex.InnerException, "Batch: failed to prepare job {JobId} for track {Track}", ex.JobId, step.Track);
            return step with { Kind = LessonBatchStepKind.Failed, Reason = ex.Message };
        }
    }

    private async Task<LessonBatchStep> MergeAsync(
        LessonBatchStep step, IReadOnlyList<RecordingSession> sessions, List<string> merged, CancellationToken ct)
    {
        var period = sessions.SelectMany(s => s.Periods).FirstOrDefault(p => p.Id == step.Period);
        var tracks = (period?.Tracks ?? Array.Empty<RecordingTrack>())
            .Where(t => t.Kind == LessonBatchPlanner.KindParticipant)
            .Select(t => (Track: t, Run: t.Runs.LastOrDefault(r => r.HasTranscript)))
            .Where(x => x.Run != null)
            .Select(x => new DialogueTrackRequest(
                x.Run!.JobId,
                string.IsNullOrWhiteSpace(x.Track.Participant) ? Path.GetFileNameWithoutExtension(x.Track.FileName) : x.Track.Participant!))
            .ToList();
        try
        {
            var result = await _dialogues.CreateAsync(new DialogueCreateRequest(null, tracks), ct);
            merged.Add(result.JobId);
            return step with { Kind = LessonBatchStepKind.Merged, Reason = "The dialogue is merged.", JobIds = new[] { result.JobId } };
        }
        catch (DialogueRequestException ex)
        {
            _logger.LogWarning("Batch: the dialogue of {Period} was not merged: {Message}", step.Period, ex.Message);
            return step with { Kind = LessonBatchStepKind.Failed, Reason = ex.Message };
        }
    }

    private string? ModelOf(string key, string fallback)
    {
        var model = RecordingRunDefaults.Model(_configuration, key, fallback, out var notAModel);
        if (notAModel)
            _logger.LogWarning("{Key} is not a model id; such tracks stay on the shared model", key);
        return model;
    }

    /// <summary>The model the settings of the call name for every run, if they name one.</summary>
    private static string? ModelNamedIn(JsonElement? settings)
    {
        if (settings is not { ValueKind: JsonValueKind.Object } given
            || !given.TryGetProperty("model", out var model)
            || model.ValueKind != JsonValueKind.String)
            return null;
        var id = model.GetString()?.Trim();
        return string.IsNullOrEmpty(id) ? null : id;
    }

    private static ProblemDetails ProblemOf(int status, string detail) => new()
    {
        Title = status switch { 400 => "Bad Request", 404 => "Not Found", 409 => "Conflict", _ => "Error" },
        Status = status,
        Detail = detail,
    };
}

public sealed class LessonBatchBody
{
    /// <summary>Dates of the lessons, <c>YYYY-MM-DD</c>.</summary>
    public List<string>? Dates { get; set; }

    /// <summary>False starts what the plan names; anything else only answers with the plan.</summary>
    public bool? DryRun { get; set; }

    /// <summary>Settings given to every run; the same keys as <c>settings</c> of <c>POST /api/sessions/runs</c>.</summary>
    public JsonElement? Settings { get; set; }

    /// <summary>Start a track again whose runs all ended without a transcript.</summary>
    public bool? RetryFailed { get; set; }

    /// <summary>Nothing is started when the estimate of the plan is above this many dollars.</summary>
    public double? MaxUsd { get; set; }
}

/// <param name="DryRun">True when nothing was started and the plan is what a call with <c>dryRun: false</c> would do.</param>
/// <param name="Plan">The lessons with their steps; after a start the steps carried out are <c>started</c> and <c>merged</c>.</param>
/// <param name="StartedJobs">Jobs queued by this call.</param>
/// <param name="Dialogues">Dialogue jobs merged by this call.</param>
public sealed record LessonBatchResponse(
    bool DryRun,
    LessonBatchPlan Plan,
    IReadOnlyList<string> StartedJobs,
    IReadOnlyList<string> Dialogues);
