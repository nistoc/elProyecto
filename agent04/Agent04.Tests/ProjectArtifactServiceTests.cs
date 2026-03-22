using Agent04.Application;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agent04.Tests;

public class ProjectArtifactServiceTests
{
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static ProjectArtifactService CreateService(
        IJobArtifactRootRegistry registry,
        IConfiguration? configuration = null)
    {
        configuration ??= new ConfigurationBuilder().Build();
        return new ProjectArtifactService(
            registry,
            configuration,
            new PerJobCancellationManagerFactory(),
            new AudioUtils(),
            new TranscriptionOutputWriter(NullLogger<TranscriptionOutputWriter>.Instance),
            new WorkspaceRoot(Path.GetTempPath()),
            new TestHostEnvironment(),
            NullLogger<ProjectArtifactService>.Instance);
    }

    [Fact]
    public void ResolveJobArtifactRoot_returns_registered_path_when_in_registry()
    {
        var registry = new JobArtifactRootRegistry();
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ws_pa", "root"));
        var registered = Path.Combine(workspace, "jobdir");
        registry.Register("jid-1", registered);

        var svc = CreateService(registry);
        var r = svc.ResolveJobArtifactRoot(workspace, "jid-1", null);

        Assert.True(r.IsSuccess);
        Assert.Equal(registered, r.Path);
    }

    [Fact]
    public void ResolveJobArtifactRoot_combines_single_segment_under_workspace()
    {
        var registry = new JobArtifactRootRegistry();
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ws_pa", "w2"));
        Directory.CreateDirectory(workspace);

        var svc = CreateService(registry);
        var r = svc.ResolveJobArtifactRoot(workspace, "unknown-job", "myjob");

        Assert.True(r.IsSuccess);
        Assert.Equal(Path.GetFullPath(Path.Combine(workspace, "myjob")), r.Path);
    }

    [Fact]
    public void ResolveJobArtifactRoot_rejects_parent_segments_in_relative()
    {
        var registry = new JobArtifactRootRegistry();
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ws_pa", "w3"));

        var svc = CreateService(registry);
        var r = svc.ResolveJobArtifactRoot(workspace, "j", "..");

        Assert.False(r.IsSuccess);
        Assert.Equal(ArtifactRootResolutionFailureCode.InvalidRelativePath, r.Failure);
    }

    [Fact]
    public void ResolveJobArtifactRoot_rejects_multi_segment_relative()
    {
        var registry = new JobArtifactRootRegistry();
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ws_pa", "w4"));

        var svc = CreateService(registry);
        var r = svc.ResolveJobArtifactRoot(workspace, "j", "a/b");

        Assert.False(r.IsSuccess);
        Assert.Equal(ArtifactRootResolutionFailureCode.InvalidRelativePath, r.Failure);
    }

    [Fact]
    public void ResolveJobArtifactRoot_strict_fails_without_registry_and_relative()
    {
        var registry = new JobArtifactRootRegistry();
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ws_pa", "w6"));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Agent04:StrictChunkCancelPath"] = "true" })
            .Build();

        var svc = CreateService(registry, config);
        var r = svc.ResolveJobArtifactRoot(workspace, "no-registry", null);

        Assert.False(r.IsSuccess);
        Assert.Equal(ArtifactRootResolutionFailureCode.StrictRequiresJobDirectoryRelative, r.Failure);
    }

    [Fact]
    public void ResolveJobArtifactRoot_legacy_returns_workspace_when_not_strict()
    {
        var registry = new JobArtifactRootRegistry();
        var workspace = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ws_pa", "w7"));

        var svc = CreateService(registry);
        var r = svc.ResolveJobArtifactRoot(workspace, "legacy", null);

        Assert.True(r.IsSuccess);
        Assert.Equal(workspace, r.Path);
    }

    [Fact]
    public void ResolveJobArtifactRoot_throws_when_workspace_empty()
    {
        var registry = new JobArtifactRootRegistry();
        var svc = CreateService(registry);
        Assert.Throws<ArgumentException>(() => svc.ResolveJobArtifactRoot("   ", "j", null));
    }
}
