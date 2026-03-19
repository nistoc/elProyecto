using TranslationImprover.Features.Refine.Infrastructure;
using Xunit;

namespace TranslationImprover.Tests;

public class RefineWorkspacePathsTests
{
    [Fact]
    public void ResolveEffectiveArtifactRoot_empty_segment_returns_workspace()
    {
        var w = Path.Combine(Path.GetTempPath(), "rwp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(w);
        var root = Path.GetFullPath(w);

        var artifact = RefineWorkspacePaths.ResolveEffectiveArtifactRoot(root, null);
        Assert.Equal(root, artifact);
        try { Directory.Delete(w, true); } catch { /* ignore */ }
    }

    [Fact]
    public void ResolveEffectiveArtifactRoot_subfolder()
    {
        var w = Path.Combine(Path.GetTempPath(), "rwp2", Guid.NewGuid().ToString("N"));
        var job = Path.Combine(w, "job1");
        Directory.CreateDirectory(job);
        var root = Path.GetFullPath(w);

        var artifact = RefineWorkspacePaths.ResolveEffectiveArtifactRoot(root, "job1");
        Assert.Equal(Path.GetFullPath(job), artifact);
        try { Directory.Delete(w, true); } catch { /* ignore */ }
    }

    [Fact]
    public void ResolveEffectiveArtifactRoot_rejects_multi_segment()
    {
        var w = Path.Combine(Path.GetTempPath(), "rwp3", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(w);

        Assert.Throws<ArgumentException>(() =>
            RefineWorkspacePaths.ResolveEffectiveArtifactRoot(Path.GetFullPath(w), "a/b"));

        try { Directory.Delete(w, true); } catch { /* ignore */ }
    }
}
