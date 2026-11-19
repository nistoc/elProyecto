using XtractManager.Features.Sessions.Application;
using Xunit;

namespace XtractManager.Tests;

/// <summary>
/// What a batch of days plans for the lessons as they are: which tracks it runs, what it waits for, when a
/// dialogue is due, what it costs. The lessons here are made up.
/// </summary>
public sealed class LessonBatchPlannerTests
{
    private const string Plain = "gpt-transcribe";

    // Dollars a minute: the shared model 0.02, the plain model 0.01 on a participant track and 0.011 on a mixed file.
    private static LessonBatchOptions Options(string? model = null, bool retryFailed = false) => new(
        model, Plain, Plain,
        (kind, named) => named == null || !named.StartsWith(Plain) ? 0.02 : kind == LessonBatchPlanner.KindParticipant ? 0.01 : 0.011,
        retryFailed);

    private static TrackRun Run(string job, string status = "done", bool text = true) =>
        new(job, status, text ? "awaiting_refiner" : "transcriber", "2026-01-01T00:00:00Z", text, false);

    private static RecordingTrack Mixed(string period, double minutes, params TrackRun[] runs) =>
        new(period + "/audio1.m4a", LessonBatchPlanner.KindMixed, null, "audio1.m4a", 1000, minutes * 60) { Runs = runs };

    private static RecordingTrack Voice(string period, string name, double minutes, params TrackRun[] runs) =>
        new($"{period}/Audio Record/audio{name}1.m4a", LessonBatchPlanner.KindParticipant, name, $"audio{name}1.m4a", 500, minutes * 60)
        {
            Runs = runs,
        };

    private static RecordingPeriod Recording(string id, params RecordingTrack[] tracks) =>
        new(id, null, null, "ready", false, false, false, false, 0, tracks);

    private static RecordingSession Lesson(string date, params RecordingPeriod[] periods) =>
        new(date.Replace('-', '.'), date, null, false, periods);

    private static LessonBatchPlan Plan(RecordingSession lesson, LessonBatchOptions? options = null) =>
        LessonBatchPlanner.Plan(new[] { lesson }, new[] { lesson.Date }, options ?? Options());

    [Fact]
    public void A_recording_with_only_the_mixed_file_gets_one_run_with_the_plain_model()
    {
        var lesson = Lesson("2025-08-06",
            Recording("2025.08.06/a", Mixed("2025.08.06/a", 40)),
            Recording("2025.08.06/b", Mixed("2025.08.06/b", 20)));

        var plan = Plan(lesson);

        var only = Assert.Single(plan.Lessons);
        Assert.Equal(LessonStage.NoText, only.Stage);
        Assert.All(only.Steps, s =>
        {
            Assert.Equal(LessonBatchStepKind.Run, s.Kind);
            Assert.Equal(Plain, s.Model);
        });
        Assert.Equal(2, plan.Runs);
        Assert.Equal(60, plan.Minutes);
        Assert.Equal(0.66, plan.EstimateUsd, 4);
    }

    [Fact]
    public void Participant_tracks_are_run_and_the_mixed_file_next_to_them_is_left()
    {
        var p = "2026.10.23/a";
        var lesson = Lesson("2026-10-23", Recording(p, Mixed(p, 40), Voice(p, "Ana", 40), Voice(p, "Luis", 40)));

        var plan = Plan(lesson);

        Assert.Equal(2, plan.Runs);
        Assert.All(plan.Lessons[0].Steps, s => Assert.Equal(LessonBatchPlanner.KindParticipant, s.TrackKind));
        Assert.Equal(0.8, plan.EstimateUsd, 4);
    }

    [Fact]
    public void A_track_that_has_a_transcript_is_not_run_again()
    {
        var lesson = Lesson("2025-08-06",
            Recording("d/a", Mixed("d/a", 40, Run("old", "failed", text: false), Run("j1"))),
            Recording("d/b", Mixed("d/b", 20)));

        var plan = Plan(lesson);

        var steps = plan.Lessons[0].Steps;
        Assert.Equal(LessonBatchStepKind.Done, steps[0].Kind);
        Assert.Equal(new[] { "j1" }, steps[0].JobIds);
        Assert.Equal(LessonBatchStepKind.Run, steps[1].Kind);
        Assert.Equal(LessonStage.Partial, plan.Lessons[0].Stage);
        Assert.Equal(1, plan.Runs);
        Assert.Equal(20, plan.Minutes);
    }

    [Theory]
    [InlineData("queued")]
    [InlineData("running")]
    public void A_track_whose_run_is_queued_or_going_waits(string status)
    {
        var lesson = Lesson("2025-08-06", Recording("d/a", Mixed("d/a", 40, Run("j1", status, text: false))));

        var plan = Plan(lesson);

        var step = Assert.Single(plan.Lessons[0].Steps);
        Assert.Equal(LessonBatchStepKind.Wait, step.Kind);
        Assert.Equal(new[] { "j1" }, step.JobIds);
        Assert.Equal(LessonStage.Transcribing, plan.Lessons[0].Stage);
        Assert.Equal(0, plan.Runs);
        Assert.Equal(0, plan.EstimateUsd);
    }

