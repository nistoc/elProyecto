using Agent04.Application;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agent04.Tests;

public class SubChunkDeletionTests
{
    private static ProjectArtifactService CreateArtifactService()
    {
        return new ProjectArtifactService(
            new JobArtifactRootRegistry(),
            new ConfigurationBuilder().Build(),
            new PerJobCancellationManagerFactory(),
            new AudioUtils(),
            new TranscriptionOutputWriter(NullLogger<TranscriptionOutputWriter>.Instance),
            new WorkspaceRoot(Path.GetTempPath()),
            new TestHostEnvironment(),
            NullLogger<ProjectArtifactService>.Instance);
    }

    [Fact]
    public async Task TryDeleteSubChunkArtifactsAsync_removes_audio_result_merged_and_work_state_row()
    {
        var root = Path.Combine(Path.GetTempPath(), "subdel_" + Guid.NewGuid().ToString("N"));
        var chunkDir = Path.Combine(root, "split_chunks", "chunk_0");
        var subDir = Path.Combine(chunkDir, "sub_chunks");
        var resultsDir = Path.Combine(chunkDir, "results");
        Directory.CreateDirectory(subDir);
        Directory.CreateDirectory(resultsDir);

        await File.WriteAllTextAsync(Path.Combine(subDir, "stem_sub_01.wav"), "x");
        await File.WriteAllTextAsync(Path.Combine(resultsDir, "sub_chunk_01_result.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(chunkDir, "chunk_0_merged.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(chunkDir, "chunk_0_merged.md"), "#");

        var doc = new TranscriptionWorkStateDocument
        {
            SchemaVersion = 2,
            TotalChunks = 1,
            Chunks =
            [
                new TranscriptionWorkStateChunk { Index = 0, State = "Completed" },
                new TranscriptionWorkStateChunk
                {
                    Index = 0,
                    IsSubChunk = true,
                    ParentChunkIndex = 0,
                    SubChunkIndex = 1,
                    State = "Completed",
                },
            ],
        };
        await TranscriptionWorkStateFile.SaveAsync(root, doc, CancellationToken.None);

        var svc = CreateArtifactService();
        var (ok, msg) = await svc.TryDeleteSubChunkArtifactsAsync(
            root,
            "job-test",
            parentChunkIndex: 0,
            subChunkIndex: 1,
            splitChunksDir: null,
            CancellationToken.None);

        Assert.True(ok, msg);
        Assert.False(File.Exists(Path.Combine(subDir, "stem_sub_01.wav")));
        Assert.False(File.Exists(Path.Combine(resultsDir, "sub_chunk_01_result.json")));
        Assert.False(File.Exists(Path.Combine(chunkDir, "chunk_0_merged.json")));
        Assert.False(File.Exists(Path.Combine(chunkDir, "chunk_0_merged.md")));

        var after = await TranscriptionWorkStateFile.TryLoadAsync(root, CancellationToken.None);
        Assert.NotNull(after?.Chunks);
        Assert.DoesNotContain(after.Chunks, c => c.IsSubChunk && c.SubChunkIndex == 1);
        Assert.Contains(after.Chunks, c => !c.IsSubChunk && c.Index == 0);

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Fact]
    public async Task TryDeleteSubChunkArtifactsAsync_other_sub_index_unchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), "subdel2_" + Guid.NewGuid().ToString("N"));
        var chunkDir = Path.Combine(root, "split_chunks", "chunk_0");
        var subDir = Path.Combine(chunkDir, "sub_chunks");
        Directory.CreateDirectory(subDir);
        await File.WriteAllTextAsync(Path.Combine(subDir, "a_sub_00.wav"), "a");
        await File.WriteAllTextAsync(Path.Combine(subDir, "b_sub_01.wav"), "b");

        var svc = CreateArtifactService();
        var (ok, _) = await svc.TryDeleteSubChunkArtifactsAsync(
            root, "j", 0, 0, null, CancellationToken.None);
        Assert.True(ok);
        Assert.False(File.Exists(Path.Combine(subDir, "a_sub_00.wav")));
        Assert.True(File.Exists(Path.Combine(subDir, "b_sub_01.wav")));

        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            /* ignore */
        }
    }

    private sealed class TestHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
