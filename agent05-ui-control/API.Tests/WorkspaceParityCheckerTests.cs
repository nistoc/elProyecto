using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

public class FakeHostEnv : IHostEnvironment
{
    public FakeHostEnv()
    {
        ContentRootPath = Directory.GetCurrentDirectory();
        ContentRootFileProvider = new PhysicalFileProvider(ContentRootPath);
    }

    public string EnvironmentName { get; set; } = "Test";
    public string ApplicationName { get; set; } = "Test";
    public string ContentRootPath { get; set; }
    public IFileProvider ContentRootFileProvider { get; set; }
}

public class WorkspaceParityCheckerTests
{
    [Fact]
    public void ValidateAtStartup_same_paths_no_throw()
    {
        var p = Path.Combine(Path.GetTempPath(), "wpx", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        var full = Path.GetFullPath(p);
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jobs:WorkspacePath"] = full,
            ["Agent06:WorkspaceRoot"] = full
        }).Build();
        var ex = Record.Exception(() =>
            WorkspaceParityChecker.ValidateAtStartup(cfg, new FakeHostEnv(), NullLogger.Instance));
        Assert.Null(ex);
        try { Directory.Delete(p, true); } catch { /* ignore */ }
    }

    [Fact]
    public void ValidateAtStartup_different_paths_and_strict_throws()
    {
        var a = Path.Combine(Path.GetTempPath(), "wpA", Guid.NewGuid().ToString("N"));
        var b = Path.Combine(Path.GetTempPath(), "wpB", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jobs:WorkspacePath"] = Path.GetFullPath(a),
            ["Agent06:WorkspaceRoot"] = Path.GetFullPath(b),
            ["Xtract:RequireAgent06WorkspaceMatchesJobs"] = "true"
        }).Build();

        Assert.Throws<InvalidOperationException>(() =>
            WorkspaceParityChecker.ValidateAtStartup(cfg, new FakeHostEnv(), NullLogger.Instance));

        try { Directory.Delete(a, true); Directory.Delete(b, true); } catch { /* ignore */ }
    }
}
