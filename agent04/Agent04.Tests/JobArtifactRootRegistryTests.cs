using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public class JobArtifactRootRegistryTests
{
    [Fact]
    public void Register_TryGet_Unregister_roundtrip()
    {
        var r = new JobArtifactRootRegistry();
        const string id = "job-1";
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "reg", "a"));

        r.Register(id, root);
        Assert.True(r.TryGet(id, out var got));
        Assert.Equal(root, got);

        r.Unregister(id);
        Assert.False(r.TryGet(id, out _));
    }

    [Fact]
    public void Register_ignores_empty_id()
    {
        var r = new JobArtifactRootRegistry();
        r.Register("", "/tmp/x");
        r.Register("   ", "/tmp/y");
        Assert.False(r.TryGet("", out _));
    }
}