    [Fact]
    public void A_track_whose_runs_all_ended_without_a_transcript_is_run_again_only_when_asked()
    {
        var lesson = Lesson("2025-08-06", Recording("d/a", Mixed("d/a", 40, Run("j1", "failed", text: false))));

        var byDefault = Plan(lesson);
        var asked = Plan(lesson, Options(retryFailed: true));

        Assert.Equal(LessonBatchStepKind.Failed, Assert.Single(byDefault.Lessons[0].Steps).Kind);
        Assert.Equal(0, byDefault.Runs);
        Assert.Equal(LessonStage.NoText, byDefault.Lessons[0].Stage);
        Assert.Equal(LessonBatchStepKind.Run, Assert.Single(asked.Lessons[0].Steps).Kind);
        Assert.Equal(1, asked.Runs);
    }

    [Fact]
    public void The_same_file_lying_twice_is_sent_once()
    {
        var first = Mixed("d/a", 15);
        var copy = Mixed("d/b", 15);
        var other = Mixed("d/c", 40);
        var lesson = Lesson("2025-10-31", Recording("d/a", first), Recording("d/b", copy), Recording("d/c", other));

        var plan = LessonBatchPlanner.Plan(
            new[] { lesson }, new[] { lesson.Date }, Options(),
            track => track.Id == other.Id ? "other" : "same");

        var steps = plan.Lessons[0].Steps;
        Assert.Equal(LessonBatchStepKind.Run, steps[0].Kind);
        Assert.Equal(LessonBatchStepKind.Duplicate, steps[1].Kind);
        Assert.Contains(first.Id, steps[1].Reason);
        Assert.Equal(LessonBatchStepKind.Run, steps[2].Kind);
        Assert.Equal(2, plan.Runs);
        Assert.Equal(55, plan.Minutes);
    }

    [Fact]
    public void A_dialogue_is_due_once_every_participant_track_has_a_transcript()
    {
        var p = "2026.10.23/a";
        var half = Lesson("2026-10-23", Recording(p, Voice(p, "Ana", 40, Run("j1")), Voice(p, "Luis", 40, Run("j2", "running", text: false))));
        var whole = Lesson("2026-10-23", Recording(p, Voice(p, "Ana", 40, Run("j1")), Voice(p, "Luis", 40, Run("j2"))));
        var merged = whole with
        {
            Periods = whole.Periods
                .Select(x => x with { Dialogues = new[] { new PeriodDialogue("d1", null, null, new[] { "j1", "j2" }, 1) } })
                .ToList(),
        };

        var waiting = Plan(half);
        var due = Plan(whole);
        var done = Plan(merged);

        Assert.DoesNotContain(waiting.Lessons[0].Steps, s => s.Kind == LessonBatchStepKind.Dialogue);
        Assert.Equal(LessonStage.Transcribing, waiting.Lessons[0].Stage);

        var dialogue = Assert.Single(due.Lessons[0].Steps, s => s.Kind == LessonBatchStepKind.Dialogue);
        Assert.Equal(new[] { "j1", "j2" }, dialogue.JobIds);
        Assert.Equal(1, due.Dialogues);
        Assert.Equal(0, due.Runs);
        Assert.Equal(LessonStage.DialoguePending, due.Lessons[0].Stage);

        Assert.DoesNotContain(done.Lessons[0].Steps, s => s.Kind == LessonBatchStepKind.Dialogue);
        Assert.Equal(LessonStage.TextReady, done.Lessons[0].Stage);
        Assert.Equal(LessonStage.TextReady, LessonBatchPlanner.StageOf(merged));
    }

    [Fact]
    public void A_recording_that_is_not_converted_is_named_and_never_run()
    {
        var raw = new RecordingPeriod("d/raw", null, null, "unconverted", false, false, false, false, 12345, Array.Empty<RecordingTrack>());
        var onlyRaw = Lesson("2026-10-30", raw);
        var withAudio = Lesson("2026-10-30", raw, Recording("d/a", Mixed("d/a", 40, Run("j1"))));

        var nothing = Plan(onlyRaw);
        var rest = Plan(withAudio);

        Assert.Equal(LessonBatchStepKind.Unconverted, Assert.Single(nothing.Lessons[0].Steps).Kind);
        Assert.Equal(LessonStage.NoRecording, nothing.Lessons[0].Stage);
        Assert.Equal(0, nothing.Runs);
        Assert.Equal(LessonStage.TextReady, rest.Lessons[0].Stage);
    }

    [Fact]
    public void A_date_without_a_lesson_is_told_and_the_lessons_come_by_date_each_once()
    {
        var early = Lesson("2025-08-06", Recording("e/a", Mixed("e/a", 10)));
        var late = Lesson("2025-08-13", Recording("l/a", Mixed("l/a", 10)));

        var plan = LessonBatchPlanner.Plan(
            new[] { late, early }, new[] { "2025-08-13", "2025-08-06", "2025-08-13", "2025-01-01" }, Options());

        Assert.Equal(new[] { "2025-08-06", "2025-08-13" }, plan.Lessons.Select(l => l.Date));
        Assert.Equal(new[] { "2025-01-01" }, plan.UnknownDates);
        Assert.Equal(2, plan.Runs);
    }

    [Fact]
    public void The_model_the_call_names_goes_to_every_run_with_its_own_price()
    {
        var p = "2026.10.23/a";
        var lesson = Lesson("2026-10-23",
            Recording(p, Voice(p, "Ana", 10), Voice(p, "Luis", 10)),
            Recording("2026.10.23/b", Mixed("2026.10.23/b", 10)));

        var plan = Plan(lesson, Options(model: "other-model"));

        Assert.All(plan.Lessons[0].Steps, s => Assert.Equal("other-model", s.Model));
        Assert.Equal(0.6, plan.EstimateUsd, 4);
    }
}
