using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

public class OperatorSplitArtifactPresenceTests
{
    [Fact]
    public void HasArtifacts_returns_false_when_job_dir_missing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xtract-op-split-" + Guid.NewGuid().ToString("N"));
        Assert.False(OperatorSplitArtifactPresence.HasArtifactsForChunk(dir, 0));
    }

    [Fact]
    public void HasArtifacts_true_when_sub_chunks_has_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "xtract-op-split-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sub = Path.Combine(root, "split_chunks", "chunk_2", "sub_chunks");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "piece_sub_01.wav"), "x");

            Assert.True(OperatorSplitArtifactPresence.HasArtifactsForChunk(root, 2));
            Assert.False(OperatorSplitArtifactPresence.HasArtifactsForChunk(root, 1));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    [Fact]
    public void HasArtifacts_true_when_results_has_sub_chunk_result_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "xtract-op-split-" + Guid.NewGuid().ToString("N"));
        try
        {
            var res = Path.Combine(root, "split_chunks", "chunk_0", "results");
            Directory.CreateDirectory(res);
            File.WriteAllText(Path.Combine(res, "sub_chunk_02_result.json"), "{}");

            Assert.True(OperatorSplitArtifactPresence.HasArtifactsForChunk(root, 0));
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }
}
