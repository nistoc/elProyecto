using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Controllers;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using XtractManager.Features.LessonNotes.Application;
using XtractManager.Features.LessonNotes.Infrastructure;
using XtractManager.Features.LessonPackages.Infrastructure;
using XtractManager.Features.Sessions.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>
/// The notes of a lesson from the vault copy in its day folder: what each note is, which file a path may name,
/// the pictures a note shows, and the count in the lessons list. All names and texts here are made up.
/// </summary>
public sealed class LessonNotesTests : IDisposable
{
    private const string Day = "2026-03-04";
    private const string DayFolder = "2026.03.04 Clase";
    private const string Period = DayFolder + "/2026-03-04 19.00.10 Ana Ruiz's Zoom Meeting";
    private const string OtherDayFolder = "2026.03.11";

    private readonly string _base = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "notes_" + Guid.NewGuid().ToString("N"));
    private readonly string _recordings;
    private readonly string _workspace;

    public LessonNotesTests()
    {
        _recordings = Path.Combine(_base, "recordings");
        _workspace = Path.Combine(_base, "runtime");
        Directory.CreateDirectory(_workspace);
        Put(Period + "/audio1844990042.m4a", "mixed audio bytes");
        Put(DayFolder + "/obsidian/2026.03.04 la clase - tarea.md", "# Tarea\n\n- escribir *cinco* frases\n");
        Put(DayFolder + "/obsidian/2026.03.04 конспект урока.md", "## Temas\n\n| es | ru |\n|---|---|\n| casa | дом |\n");
        Put(DayFolder + "/obsidian/canvas/2026.03.04 Aux 4 — ДЗ + подготовка.md", "1. leer\n2. contar\n");
        Put(DayFolder + "/obsidian/.obsidian/workspace.md", "the vault's own settings");
        Put(DayFolder + "/obsidian/media/pizarra.png", "png bytes");
        Put(DayFolder + "/obsidian/media/script.js", "not a picture");
        Put(DayFolder + "/notes outside the vault.md", "a note of the day folder, not of the vault copy");
        Put(OtherDayFolder + "/obsidian/2026.03.11 la tarea.md", "# Otra tarea\n");
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, true); } catch { /* ignore */ }
    }

    private void Put(string relativePath, string content)
    {
        var full = Path.Combine(_recordings, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
    }

    private (LessonNotesReader Reader, LessonNotesController Controller, SessionsController Sessions) Create()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jobs:WorkspacePath"] = _workspace,
            [RecordingsCatalog.RootPathKey] = _recordings,
        }).Build();
        var env = new FakeHostEnvironment();
        var workspace = new JobWorkspace(config, env, NullLogger<JobWorkspace>.Instance);
        var jobs = new WorkspaceAwareJobStore(new InMemoryJobStore(), workspace, NullLogger<WorkspaceAwareJobStore>.Instance);
        var catalog = new RecordingsCatalog(config, env, NullLogger<RecordingsCatalog>.Instance);
        var reader = new LessonNotesReader(catalog);
        var sessions = new SessionsController(
            catalog,
            jobs,
            workspace,
            new NoQueue(),
            new FileLessonPagesStore(workspace, NullLogger<FileLessonPagesStore>.Instance),
            new FileLessonPackageStore(config, env, workspace, NullLogger<FileLessonPackageStore>.Instance),
            config,
            NullLogger<SessionsController>.Instance,
            reader);
        return (reader, new LessonNotesController(reader, catalog), sessions);
    }

    [Theory]
    [InlineData("2030.01.02 - la clase - tarea", LessonNoteKinds.Homework)]
    [InlineData("2030.01.09 hoja 2 las tareas - comida", LessonNoteKinds.Homework)]
    [InlineData("2030.01.16 Часть 3 — ДЗ + повторение", LessonNoteKinds.Homework)]
    [InlineData("2030.01.23 урок - Домашнее задание", LessonNoteKinds.Homework)]
    [InlineData("2030.01.30 сам - выполнение упражнений", LessonNoteKinds.Homework)]
    [InlineData("2030.02.06 Конспект урока", LessonNoteKinds.Summary)]
    [InlineData("2030.02.13 - anki palabras", LessonNoteKinds.Cards)]
    [InlineData("2030.02.13 - log", LessonNoteKinds.Log)]
    [InlineData("2030.02.20 Конспект — Dialogos", LessonNoteKinds.Summary)]
    [InlineData("2030.02.20 Dialogos", LessonNoteKinds.Dialogues)]
    [InlineData("Расшифровка урока", LessonNoteKinds.Transcript)]
    [InlineData("подзадача и catalogo", LessonNoteKinds.Other)]
    public void A_note_is_told_by_its_name(string name, string kind) =>
        Assert.Equal(kind, LessonNoteKinds.Of(name));

    [Fact]
    public void The_notes_of_a_lesson_are_those_of_the_vault_copy_in_its_day_folder_homework_first()
    {
        var notes = Create().Reader.List(Day);

        Assert.Equal(
            new[]
            {
                (DayFolder + "/obsidian/canvas/2026.03.04 Aux 4 — ДЗ + подготовка.md", LessonNoteKinds.Homework),
                (DayFolder + "/obsidian/2026.03.04 la clase - tarea.md", LessonNoteKinds.Homework),
                (DayFolder + "/obsidian/2026.03.04 конспект урока.md", LessonNoteKinds.Summary),
            },
            notes.Select(n => (n.Path, n.Kind)).ToArray());
        Assert.Equal("2026.03.04 la clase - tarea", notes[1].Title);
        Assert.True(notes[1].SizeBytes > 0);
        Assert.Empty(Create().Reader.List("2026-03-18"));
    }

    [Fact]
    public void The_lessons_list_tells_how_many_notes_and_homework_a_lesson_has()
    {
        var parts = Create();
        Assert.Equal(new LessonNotesInfo(3, 2), parts.Reader.Infos()[Day]);
        Assert.Equal(new LessonNotesInfo(1, 1), parts.Reader.Infos()["2026-03-11"]);

        var listed = Assert.IsType<SessionsResponse>(Assert.IsType<OkObjectResult>(parts.Sessions.List().Result.Result).Value);
        Assert.Equal(new LessonNotesInfo(3, 2), listed.Sessions.Single(s => s.Date == Day).Notes);
    }

    [Fact]
    public void The_text_of_a_note_is_given_by_its_path()
    {
        var controller = Create().Controller;

        var text = Assert.IsType<ContentResult>(controller.Text(Day, DayFolder + "/obsidian/2026.03.04 la clase - tarea.md"));
        Assert.StartsWith("text/markdown", text.ContentType);
        Assert.Contains("escribir *cinco* frases", text.Content);
    }

    [Theory]
    [InlineData(OtherDayFolder + "/obsidian/2026.03.11 la tarea.md")] // a note of another lesson
    [InlineData(DayFolder + "/notes outside the vault.md")] // not in the vault copy
    [InlineData(DayFolder + "/obsidian/.obsidian/workspace.md")] // the vault's own settings
    [InlineData(DayFolder + "/obsidian/../notes outside the vault.md")] // out of the folder
    [InlineData(DayFolder + "/obsidian/media/script.js")] // not a note
    [InlineData(DayFolder + "/obsidian/no such note.md")]
    [InlineData("")]
    [InlineData(null)]
    public void A_path_that_is_not_a_note_of_the_lesson_is_not_read(string? path) =>
        Assert.IsType<NotFoundObjectResult>(Create().Controller.Text(Day, path));

    [Fact]
    public void A_picture_a_note_shows_is_found_by_its_name_in_the_vault_copy()
    {
        var controller = Create().Controller;

        var file = Assert.IsType<PhysicalFileResult>(controller.EmbeddedFile(Day, "pizarra.png"));
        Assert.Equal("image/png", file.ContentType);
        Assert.True(file.EnableRangeProcessing);

        Assert.IsType<NotFoundObjectResult>(controller.EmbeddedFile(Day, "script.js"));
        Assert.IsType<NotFoundObjectResult>(controller.EmbeddedFile(Day, "media/pizarra.png"));
        Assert.IsType<NotFoundObjectResult>(controller.EmbeddedFile(Day, "..\\pizarra.png"));
        Assert.IsType<NotFoundObjectResult>(controller.EmbeddedFile("2026-03-11", "pizarra.png"));
    }

    [Fact]
    public void A_date_that_is_not_one_is_refused()
    {
        var controller = Create().Controller;
        Assert.IsType<BadRequestObjectResult>(controller.List("04.03.2026").Result);
        Assert.IsType<BadRequestObjectResult>(controller.Text("2026-3-4", "x.md"));
    }

    private sealed class NoQueue : IPipelineQueue
    {
        public void Enqueue(string jobId) { }

        public void EnqueueRefiner(string jobId, string? inputFile = null, bool lessonPrompt = false) { }
    }
}
