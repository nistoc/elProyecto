using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

public class OperatorSubChunkArtifactsTests
{
    [Fact]
    public void IsSubChunkRunning_true_when_vm_row_running()
    {
        var snap = new JobSnapshot
        {
            Chunks = new ChunkState
            {
                ChunkVirtualModel = new List<ChunkVirtualModelEntry>
                {
                    new()
                    {
                        IsSubChunk = true,
                        ParentChunkIndex = 1,
                        SubChunkIndex = 2,
                        State = "Running",
                    },
                },
            },
        };

        Assert.True(OperatorSubChunkArtifacts.IsSubChunkRunning(snap, 1, 2));
        Assert.False(OperatorSubChunkArtifacts.IsSubChunkRunning(snap, 1, 3));
    }

    [Fact]
    public async Task TryDeleteBundleAsync_deletes_matching_audio_and_result_json()
    {
        var root = Path.Combine(Path.GetTempPath(), "xtract-sub-del-" + Guid.NewGuid().ToString("N"));
        try
        {
            var subDir = Path.Combine(root, "split_chunks", "chunk_0", "sub_chunks");
            var resDir = Path.Combine(root, "split_chunks", "chunk_0", "results");
            Directory.CreateDirectory(subDir);
            Directory.CreateDirectory(resDir);
            var audio = Path.Combine(subDir, "stem_sub_01.wav");
            var json = Path.Combine(resDir, "sub_chunk_01_result.json");
            File.WriteAllText(audio, "a");
            File.WriteAllText(json, "{}");

            var (ok, msg) = await OperatorSubChunkArtifacts.TryDeleteBundleAsync(
                root,
                agent04JobId: "jid",
                parentChunkIndex: 0,
                subChunkIndex: 1,
                splitChunksDir: "split_chunks",
                snap: null,
                logger: null,
                CancellationToken.None);

            Assert.True(ok);
            Assert.Equal("sub_chunk_deleted", msg);
            Assert.False(File.Exists(audio));
            Assert.False(File.Exists(json));
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
    public async Task TryDeleteBundleAsync_fails_when_sub_running_in_snapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "xtract-sub-del-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var snap = new JobSnapshot
            {
                Chunks = new ChunkState
                {
                    ChunkVirtualModel = new List<ChunkVirtualModelEntry>
                    {
                        new()
                        {
                            IsSubChunk = true,
                            ParentChunkIndex = 0,
                            SubChunkIndex = 1,
                            State = "Running",
                        },
                    },
                },
            };

            var (ok, msg) = await OperatorSubChunkArtifacts.TryDeleteBundleAsync(
                root,
                null,
                0,
                1,
                "split_chunks",
                snap,
                null,
                CancellationToken.None);

            Assert.False(ok);
            Assert.Contains("running", msg, StringComparison.OrdinalIgnoreCase);
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
