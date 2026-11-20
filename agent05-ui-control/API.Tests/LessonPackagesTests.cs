using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Controllers;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using XtractManager.Features.LessonPackages.Application;
using XtractManager.Features.LessonPackages.Infrastructure;
using XtractManager.Features.Sessions.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>
/// The package of a lesson on disk and over HTTP: the builder's file is only read, the owner's edits are
/// written next to it, a phrase is played from the recordings folder, and the words of the earlier lessons
/// come from the transcripts in the job folders. All names, words and lines here are made up.
/// </summary>
public sealed class LessonPackagesTests : IDisposable
{
    private const string Day = "2026-10-23";
    private const string Period = "2026.10.23/2026-10-23 19.01.16 Maria Lopez's Zoom Meeting";
    private const string MixedTrack = Period + "/audio1844996676.m4a";
    private const string EarlierPeriod = "2026.10.16/2026-10-16 19.00.05 Maria Lopez's Zoom Meeting";
    private const string EarlierMixedTrack = EarlierPeriod + "/audio1844990001.m4a";

    private const string PackageJson = """
        {
          "schema_version": 1,
          "lesson": "2026-10-23",
          "built_at": "2026-10-24T09:00:00Z",
          "builder": "test",
          "periods": [ { "n": 1, "job": "jobdialogue1", "start": "19:01", "lines": 3, "end_at": "0:30" } ],
          "topics": [ { "period": 1, "title": "Weather", "summary": "Talk about the weather", "from_line": 1 } ],
          "grammar": [ { "topic": "Past tense", "rule": "Finished actions", "period": 1,
                         "examples": [ { "period": 1, "at": "0:12", "sec": 12.5, "quote": "Ayer comí." } ], "parts": null } ],
          "word_notes": null,
          "examples": [ { "period": 1, "line": 2, "quote": "Me gusta la ventana.", "ru": "Мне нравится окно.", "shows": "gustar" } ],
          "new_words": [ { "period": 1, "at": "0:05", "sec": 5, "who": "teacher", "es": "ventana", "ru": "окно", "quote": "la ventana" },
                         { "period": 1, "es": "tejado", "ru": "крыша" } ],
          "vocabulary": [],
          "other_words": [ { "word": "chimenea", "raw": "Chimenea", "count": 2, "period": 1, "kind": "spanish" } ],
          "made_by_hand": { "kept": true }
        }
        """;

