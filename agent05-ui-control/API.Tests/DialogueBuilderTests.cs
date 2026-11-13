using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Controllers;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

public sealed class DialogueBuilderTests : IDisposable
{
    private const string TeacherJob = "teacher00001";
    private const string StudentJob = "student00001";

    private const string TeacherTranscript =
        ">>>>>>>\n" +
        "- 8.66 speaker_0: \"Ага,\"\n" +
        "- 17.36 speaker_0: \"entonces,\"\n" +
        "- 160.10 speaker_1: \"la cama de matrimonio\"\n" +
        "<<<<<\n";

    private const string StudentTranscript =
        ">>>>>>>\n" +
        "- 0.00 speaker_0: \"Olha.\n" +
        "Hm.\"\n" +
        "- 161.75 speaker_0: \"донде, но сейчас demasiado dinero.\"\n" +
        "<<<<<\n";

    private readonly string _root;
    private readonly JobWorkspace _workspace;
    private readonly IJobStore _store;
    private readonly DialogueBuilder _builder;

    public DialogueBuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "dialogue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _workspace = NewWorkspace();
        _store = NewStore();
        _builder = new DialogueBuilder(_store, _workspace, NullLogger<DialogueBuilder>.Instance);

        WriteJobFile(TeacherJob, "transcript_aaa.md", TeacherTranscript);
        WriteJobFile(StudentJob, "transcript_bbb.md", StudentTranscript);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best-effort */ }
    }

    private JobWorkspace NewWorkspace()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:WorkspacePath"] = _root })
            .Build();
        return new JobWorkspace(config, new FakeHostEnvironment { ContentRootPath = Path.GetTempPath() }, NullLogger<JobWorkspace>.Instance);
    }

    private IJobStore NewStore() =>
        new WorkspaceAwareJobStore(new InMemoryJobStore(), _workspace, NullLogger<WorkspaceAwareJobStore>.Instance);

    private string WriteJobFile(string jobId, string relativePath, string content)
    {
        var path = Path.Combine(_root, jobId, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static DialogueCreateRequest TwoTracks(string? title = "Урок 23.10 — диалог") => new(title, new[]
    {
        new DialogueTrackRequest(TeacherJob, "Преподаватель"),
        new DialogueTrackRequest(StudentJob, "Ученик"),
    });

    private string ReadDialogueFile(DialogueBuildResult result) =>
        File.ReadAllText(Path.Combine(_root, result.JobId, result.File), Encoding.UTF8);

    [Fact]
    public async Task Create_writes_the_merged_transcript_into_a_new_job_that_waits_for_the_refiner()
    {
        var result = await _builder.CreateAsync(TwoTracks());

        Assert.StartsWith("transcript_dialogue_", result.File);
        Assert.DoesNotContain("_fixed", result.File);
        Assert.Equal(5, result.Entries);
        Assert.Equal(
            ">>>>>>>\n" +
            "- 0.00 Ученик: \"Olha.\n" +
            "Hm.\"\n" +
            "- 8.66 Преподаватель: \"Ага,\"\n" +
            "- 17.36 Преподаватель: \"entonces,\"\n" +
            "- 160.10 Преподаватель: \"la cama de matrimonio\"\n" +
            "- 161.75 Ученик: \"донде, но сейчас demasiado dinero.\"\n" +
            "<<<<<\n",
            ReadDialogueFile(result));

        var snap = await _store.GetAsync(result.JobId);
        Assert.NotNull(snap);
        Assert.Equal("awaiting_refiner", snap!.Phase);
        Assert.Equal("done", snap.Status);
        Assert.Equal("Урок 23.10 — диалог", snap.OriginalFilename);
        Assert.Contains(DialogueBuilder.DialogueTag, snap.Tags ?? Array.Empty<string>());

        var manifest = DialogueManifestFile.TryLoad(Path.Combine(_root, result.JobId));
        Assert.NotNull(manifest);
        Assert.Equal(new[] { TeacherJob, StudentJob }, manifest!.Tracks.Select(t => t.JobId));
        Assert.Equal(new[] { "Преподаватель", "Ученик" }, manifest.Tracks.Select(t => t.Speaker));
        var merge = Assert.Single(manifest.Merges);
        Assert.Equal(result.File, merge.File);
        Assert.Equal(new[] { "transcript_aaa.md", "transcript_bbb.md" }, merge.Tracks.Select(t => t.File));
        Assert.Equal(new[] { 3, 2 }, merge.Tracks.Select(t => t.Entries));
    }

    [Fact]
    public async Task Create_leaves_the_track_jobs_untouched()
    {
        var before = SnapshotFolder(TeacherJob).Concat(SnapshotFolder(StudentJob)).ToList();

        await _builder.CreateAsync(TwoTracks());

        Assert.Equal(before, SnapshotFolder(TeacherJob).Concat(SnapshotFolder(StudentJob)).ToList());
    }

    private IEnumerable<string> SnapshotFolder(string jobId) =>
        Directory.EnumerateFiles(Path.Combine(_root, jobId), "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => p + "|" + File.GetLastWriteTimeUtc(p).Ticks + "|" + File.ReadAllText(p));

    [Fact]
    public async Task Create_labels_the_line_of_a_chunk_that_has_no_time_marks()
    {
        // Chunk 0 of the student fell back to a plain model: text only, no segments.
        WriteJobFile(StudentJob, "chunks_json/student_part_000.json", "{\"text\":\"Olha. Hm.\",\"usage\":{}}");
        WriteJobFile(StudentJob, "chunks_md/student_part_000.md", ">>>>>>>\n- 0.00 speaker_0: \"Olha.\nHm.\"\n<<<<<\n");
        WriteJobFile(StudentJob, "chunks_json/student_part_001.json",
            "{\"text\":\"донде\",\"segments\":[{\"start\":4.75,\"end\":7.0,\"text\":\"донде\",\"speaker\":\"A\"}]}");
        WriteJobFile(StudentJob, "chunks_md/student_part_001.md", ">>>>>>>\n- 161.75 speaker_0: \"донде, но сейчас demasiado dinero.\"\n<<<<<\n");

        var result = await _builder.CreateAsync(TwoTracks());

        var text = ReadDialogueFile(result);
        Assert.Contains("- 0.00 Ученик (без времени): \"Olha.\nHm.\"\n", text);
        Assert.Contains("- 161.75 Ученик: \"донде", text);
        Assert.Equal(1, result.Tracks.Single(t => t.JobId == StudentJob).UntimedEntries);
        Assert.Equal(0, result.Tracks.Single(t => t.JobId == TeacherJob).UntimedEntries);
        Assert.Contains(result.Warnings, w => w.Contains("without time marks") && w.Contains("Ученик"));
    }

    [Fact]
    public async Task Create_takes_the_newest_combined_transcript_and_skips_refiner_outputs()
    {
        var older = WriteJobFile(TeacherJob, "transcript_aaa.md", TeacherTranscript);
        File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddHours(-2));
        var newer = WriteJobFile(TeacherJob, "transcript_20261113_210000_000.md", ">>>>>>>\n- 9.00 speaker_0: \"rebuilt\"\n<<<<<\n");
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddHours(-1));
        WriteJobFile(TeacherJob, "transcript_fixed_20261113_220000_000.md", ">>>>>>>\n- 9.00 speaker_0: \"refined\"\n<<<<<\n");

        var result = await _builder.CreateAsync(TwoTracks());

        Assert.Equal("transcript_20261113_210000_000.md", result.Tracks[0].File);
        Assert.Contains("- 9.00 Преподаватель: \"rebuilt\"\n", ReadDialogueFile(result));
    }

    [Fact]
    public async Task Create_reads_the_file_named_for_a_track_even_a_refined_one()
    {
        WriteJobFile(TeacherJob, "transcript_fixed_1.md", ">>>>>>>\n- 9.00 speaker_0: \"refined\"\n<<<<<\n");

        var result = await _builder.CreateAsync(new DialogueCreateRequest(null, new[]
        {
            new DialogueTrackRequest(TeacherJob, "Преподаватель", "transcript_fixed_1.md"),
            new DialogueTrackRequest(StudentJob, "Ученик"),
        }));

        Assert.Contains("- 9.00 Преподаватель: \"refined\"\n", ReadDialogueFile(result));
        var snap = await _store.GetAsync(result.JobId);
        Assert.Equal("Dialogue: Преподаватель / Ученик", snap!.OriginalFilename);
    }

    [Fact]
    public async Task Create_rejects_bad_requests_and_creates_no_job()
    {
        var foldersBefore = Directory.GetDirectories(_root).Length;

        async Task<DialogueRequestException> Fails(params DialogueTrackRequest[] tracks) =>
            await Assert.ThrowsAsync<DialogueRequestException>(() => _builder.CreateAsync(new DialogueCreateRequest("t", tracks)));

        Assert.False((await Fails(new DialogueTrackRequest(TeacherJob, "A"))).NotFound);
        Assert.False((await Fails(new DialogueTrackRequest(TeacherJob, "A"), new DialogueTrackRequest(StudentJob, "a"))).NotFound);
        Assert.False((await Fails(new DialogueTrackRequest(TeacherJob, "A:B"), new DialogueTrackRequest(StudentJob, "C"))).NotFound);
        Assert.False((await Fails(new DialogueTrackRequest(TeacherJob, ""), new DialogueTrackRequest(StudentJob, "C"))).NotFound);
        Assert.False((await Fails(new DialogueTrackRequest("../" + TeacherJob, "A"), new DialogueTrackRequest(StudentJob, "C"))).NotFound);
        Assert.False((await Fails(new DialogueTrackRequest(TeacherJob, "A", "../" + StudentJob + "/transcript_bbb.md"), new DialogueTrackRequest(StudentJob, "C"))).NotFound);
        Assert.False((await Fails(new DialogueTrackRequest(TeacherJob, "A", "missing.md"), new DialogueTrackRequest(StudentJob, "C"))).NotFound);
        Assert.True((await Fails(new DialogueTrackRequest(TeacherJob, "A"), new DialogueTrackRequest("nosuchjob", "C"))).NotFound);

        Directory.CreateDirectory(Path.Combine(_root, "emptyjob"));
        Assert.False((await Fails(new DialogueTrackRequest(TeacherJob, "A"), new DialogueTrackRequest("emptyjob", "C"))).NotFound);
        WriteJobFile("notranscript", "transcript_x.md", "just text\n");
        Assert.False((await Fails(new DialogueTrackRequest(TeacherJob, "A"), new DialogueTrackRequest("notranscript", "C"))).NotFound);

        // Only the two folders this test made itself were added.
        Assert.Equal(foldersBefore + 2, Directory.GetDirectories(_root).Length);
        Assert.Empty(await new InMemoryJobStoreProbe(_store).ListDialoguesAsync());
    }

    [Fact]
    public async Task Rebuild_writes_a_new_file_and_keeps_an_edited_earlier_merge()
    {
        var first = await _builder.CreateAsync(TwoTracks());
        var firstPath = Path.Combine(_root, first.JobId, first.File);
        File.WriteAllText(firstPath, "edited by hand\n");

        // The student track was transcribed again: a newer combined transcript appears in its job.
        var newer = WriteJobFile(StudentJob, "transcript_20261114_100000_000.md", ">>>>>>>\n- 3.20 speaker_0: \"Hola.\"\n<<<<<\n");
        File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddMinutes(5));

        var second = await _builder.RebuildAsync(first.JobId);

        Assert.Equal(first.JobId, second.JobId);
        Assert.NotEqual(first.File, second.File);
        Assert.Equal("edited by hand\n", File.ReadAllText(firstPath));
        Assert.Contains("- 3.20 Ученик: \"Hola.\"\n", ReadDialogueFile(second));
        Assert.Equal(4, second.Entries);

        var manifest = DialogueManifestFile.TryLoad(Path.Combine(_root, first.JobId));
        Assert.Equal(new[] { first.File, second.File }, manifest!.Merges.Select(m => m.File));
    }

    [Fact]
    public async Task Rebuild_refuses_a_job_that_is_not_a_dialogue()
    {
        var notDialogue = await Assert.ThrowsAsync<DialogueRequestException>(() => _builder.RebuildAsync(TeacherJob));
        Assert.False(notDialogue.NotFound);
        Assert.Contains("not a dialogue", notDialogue.Message);

        var missing = await Assert.ThrowsAsync<DialogueRequestException>(() => _builder.RebuildAsync("nosuchjob"));
        Assert.True(missing.NotFound);
    }

    [Fact]
    public async Task Dialogue_keeps_its_title_and_phase_after_a_restart()
    {
        var result = await _builder.CreateAsync(TwoTracks());

        // A fresh in-memory store sees the dialogue only as a folder on disk.
        var afterRestart = NewStore();
        var snap = await afterRestart.GetAsync(result.JobId);
        Assert.NotNull(snap);
        Assert.Equal("Урок 23.10 — диалог", snap!.OriginalFilename);
        Assert.Equal("awaiting_refiner", snap.Phase);
        Assert.Equal("done", snap.Status);

        var list = await afterRestart.ListAsync(new JobListFilter(null, null, null, null, 100, 0));
        Assert.Equal("Урок 23.10 — диалог", list.Single(j => j.Id == result.JobId).OriginalFilename);
    }

    [Fact]
    public async Task Controller_maps_results_and_errors_to_http()
    {
        var controller = new DialogueController(_builder, NullLogger<DialogueController>.Instance);

        var created = await controller.Create(new DialogueCreateBody
        {
            Title = "t",
            Tracks = new List<DialogueTrackBody>
            {
                new() { JobId = TeacherJob, Speaker = "Преподаватель" },
                new() { JobId = StudentJob, Speaker = "Ученик" },
            }
        });
        var createdResult = Assert.IsType<CreatedResult>(created);
        var payload = Assert.IsType<DialogueBuildResult>(createdResult.Value);
        Assert.Equal("/api/jobs/" + payload.JobId, createdResult.Location);

        Assert.IsType<OkObjectResult>(await controller.Rebuild(payload.JobId));
        Assert.IsType<BadRequestObjectResult>(await controller.Create(null));
        Assert.IsType<BadRequestObjectResult>(await controller.Rebuild(TeacherJob));
        Assert.IsType<NotFoundObjectResult>(await controller.Rebuild("nosuchjob"));
        Assert.IsType<NotFoundObjectResult>(await controller.Create(new DialogueCreateBody
        {
            Tracks = new List<DialogueTrackBody>
            {
                new() { JobId = TeacherJob, Speaker = "A" },
                new() { JobId = "nosuchjob", Speaker = "B" },
            }
        }));
    }

    /// <summary>Lists the jobs the store holds in memory that carry the dialogue tag.</summary>
    private sealed class InMemoryJobStoreProbe
    {
        private readonly IJobStore _store;

        public InMemoryJobStoreProbe(IJobStore store) => _store = store;

        public async Task<IReadOnlyList<JobListItem>> ListDialoguesAsync() =>
            await _store.ListAsync(new JobListFilter(DialogueBuilder.DialogueTag, null, null, null, 100, 0));
    }
}
