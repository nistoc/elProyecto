using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public class TranscriptionPathsTests
{
    [Fact]
    public void ResolveArtifactRoot_returns_parent_directory_when_file_in_subfolder()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "a04paths", Guid.NewGuid().ToString("N"));
        var jobDir = Path.Combine(workspace, "job123");
        Directory.CreateDirectory(jobDir);
        var audio = Path.Combine(jobDir, "clip.m4a");
        File.WriteAllText(audio, "x");

        var root = Path.GetFullPath(workspace);
        var artifact = TranscriptionPaths.ResolveArtifactRoot(root, audio);

        Assert.Equal(Path.GetFullPath(jobDir), artifact);
        try { Directory.Delete(workspace, true); } catch { /* ignore */ }
    }

    [Fact]
    public void ResolveArtifactRoot_returns_workspace_when_file_at_workspace_root()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "a04paths2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var audio = Path.Combine(workspace, "root.m4a");
        File.WriteAllText(audio, "x");
        var root = Path.GetFullPath(workspace);

        var artifact = TranscriptionPaths.ResolveArtifactRoot(root, audio);

        Assert.Equal(root, artifact);
        try { Directory.Delete(workspace, true); } catch { /* ignore */ }
    }

    [Fact]
    public void ResolveArtifactRoot_throws_when_file_outside_workspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "a04paths3", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "a04paths3_out", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(outside);
        var audio = Path.Combine(outside, "x.m4a");
        File.WriteAllText(audio, "x");

        Assert.Throws<InvalidOperationException>(() =>
            TranscriptionPaths.ResolveArtifactRoot(Path.GetFullPath(workspace), audio));

        try { Directory.Delete(workspace, true); Directory.Delete(outside, true); } catch { /* ignore */ }
    }

    [Fact]
    public void ResolveArtifactRoot_throws_when_file_missing()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "a04paths4", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var missing = Path.Combine(workspace, "nope.m4a");

        Assert.Throws<FileNotFoundException>(() =>
            TranscriptionPaths.ResolveArtifactRoot(Path.GetFullPath(workspace), missing));

        try { Directory.Delete(workspace, true); } catch { /* ignore */ }
    }
}
