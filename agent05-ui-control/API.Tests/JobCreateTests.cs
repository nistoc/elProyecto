using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

public class FakeHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "API.Tests";
    public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
    public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(Directory.GetCurrentDirectory());
}

public class JobCreateTests
{
    [Fact]
    public async Task CreateAsync_then_Get_returns_job_with_tags()
    {
        var store = new InMemoryJobStore();
        var id = await store.CreateAsync(new JobCreateInput("test.m4a", new[] { "a", "b" }));
        var snap = await store.GetAsync(id);
        Assert.NotNull(snap);
        Assert.Equal(2, snap.Tags?.Count ?? 0);
    }

    [Fact]
    public async Task JobWorkspace_SaveUploadedFile_creates_file_in_job_dir()
    {
        var temp = Path.Combine(Path.GetTempPath(), "XtractManagerTests", Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:WorkspacePath"] = temp })
            .Build();
        var env = new FakeHostEnvironment { ContentRootPath = Path.GetTempPath() };
        var workspace = new JobWorkspace(config, env, NullLogger<JobWorkspace>.Instance);
        var jobId = "test-job-1";
        await workspace.EnsureJobDirectoryAsync(jobId);
        var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("fake audio content"));
        var savedName = await workspace.SaveUploadedFileAsync(jobId, ms, "My Interview.m4a");
        Assert.Equal("My Interview.m4a", savedName);
        var path = Path.Combine(workspace.GetJobDirectoryPath(jobId), savedName);
        Assert.True(File.Exists(path));
        try { Directory.Delete(temp, true); } catch { }
    }
}