    private readonly string _base = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "packages_" + Guid.NewGuid().ToString("N"));
    private readonly string _recordings;
    private readonly string _workspace;
    private readonly string _packages;

    public LessonPackagesTests()
    {
        _recordings = Path.Combine(_base, "recordings");
        _workspace = Path.Combine(_base, "runtime");
        _packages = Path.Combine(_base, FileLessonPackageStore.DefaultFolderName);
        Directory.CreateDirectory(_recordings);
        Directory.CreateDirectory(_workspace);
        Put(_recordings, MixedTrack, "mixed audio bytes");
        Put(_recordings, EarlierMixedTrack, "audio of the lesson before");
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, true); } catch { /* ignore */ }
    }

    private static string Put(string root, string relativePath, string content)
    {
        var full = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
        return full;
    }

    private string PackageFile(string day = Day) => Path.Combine(_packages, day, FileLessonPackageStore.PackageFileName);

    private string MarksFile(string day = Day) => Path.Combine(_packages, day, FileLessonPackageStore.MarksFileName);

    private void PutPackage(string day = Day, string json = PackageJson) =>
        Put(_packages, day + "/" + FileLessonPackageStore.PackageFileName, json);

    private sealed record Parts(
        LessonPackagesController Controller,
        FileLessonPackageStore Store,
        RecordingsCatalog Catalog,
        JobWorkspace Workspace,
        IJobStore Jobs,
        IConfiguration Config);

    private Parts Create(LessonsRead? words = null, string? packagesRoot = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jobs:WorkspacePath"] = _workspace,
            [RecordingsCatalog.RootPathKey] = _recordings,
        };
        if (packagesRoot != null)
            settings[FileLessonPackageStore.RootPathKey] = packagesRoot;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var env = new FakeHostEnvironment();
        var workspace = new JobWorkspace(config, env, NullLogger<JobWorkspace>.Instance);
        var jobs = new WorkspaceAwareJobStore(new InMemoryJobStore(), workspace, NullLogger<WorkspaceAwareJobStore>.Instance);
        var catalog = new RecordingsCatalog(config, env, NullLogger<RecordingsCatalog>.Instance);
        var store = new FileLessonPackageStore(config, env, workspace, NullLogger<FileLessonPackageStore>.Instance);
        var controller = new LessonPackagesController(
            store, new FixedWords(words ?? LessonsRead.Nothing), catalog, NullLogger<LessonPackagesController>.Instance);
        return new Parts(controller, store, catalog, workspace, jobs, config);
    }

    private static LessonPackageView Viewed(ActionResult<LessonPackageView> result) =>
        Assert.IsType<LessonPackageView>(Assert.IsType<OkObjectResult>(result.Result).Value);

    private static LessonMarkBody Body(bool? leftOut = null, params (string Field, string? Text)[] fields) => new()
    {
        Fields = fields.Length == 0 ? null : fields.ToDictionary(f => f.Field, f => f.Text, StringComparer.Ordinal),
        LeftOut = leftOut,
    };

    // ----- the files -----

    [Fact]
    public void The_packages_folder_stands_next_to_the_jobs_folder_unless_the_key_names_another()
    {
        var other = Path.Combine(_base, "elsewhere");

        Assert.Equal(_packages, Create().Store.RootPath);
        Assert.Equal(other, Create(packagesRoot: other).Store.RootPath);
    }

    [Fact]
    public void List_names_the_lessons_that_have_a_package()
    {
        var store = Create().Store;
        Assert.Empty(store.List());

        PutPackage();
        Directory.CreateDirectory(Path.Combine(_packages, "2026-10-30"));
        Put(_packages, "notes/" + FileLessonPackageStore.PackageFileName, "{}");

        var listed = store.List();

        Assert.Equal(new[] { Day }, listed.Keys);
        Assert.False(string.IsNullOrEmpty(listed[Day].BuiltAt));
    }

    [Fact]
    public void Read_gives_the_lists_of_the_file_and_takes_a_list_written_as_null_for_an_empty_one()
    {
        PutPackage();

        var package = Create().Store.Read(Day)!;

        Assert.Equal(Day, package.Lesson);
        Assert.Equal("2026-10-24T09:00:00Z", package.BuiltAt);
        Assert.Equal("jobdialogue1", Assert.Single(package.Periods).Job);
        Assert.Equal(2, package.NewWords.Count);
        Assert.Equal("ventana", package.NewWords[0].Es);
        Assert.Equal(5.0, package.NewWords[0].Sec);
        Assert.Equal("teacher", package.NewWords[0].Who);
        Assert.Equal(12.5, Assert.Single(Assert.Single(package.Grammar).Examples).Sec);
        Assert.Empty(package.Grammar[0].Parts);
        Assert.Empty(package.WordNotes);
        Assert.Equal("Chimenea", Assert.Single(package.OtherWords).Raw);
        Assert.Null(Create().Store.Read("2026-10-30"));
    }

    [Theory]
    [InlineData("{ \"schema_version\": 2, \"new_words\": [] }")]
    [InlineData("{ not json")]
    [InlineData("null")]
    public void Read_refuses_a_file_it_cannot_take(string json)
    {
        PutPackage(json: json);

        Assert.Throws<InvalidOperationException>(() => Create().Store.Read(Day));
    }

    [Fact]
    public void An_edit_is_written_next_to_the_package_and_the_package_stays_as_built()
    {
        PutPackage();
        var before = File.ReadAllBytes(PackageFile());
        var store = Create().Store;

        var kept = store.ChangeMark(Day, "n-ventana", _ => new LessonMark
        {
            Fields = new() { ["ru"] = "окошко" },
            Was = new() { ["ru"] = "окно" },
            At = "2026-10-25T10:00:00Z",
        });

        Assert.Equal("окошко", kept!.Fields!["ru"]);
        Assert.Equal(before, File.ReadAllBytes(PackageFile()));
        var written = File.ReadAllText(MarksFile(), Encoding.UTF8);
        Assert.Contains("\"n-ventana\"", written);
        Assert.Contains("\"ru\": \"окошко\"", written);
        Assert.Contains("\"left_out\": false", written);
        Assert.False(File.Exists(MarksFile() + ".tmp"));
        Assert.Equal("окошко", Create().Store.Marks(Day)["n-ventana"].Fields!["ru"]);
    }

    [Fact]
    public void A_write_keeps_what_a_hand_added_to_the_edits()
    {
        PutPackage();
        Put(_packages, Day + "/" + FileLessonPackageStore.MarksFileName, """
            {
              "n-tejado": { "left_out": true, "why": "heard wrong" },
              "Not An Id": { "left_out": true }
            }
            """);
        var store = Create().Store;

        store.ChangeMark(Day, "n-ventana", _ => new LessonMark { LeftOut = true });

        var written = File.ReadAllText(MarksFile(), Encoding.UTF8);
        Assert.Contains("\"why\": \"heard wrong\"", written);
        Assert.Contains("\"Not An Id\"", written);
        Assert.Equal(new[] { "n-tejado", "n-ventana" }, store.Marks(Day).Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Taking_the_last_edit_off_leaves_no_edit_and_a_change_to_nothing_writes_nothing()
    {
        PutPackage();
        var store = Create().Store;

        Assert.Null(store.ChangeMark(Day, "n-ventana", _ => null));
        Assert.False(File.Exists(MarksFile()));

        store.ChangeMark(Day, "n-ventana", _ => new LessonMark { LeftOut = true });
        Assert.Null(store.ChangeMark(Day, "n-ventana", _ => new LessonMark()));
        Assert.Empty(store.Marks(Day));
    }

    [Fact]
    public void A_change_that_gives_the_entry_back_as_it_was_writes_nothing()
    {
        PutPackage();
        var marks = Put(_packages, Day + "/" + FileLessonPackageStore.MarksFileName, "{ \"n-ventana\": {\"left_out\":true} }\n\n");
        var before = File.ReadAllBytes(marks);
        var store = Create().Store;

        var kept = store.ChangeMark(Day, "n-ventana", known => known);

        Assert.True(kept!.LeftOut);
        Assert.Equal(before, File.ReadAllBytes(marks));
    }

    [Fact]
    public void Edits_that_cannot_be_read_are_never_written_over()
    {
        PutPackage();
        var marks = Put(_packages, Day + "/" + FileLessonPackageStore.MarksFileName, "{ \"n-ventana\": { broken");
        var before = File.ReadAllBytes(marks);
        var store = Create().Store;

        Assert.Throws<InvalidOperationException>(() => store.Marks(Day));
        Assert.Throws<InvalidOperationException>(() => store.ChangeMark(Day, "n-tejado", _ => new LessonMark { LeftOut = true }));
        Assert.Equal(before, File.ReadAllBytes(marks));
    }

    // ----- the API -----

    [Fact]
    public async Task Get_answers_400_for_a_bad_date_and_404_for_a_lesson_with_no_package()
    {
        var controller = Create().Controller;

        Assert.IsType<BadRequestObjectResult>((await controller.Get("23.10.2026")).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.Get(Day)).Result);
    }

    [Fact]
    public async Task Words_gives_the_forms_of_the_lessons_before_the_date_oldest_first()
    {
        var words = new LessonsRead(
            new[]
            {
                new LessonWords("2026-11-06", new[] { "El tejado es rojo." }),
                new LessonWords("2026-10-16", new[] { "Abre las ventanitas del árbol." }),
                new LessonWords("2026-11-13", new[] { "La chimenea." }),
            },
            new Dictionary<string, string>());
        var controller = Create(words).Controller;

        var answer = Assert.IsType<LessonFormsResponse>(
            Assert.IsType<OkObjectResult>((await controller.Words("2026-11-13")).Result).Value);

        Assert.Equal("2026-11-13", answer.Before);
        Assert.Equal(new[] { "2026-10-16", "2026-11-06" }, answer.Lessons.Select(l => l.Date));
        Assert.Equal(new[] { "abre", "arbol", "del", "las", "ventanitas" }, answer.Lessons[0].Forms);
        Assert.IsType<BadRequestObjectResult>((await controller.Words("13.11.2026")).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.Words(null)).Result);
    }

    [Fact]
    public async Task Get_gives_the_package_with_its_words_counted_against_the_earlier_lessons()
    {
        PutPackage();
        var words = new LessonsRead(
            new[]
            {
                new LessonWords("2026-10-16", new[] { "Abre las ventanitas." }),
                new LessonWords("2026-11-06", new[] { "El tejado es rojo." }),
            },
            new Dictionary<string, string> { ["jobdialogue1"] = "track-mixed" });

        var view = Viewed(await Create(words).Controller.Get(Day));

        Assert.Equal(Day, view.Date);
        Assert.Equal(new[] { "2026-10-16" }, view.Package.NewWords[0].SeenIn);
        Assert.Empty(view.Package.NewWords[1].SeenIn!);
        Assert.Equal("n-ventana", view.Package.NewWords[0].Id);
        Assert.Equal("track-mixed", view.Package.Periods[0].AudioTrack);
        Assert.Equal(1, view.Compared.Lessons);
        Assert.Equal(2, view.Counts.NewWords);
        Assert.Equal(1, view.Counts.NewWordsNeverSeen);
        Assert.Equal(1, view.Counts.OtherWordsNeverSeen);
    }

    [Fact]
    public async Task Get_answers_409_when_the_file_cannot_be_read()
    {
        PutPackage(json: "{ not json");

        Assert.IsType<ConflictObjectResult>((await Create().Controller.Get(Day)).Result);
    }

    [Fact]
    public async Task Put_writes_a_field_anew_and_null_puts_the_builders_text_back()
    {
        PutPackage();
        var before = File.ReadAllBytes(PackageFile());
        var controller = Create().Controller;

        var edited = Viewed(await controller.PutMark(Day, "n-ventana", Body(null, ("ru", "окошко"), ("note", "small one"))));
        var word = edited.Package.NewWords[0];
        Assert.Equal("окошко", word.Ru);
        Assert.Equal("small one", word.Note);
        Assert.Equal("окно", word.Edited!["ru"]);
        Assert.Null(word.Edited["note"]);
        Assert.Equal(1, edited.Counts.Edited);
        Assert.Contains("\"was\"", File.ReadAllText(MarksFile(), Encoding.UTF8));

        var back = Viewed(await controller.PutMark(Day, "n-ventana", Body(null, ("ru", null), ("note", null))));
        Assert.Equal("окно", back.Package.NewWords[0].Ru);
        Assert.Null(back.Package.NewWords[0].Edited);
        Assert.Equal(0, back.Counts.Edited);
        Assert.Equal(before, File.ReadAllBytes(PackageFile()));
    }

    [Fact]
    public async Task Put_edits_an_example_a_grammar_rule_and_a_topic_too()
    {
        PutPackage();
        var parts = Create();
        var package = parts.Store.Read(Day)!;
        LessonPackageViewBuilder.Items(package);
        var example = package.Examples[0].Id!;
        var grammar = package.Grammar[0].Id!;
        var grammarExample = package.Grammar[0].Examples[0].Id!;
        var topic = package.Topics[0].Id!;

        await parts.Controller.PutMark(Day, example, Body(null, ("quote", "Me gustan las ventanitas."), ("shows", "gustar, plural")));
        await parts.Controller.PutMark(Day, grammar, Body(null, ("rule", "Actions that are over")));
        await parts.Controller.PutMark(Day, grammarExample, Body(null, ("quote", "Ayer comí pan.")));
        var view = Viewed(await parts.Controller.PutMark(Day, topic, Body(null, ("title", "The weather"))));

        Assert.Equal("Me gustan las ventanitas.", view.Package.Examples[0].Quote);
        Assert.Equal("gustar, plural", view.Package.Examples[0].Shows);
        Assert.Equal("Me gusta la ventana.", view.Package.Examples[0].Edited!["quote"]);
        Assert.Equal("Actions that are over", view.Package.Grammar[0].Rule);
        Assert.Equal("Ayer comí pan.", view.Package.Grammar[0].Examples[0].Quote);
        Assert.Equal("The weather", view.Package.Topics[0].Title);
        // The id is made of the builder's text, so an edited item keeps the id it was edited by.
        Assert.Equal(example, view.Package.Examples[0].Id);
        Assert.Equal(grammar, view.Package.Grammar[0].Id);
        Assert.Equal(4, view.Counts.Edited);
        Assert.Equal(0, view.Counts.Stale);
    }

    [Fact]
    public async Task Put_takes_a_word_out_of_the_lists_and_puts_it_back()
    {
        PutPackage();
        var controller = Create().Controller;

        var taken = Viewed(await controller.PutMark(Day, "o-chimenea", Body(leftOut: true)));
        Assert.True(taken.Package.OtherWords[0].LeftOut);
        Assert.Equal(0, taken.Counts.OtherWords);
        Assert.Equal(1, taken.Counts.LeftOut);

        var back = Viewed(await controller.PutMark(Day, "o-chimenea", Body(leftOut: false)));
        Assert.False(back.Package.OtherWords[0].LeftOut);
        Assert.Equal(1, back.Counts.OtherWords);
    }

    [Fact]
    public async Task Put_refuses_what_it_cannot_take()
    {
        PutPackage();
        var controller = Create().Controller;

        Assert.IsType<BadRequestObjectResult>((await controller.PutMark("23.10", "n-ventana", Body(leftOut: true))).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.PutMark(Day, "N Ventana", Body(leftOut: true))).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.PutMark(Day, "n-ventana", null)).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.PutMark(Day, "n-ventana", new LessonMarkBody())).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.PutMark(Day, "n-ventana", Body(null, ("shows", "x")))).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.PutMark(Day, "n-ventana", Body(null, ("es", " ")))).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.PutMark(Day, "n-balcon", Body(leftOut: true))).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.PutMark("2026-10-30", "n-ventana", Body(leftOut: true))).Result);
        Assert.False(File.Exists(MarksFile()));
    }

    [Fact]
    public async Task Put_answers_409_and_leaves_the_edits_alone_when_they_cannot_be_read()
    {
        PutPackage();
        var marks = Put(_packages, Day + "/" + FileLessonPackageStore.MarksFileName, "{ broken");
        var before = File.ReadAllBytes(marks);

        var result = await Create().Controller.PutMark(Day, "n-ventana", Body(leftOut: true));

        Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal(before, File.ReadAllBytes(marks));
    }

    [Fact]
    public async Task Delete_takes_every_edit_off_one_item()
    {
        PutPackage();
        var controller = Create().Controller;
        await controller.PutMark(Day, "n-ventana", Body(true, ("ru", "окошко")));
        await controller.PutMark(Day, "n-tejado", Body(leftOut: true));

        var view = Viewed(await controller.RemoveMark(Day, "n-ventana"));

        Assert.Equal("окно", view.Package.NewWords[0].Ru);
        Assert.False(view.Package.NewWords[0].LeftOut);
        Assert.True(view.Package.NewWords[1].LeftOut);
        Assert.IsType<NotFoundObjectResult>((await controller.RemoveMark(Day, "n-ventana")).Result);
        Assert.IsType<NotFoundObjectResult>((await controller.RemoveMark("2026-10-30", "n-ventana")).Result);
        Assert.IsType<BadRequestObjectResult>((await controller.RemoveMark(Day, "../x")).Result);
    }

    [Fact]
    public async Task What_a_hand_added_to_an_edit_stays_in_the_file_when_the_edit_is_taken_off()
    {
        PutPackage();
        Put(_packages, Day + "/" + FileLessonPackageStore.MarksFileName, """
            {
              "n-ventana": { "fields": { "ru": "окошко" }, "why": "heard wrong" },
              "n-tejado": { "left_out": true, "why": "not a word" }
            }
            """);
        var controller = Create().Controller;

        var back = Viewed(await controller.PutMark(Day, "n-ventana", Body(null, ("ru", null))));
        var removed = Viewed(await controller.RemoveMark(Day, "n-tejado"));

        Assert.Equal("окно", back.Package.NewWords[0].Ru);
        Assert.Null(back.Package.NewWords[0].Edited);
        Assert.False(removed.Package.NewWords[1].LeftOut);
        Assert.Equal(0, removed.Counts.Edited);
        Assert.Equal(0, removed.Counts.LeftOut);
        Assert.Equal(0, removed.Counts.Stale);
        var written = File.ReadAllText(MarksFile(), Encoding.UTF8);
        Assert.Contains("\"why\": \"heard wrong\"", written);
        Assert.Contains("\"why\": \"not a word\"", written);
        Assert.DoesNotContain("окошко", written);

        // Nothing of the owner's is left on the item: taking the edit off again finds none and writes nothing.
        File.AppendAllText(MarksFile(), "\n\n");
        var asLeft = File.ReadAllBytes(MarksFile());
        Assert.IsType<NotFoundObjectResult>((await controller.RemoveMark(Day, "n-tejado")).Result);
        Assert.Equal(asLeft, File.ReadAllBytes(MarksFile()));
    }

    [Fact]
    public void TrackAudio_gives_the_file_of_a_track_so_that_a_part_of_it_can_be_asked_for()
    {
        var parts = Create();
        var track = parts.Catalog.Scan().Single(s => s.Date == Day).Periods.Single().Tracks.Single();

        var file = Assert.IsType<PhysicalFileResult>(parts.Controller.TrackAudio(" " + track.Id + " "));

        Assert.Equal(
            Path.GetFullPath(Path.Combine(_recordings, MixedTrack.Replace('/', Path.DirectorySeparatorChar))),
            Path.GetFullPath(file.FileName));
        Assert.Equal("audio/mp4", file.ContentType);
        Assert.True(file.EnableRangeProcessing);
        Assert.IsType<NotFoundObjectResult>(parts.Controller.TrackAudio("no-such-track"));
        Assert.IsType<NotFoundObjectResult>(parts.Controller.TrackAudio(null));
    }

    // ----- the lessons list and the words of the lessons -----

    [Fact]
    public async Task The_lessons_list_tells_which_lesson_has_a_package()
    {
        PutPackage();
        var parts = Create();
        var sessions = new SessionsController(
            parts.Catalog,
            parts.Jobs,
            parts.Workspace,
            new NoQueue(),
            new FileLessonPagesStore(parts.Workspace, NullLogger<FileLessonPagesStore>.Instance),
            parts.Store,
            parts.Config,
            NullLogger<SessionsController>.Instance);

        var listed = Assert.IsType<SessionsResponse>(Assert.IsType<OkObjectResult>((await sessions.List()).Result).Value);

        Assert.False(string.IsNullOrEmpty(listed.Sessions.Single(s => s.Date == Day).Package?.BuiltAt));
        Assert.Null(listed.Sessions.Single(s => s.Date == "2026-10-16").Package);
    }

    [Fact]
    public void The_text_of_a_job_is_its_newest_combined_transcript_and_its_newest_refined_one()
    {
        var job = Path.Combine(_workspace, "jobwithfiles1");
        var t0 = new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc);
        void PutAt(string name, int minutes) =>
            File.SetLastWriteTimeUtc(Put(_workspace, "jobwithfiles1/" + name, "- 1.00 speaker_0: \"hola\""), t0.AddMinutes(minutes));

        Assert.Null(LessonWordIndex.TextOf(job));

        PutAt("transcript.md", 0);
        PutAt("transcript_dialogue_20261024.md", 5);
        Put(_workspace, "jobwithfiles1/chunks_json/audio_part_000.json", "{}");
        var combinedOnly = LessonWordIndex.TextOf(job)!;
        Assert.Equal("transcript_dialogue_20261024.md", combinedOnly.Raw!.Name);
        Assert.Null(combinedOnly.Refined);
        Assert.Empty(combinedOnly.Answers);

        PutAt("transcript_fixed_1.md", 1);
        PutAt("transcript_fixed_2.md", 2);
        PutAt("transcript_fixed_2.prompt.md", 9);
        var both = LessonWordIndex.TextOf(job)!;
        Assert.Equal("transcript_dialogue_20261024.md", both.Raw!.Name);
        Assert.Equal("transcript_fixed_2.md", both.Refined!.Name);
        Assert.Empty(both.Answers);
    }

    [Fact]
    public void A_job_that_kept_only_a_refined_transcript_is_checked_against_its_chunk_answers()
    {
        Put(_workspace, "jobrefinedonly/transcript_fixed_1.md", "- 1.00 speaker_0: \"hola\"");
        Put(_workspace, "jobrefinedonly/chunks_json/audio_part_001.json", "{}");
        Put(_workspace, "jobrefinedonly/chunks_json/audio_part_000.json", "{}");

        var text = LessonWordIndex.TextOf(Path.Combine(_workspace, "jobrefinedonly"))!;

        Assert.Null(text.Raw);
        Assert.Equal("transcript_fixed_1.md", text.Refined!.Name);
        Assert.Equal(new[] { "audio_part_000.json", "audio_part_001.json" }, text.Answers.Select(f => f.Name));
    }

    [Fact]
    public async Task Of_a_refined_transcript_only_the_words_the_raw_one_has_are_counted()
    {
        Put(_workspace, "jobearlier01/audio1844990001.m4a", "audio of the lesson before");
        Put(_workspace, "jobearlier01/transcript.md",
            "- 1.00 speaker_0: \"Грасиас, эста бьен.\"\n- 4.00 speaker_1: \"Не могу использовать окно.\"\n- 7.00 speaker_0: \"El tejado.\"\n");
        Put(_workspace, "jobearlier01/transcript_fixed_1.md",
            "- 1.00 speaker_0: \"Gracias, está bien.\"\n- 4.00 speaker_1: \"No puedo utilizar la ventana.\"\n- 7.00 speaker_0: \"El tejado.\"\n");
        var parts = Create();
        var index = new LessonWordIndex(parts.Catalog, parts.Jobs, parts.Workspace, NullLogger<LessonWordIndex>.Instance);

        var lesson = Assert.Single((await index.ReadAsync()).Lessons);

        // Spanish the recogniser wrote in Cyrillic and the refine respelled: said.
        Assert.Contains("gracias", lesson.Forms);
        Assert.Contains("bien", lesson.Forms);
        Assert.Contains("tejado", lesson.Forms);
        // Russian speech the refine wrote in Spanish: not said.
        Assert.DoesNotContain("puedo", lesson.Forms);
        Assert.DoesNotContain("utilizar", lesson.Forms);
        Assert.DoesNotContain("ventana", lesson.Forms);
    }

    [Fact]
    public async Task The_chunk_answers_stand_in_for_a_raw_transcript_that_was_not_kept()
    {
        Put(_workspace, "jobearlier01/audio1844990001.m4a", "audio of the lesson before");
        Put(_workspace, "jobearlier01/transcript_fixed_1.md",
            "- 1.00 speaker_0: \"Muchas gracias.\"\n- 4.00 speaker_1: \"No puedo utilizar la ventana.\"\n");
        Put(_workspace, "jobearlier01/chunks_json/audio_part_000.json",
            """{ "segments": [ { "text": "Мучас грасиас.", "start": 1.0, "end": 2.0 }, { "text": "Не могу использовать окно.", "start": 4.0, "end": 6.0 } ] }""");
        var parts = Create();
        var index = new LessonWordIndex(parts.Catalog, parts.Jobs, parts.Workspace, NullLogger<LessonWordIndex>.Instance);

        var lesson = Assert.Single((await index.ReadAsync()).Lessons);

        Assert.Contains("muchas", lesson.Forms);
        Assert.Contains("gracias", lesson.Forms);
        Assert.DoesNotContain("utilizar", lesson.Forms);
        Assert.DoesNotContain("ventana", lesson.Forms);
    }

    [Fact]
    public async Task A_lesson_no_job_transcribed_is_read_from_the_answer_files_of_its_day_folder()
    {
        // A day of flat recordings, before the pipeline: the answer of another service lies next to the audio.
        Put(_recordings, "2025.09.03/Voice 250903_190328.m4a", "old audio");
        Put(_recordings, "2025.09.03/Voice 250903_190328.m4a.json",
            """{ "language_code": null, "segments": [ { "text": "Hoy hablamos del tejado.", "start_time": 1.0, "end_time": 3.0, "words": [] }, { "text": "Да, хорошо.", "start_time": 3.5, "end_time": 4.0, "words": [] } ] }""");
        Put(_recordings, "2025.09.03/2025-09-03 response.json", """{ "chunks": [ { "response": { "text": "chimenea" } } ] }""");
        Put(_recordings, "2025.09.03/notes.json", "[1, 2, 3]");
        Put(_recordings, "2025.09.03/obsidian/media/data.json", """{ "segments": [ { "text": "ventanal" } ] }""");
        var parts = Create();
        var index = new LessonWordIndex(parts.Catalog, parts.Jobs, parts.Workspace, NullLogger<LessonWordIndex>.Instance);

        var lesson = Assert.Single((await index.ReadAsync()).Lessons);

        Assert.Equal("2025-09-03", lesson.Date);
        Assert.Contains("hablamos", lesson.Forms);
        Assert.Contains("tejado", lesson.Forms);
        Assert.DoesNotContain("chimenea", lesson.Forms);
        Assert.DoesNotContain("ventanal", lesson.Forms);
    }

    [Fact]
    public async Task The_answer_files_of_a_day_folder_are_not_read_when_a_job_transcribed_one_of_its_recordings()
    {
        Put(_recordings, "2026.10.16/old answer.json", """{ "segments": [ { "text": "Una chimenea." } ] }""");
        Put(_workspace, "jobearlier01/audio1844990001.m4a", "audio of the lesson before");
        Put(_workspace, "jobearlier01/transcript.md", "- 1.00 speaker_0: \"El tejado.\"\n");
        var parts = Create();
        var index = new LessonWordIndex(parts.Catalog, parts.Jobs, parts.Workspace, NullLogger<LessonWordIndex>.Instance);

        var lesson = Assert.Single((await index.ReadAsync()).Lessons);

        Assert.Contains("tejado", lesson.Forms);
        Assert.DoesNotContain("chimenea", lesson.Forms);
    }

    [Fact]
    public async Task The_words_of_a_lesson_are_read_from_the_transcript_of_the_job_that_transcribed_its_track()
    {
        // A job made by a plain upload of the mixed file of the earlier lesson: tied by the name and size of the audio.
        Put(_workspace, "jobearlier01/audio1844990001.m4a", "audio of the lesson before");
        var transcript = Put(_workspace, "jobearlier01/transcript.md",
            "- 1.00 speaker_0: \"Abre las ventanitas.\"\n- 4.00 speaker_1: \"Да, сейчас. El tejado.\"\n");
        var parts = Create();
        var index = new LessonWordIndex(parts.Catalog, parts.Jobs, parts.Workspace, NullLogger<LessonWordIndex>.Instance);

        var read = await index.ReadAsync();

        var lesson = Assert.Single(read.Lessons);
        Assert.Equal("2026-10-16", lesson.Date);
        Assert.Contains("ventanitas", lesson.Forms);
        Assert.Contains("tejado", lesson.Forms);
        Assert.DoesNotContain("сейчас", lesson.Forms);
        var earlierTrack = parts.Catalog.Scan().Single(s => s.Date == "2026-10-16").Periods.Single().Tracks.Single();
        Assert.Equal(earlierTrack.Id, read.MixedTrackByJob["jobearlier01"]);

        // The transcript changed: the lesson is read anew the next time.
        File.WriteAllText(transcript, "- 1.00 speaker_0: \"Una chimenea grande.\"\n", new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(transcript, DateTime.UtcNow.AddMinutes(1));

        var again = await index.ReadAsync();

        Assert.Contains("chimenea", Assert.Single(again.Lessons).Forms);
        Assert.DoesNotContain("ventanitas", again.Lessons[0].Forms);
    }

    private sealed class FixedWords(LessonsRead read) : ILessonWordIndex
    {
        public Task<LessonsRead> ReadAsync(CancellationToken ct = default) => Task.FromResult(read);
    }

    private sealed class NoQueue : IPipelineQueue
    {
        public void Enqueue(string jobId) { }

        public void EnqueueRefiner(string jobId, string? inputFile = null, bool lessonPrompt = false) { }
    }
}
