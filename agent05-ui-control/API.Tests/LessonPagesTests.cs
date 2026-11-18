using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Controllers;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using XtractManager.Features.Sessions.Application;
using XtractManager.Features.Sessions.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>
/// The pages made of a lesson (the lesson package): their addresses are put by lesson date, kept in a file
/// of the jobs folder and shown with the lesson. All names and addresses here are made up.
/// </summary>
public sealed class LessonPagesTests : IDisposable
{
    private const string Lesson = "2026-10-23";
    private const string Period = "2026.10.23/2026-10-23 19.01.16 Maria Lopez's Zoom Meeting";
    private const string OtherPeriod = "2026.10.30 verbs/2026-10-30 19.02.00 Maria Lopez's Zoom Meeting";
    private const string PageA = "https://pages.example.org/lesson/a1";
    private const string PageB = "https://pages.example.org/lesson/b2";

    private readonly string _base = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "pages_" + Guid.NewGuid().ToString("N"));
    private readonly string _recordings;
    private readonly string _workspace;

    public LessonPagesTests()
    {
        _recordings = Path.Combine(_base, "recordings");
        _workspace = Path.Combine(_base, "runtime");
        Directory.CreateDirectory(_recordings);
        Directory.CreateDirectory(_workspace);
        PutRecording(Period + "/audio1844996676.m4a", "mixed audio bytes");
        PutRecording(OtherPeriod + "/audio1844990000.m4a", "another lesson");
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, true); } catch { /* ignore */ }
    }

    private void PutRecording(string relativePath, string content)
    {
        var full = Path.Combine(_recordings, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
    }

    private string PagesFile => Path.Combine(_workspace, FileLessonPagesStore.FileName);

    private (SessionsController Controller, IJobStore Store) Create(bool recordingsConfigured = true)
    {
        var settings = new Dictionary<string, string?> { ["Jobs:WorkspacePath"] = _workspace };
        if (recordingsConfigured)
            settings[RecordingsCatalog.RootPathKey] = _recordings;
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var env = new FakeHostEnvironment();
        var workspace = new JobWorkspace(config, env, NullLogger<JobWorkspace>.Instance);
        var store = new WorkspaceAwareJobStore(new InMemoryJobStore(), workspace, NullLogger<WorkspaceAwareJobStore>.Instance);
        var controller = new SessionsController(
            new RecordingsCatalog(config, env, NullLogger<RecordingsCatalog>.Instance),
            store,
            workspace,
            new NoQueue(),
            new FileLessonPagesStore(workspace, NullLogger<FileLessonPagesStore>.Instance),
            new XtractManager.Features.LessonPackages.Infrastructure.FileLessonPackageStore(
                config, env, workspace,
                NullLogger<XtractManager.Features.LessonPackages.Infrastructure.FileLessonPackageStore>.Instance),
            config,
            NullLogger<SessionsController>.Instance);
        return (controller, store);
    }

    private static LessonPageBody Body(string? url, string? title = null) => new() { Url = url, Title = title };

    private static IReadOnlyList<LessonPage> Put(ActionResult<LessonPagesResponse> result) =>
        Assert.IsType<LessonPagesResponse>(Assert.IsType<OkObjectResult>(result.Result).Value).Pages;

    private static async Task<IReadOnlyList<RecordingSession>> ListedAsync(SessionsController controller) =>
        Assert.IsType<SessionsResponse>(Assert.IsType<OkObjectResult>((await controller.List()).Result).Value).Sessions;

    private List<string> RecordingsSnapshot() =>
        Directory.EnumerateFileSystemEntries(_recordings, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => File.Exists(p)
                ? $"{p}|{new FileInfo(p).Length}|{File.GetLastWriteTimeUtc(p).Ticks}"
                : p)
            .ToList();

    [Fact]
    public async Task A_lesson_without_a_page_lists_none()
    {
        var (controller, _) = Create();

        Assert.All(await ListedAsync(controller), s => Assert.Empty(s.Pages));
        Assert.False(File.Exists(PagesFile));
    }

    [Fact]
    public async Task A_page_put_for_a_date_is_shown_with_the_lesson_of_that_date_only()
    {
        var before = RecordingsSnapshot();
        var (controller, store) = Create();

        var put = Put(controller.PutPage(Lesson, Body(" " + PageA + " ", "  Lesson package  ")));

        var page = Assert.Single(put);
        Assert.Equal(PageA, page.Url);
        Assert.Equal("Lesson package", page.Title);
        Assert.NotNull(page.AddedAt);

        var sessions = await ListedAsync(controller);
        Assert.Equal(new[] { PageA }, sessions.Single(s => s.Date == Lesson).Pages.Select(p => p.Url));
        Assert.Empty(sessions.Single(s => s.Date == "2026-10-30").Pages);

        // The addresses are kept in the jobs folder, as a file: the recordings folder is only read,
        // and the jobs list does not take the file for a job.
        Assert.True(File.Exists(PagesFile));
        Assert.Equal(before, RecordingsSnapshot());
        Assert.Empty(await store.ListAsync(new JobListFilter(Limit: int.MaxValue)));
    }

    [Fact]
    public void The_page_put_last_comes_first_and_one_put_again_is_only_renamed()
    {
        var (controller, _) = Create();

        Put(controller.PutPage(Lesson, Body(PageA, "first form")));
        Put(controller.PutPage(Lesson, Body(PageB)));
        var pages = Put(controller.PutPage(Lesson, Body(PageA, "first form, kept")));

        Assert.Equal(new[] { PageB, PageA }, pages.Select(p => p.Url));
        Assert.Null(pages[0].Title);
        Assert.Equal("first form, kept", pages[1].Title);
    }

    [Fact]
    public void The_pages_outlive_the_process_that_put_them()
    {
        var (first, _) = Create();
        Put(first.PutPage(Lesson, Body(PageA, "Пакет урока")));

        var (second, _) = Create();
        var pages = Put(second.PutPage(Lesson, Body(PageB)));

        Assert.Equal(new[] { PageB, PageA }, pages.Select(p => p.Url));
        Assert.Equal("Пакет урока", pages[1].Title);
        Assert.Contains("\"schema_version\": 1", File.ReadAllText(PagesFile));
        Assert.Contains("Пакет урока", File.ReadAllText(PagesFile));
    }

    [Fact]
    public async Task A_removed_page_is_gone_and_the_others_stay()
    {
        var (controller, _) = Create();
        Put(controller.PutPage(Lesson, Body(PageA)));
        Put(controller.PutPage(Lesson, Body(PageB)));

        var left = Put(controller.RemovePage(Lesson, PageB));

        Assert.Equal(new[] { PageA }, left.Select(p => p.Url));
        Assert.IsType<NotFoundObjectResult>(controller.RemovePage(Lesson, PageB).Result);
        Assert.Empty(Put(controller.RemovePage(Lesson, PageA)));
        Assert.All(await ListedAsync(controller), s => Assert.Empty(s.Pages));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("pages.example.org/lesson")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/lesson.html")]
    [InlineData("ftp://pages.example.org/lesson")]
    [InlineData("https://someone:secret@pages.example.org/lesson")]
    public void An_address_that_is_not_a_plain_web_address_is_refused(string? url)
    {
        var (controller, _) = Create();

        Assert.IsType<BadRequestObjectResult>(controller.PutPage(Lesson, Body(url)).Result);
        Assert.False(File.Exists(PagesFile));
    }

    [Theory]
    [InlineData("23.10.2026")]
    [InlineData("2026-10-2")]
    [InlineData("2026-13-40")]
    [InlineData("2026.10.23")]
    public void A_date_not_written_as_a_lesson_is_dated_is_refused(string date)
    {
        var (controller, _) = Create();

        Assert.IsType<BadRequestObjectResult>(controller.PutPage(date, Body(PageA)).Result);
        Assert.IsType<BadRequestObjectResult>(controller.RemovePage(date, PageA).Result);
        Assert.False(File.Exists(PagesFile));
    }

    [Fact]
    public void A_title_longer_than_a_line_of_a_link_is_refused()
    {
        var (controller, _) = Create();

        Assert.IsType<BadRequestObjectResult>(
            controller.PutPage(Lesson, Body(PageA, new string('x', LessonPageRules.MaxTitleLength + 1))).Result);
        Assert.IsType<BadRequestObjectResult>(controller.PutPage(Lesson, Body(PageA, "two\nlines")).Result);
        Assert.False(File.Exists(PagesFile));
    }

    [Fact]
    public void A_page_is_put_only_for_a_day_that_has_a_lesson()
    {
        var (controller, _) = Create();
        var (withoutRecordings, _) = Create(recordingsConfigured: false);

        var noLesson = Assert.IsType<NotFoundObjectResult>(controller.PutPage("2026-10-24", Body(PageA)).Result);
        Assert.Contains("2026-10-24", Assert.IsType<ProblemDetails>(noLesson.Value).Detail);
        Assert.IsType<NotFoundObjectResult>(withoutRecordings.PutPage(Lesson, Body(PageA)).Result);
        Assert.False(File.Exists(PagesFile));
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_shows_no_pages_and_is_not_written_over()
    {
        const string broken = "{ \"lessons\": { \"2026-10-23\": [ { \"url\": ";
        File.WriteAllText(PagesFile, broken, new UTF8Encoding(false));
        var (controller, _) = Create();

        Assert.All(await ListedAsync(controller), s => Assert.Empty(s.Pages));
        var refused = Assert.IsType<ConflictObjectResult>(controller.PutPage(Lesson, Body(PageA)).Result);
        Assert.Contains(FileLessonPagesStore.FileName, Assert.IsType<ProblemDetails>(refused.Value).Detail);
        Assert.IsType<ConflictObjectResult>(controller.RemovePage(Lesson, PageA).Result);
        Assert.Equal(broken, File.ReadAllText(PagesFile));
    }

    [Fact]
    public async Task Of_a_file_written_by_hand_only_plain_web_addresses_become_links()
    {
        File.WriteAllText(PagesFile, """
            {
              "schema_version": 1,
              "lessons": {
                "2026-10-23": [
                  { "url": "javascript:alert(1)", "title": "not a page" },
                  null,
                  { "url": "https://pages.example.org/lesson/a1", "title": "Lesson package" },
                  { "url": "https://pages.example.org/lesson/a1", "title": "the same again" },
                  { "title": "no address" }
                ],
                "not a date": [ { "url": "https://pages.example.org/lesson/b2" } ],
                "2026-10-30": null
              }
            }
            """, new UTF8Encoding(false));
        var (controller, _) = Create();

        var sessions = await ListedAsync(controller);

        var page = Assert.Single(sessions.Single(s => s.Date == Lesson).Pages);
        Assert.Equal(PageA, page.Url);
        Assert.Equal("Lesson package", page.Title);
        Assert.Empty(sessions.Single(s => s.Date == "2026-10-30").Pages);

        // A change keeps what the file held for the other days.
        Put(controller.PutPage("2026-10-30", Body(PageB)));
        Assert.Equal(new[] { PageA }, (await ListedAsync(controller)).Single(s => s.Date == Lesson).Pages.Select(p => p.Url));
    }

    [Fact]
    public void What_was_added_to_the_file_by_hand_stays_after_a_change()
    {
        File.WriteAllText(PagesFile, """
            {
              "schema_version": 1,
              "comment": "kept by the operator",
              "lessons": {
                "2026-10-23": [
                  { "url": "https://pages.example.org/lesson/a1", "note": "checked twice", "marks": [1, 2] }
                ]
              }
            }
            """, new UTF8Encoding(false));
        var (controller, _) = Create();

        Put(controller.PutPage("2026-10-30", Body(PageB)));
        Put(controller.PutPage(Lesson, Body(PageA, "renamed")));

        using var written = JsonDocument.Parse(File.ReadAllText(PagesFile));
        Assert.Equal("kept by the operator", written.RootElement.GetProperty("comment").GetString());
        var page = written.RootElement.GetProperty("lessons").GetProperty(Lesson)[0];
        Assert.Equal("renamed", page.GetProperty("title").GetString());
        Assert.Equal("checked twice", page.GetProperty("note").GetString());
        Assert.Equal(2, page.GetProperty("marks").GetArrayLength());
    }

    [Fact]
    public void A_change_that_cannot_be_written_says_so_and_leaves_the_file_as_it_was()
    {
        var (controller, _) = Create();
        Put(controller.PutPage(Lesson, Body(PageA)));
        var before = File.ReadAllText(PagesFile);
        // The change is written next to the file first; a folder of that name stands in the way.
        Directory.CreateDirectory(PagesFile + ".tmp");

        var put = Assert.IsType<ObjectResult>(controller.PutPage(Lesson, Body(PageB)).Result);
        var removed = Assert.IsType<ObjectResult>(controller.RemovePage(Lesson, PageA).Result);

        Assert.Equal(500, put.StatusCode);
        Assert.Contains(FileLessonPagesStore.FileName, Assert.IsType<ProblemDetails>(put.Value).Detail);
        Assert.Equal(500, removed.StatusCode);
        Assert.Equal(before, File.ReadAllText(PagesFile));
    }

    [Fact]
    public void The_jobs_folder_is_not_made_for_a_page()
    {
        var (controller, _) = Create();
        Directory.Delete(_workspace, recursive: true);

        var put = Assert.IsType<ObjectResult>(controller.PutPage(Lesson, Body(PageA)).Result);

        Assert.Equal(500, put.StatusCode);
        Assert.False(Directory.Exists(_workspace));
    }

    [Fact]
    public void A_lesson_takes_no_more_pages_than_the_limit()
    {
        var (controller, _) = Create();
        for (var i = 0; i < LessonPageRules.MaxPagesPerLesson; i++)
            Put(controller.PutPage(Lesson, Body($"https://pages.example.org/lesson/{i}")));

        Assert.IsType<ConflictObjectResult>(controller.PutPage(Lesson, Body(PageA)).Result);
        // One that is already there can still be renamed.
        Assert.Equal(
            LessonPageRules.MaxPagesPerLesson,
            Put(controller.PutPage(Lesson, Body("https://pages.example.org/lesson/0", "renamed"))).Count);
    }

    private sealed class NoQueue : IPipelineQueue
    {
        public void Enqueue(string jobId) { }

        public void EnqueueRefiner(string jobId, string? inputFile = null, bool lessonPrompt = false) { }
    }
}
