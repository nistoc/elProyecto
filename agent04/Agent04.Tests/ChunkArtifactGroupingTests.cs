using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public class ChunkArtifactGroupingTests
{
    [Theory]
    [InlineData("foo_part_000.wav", 0)]
    [InlineData("x _part_12_suffix.wav", 12)]
    [InlineData("noindex.wav", null)]
    public void InferChunkIndexFromName_aligns_with_ts(string name, int? expected)
    {
        var got = ChunkArtifactGrouping.InferChunkIndexFromName(name);
        Assert.Equal(expected, got);
    }

    [Fact]
    public void FileBelongsToChunkIndex_prefers_part_over_scanner_index()
    {
        var f = new ArtifactFileEntry { Name = "2026-02-25_part_000.wav", Index = 2026, Kind = "audio" };
        Assert.True(ChunkArtifactGrouping.FileBelongsToChunkIndex(f, 0, 0));
        Assert.False(ChunkArtifactGrouping.FileBelongsToChunkIndex(f, 2026, 0));
    }

    [Fact]
    public void ComputeChunkIndices_with_positive_total_is_dense_range()
    {
        var scan = new JobArtifactDirectoryScanner.ScanResult
        {
            Chunks =
            [
                new ArtifactFileEntry { Name = "a_part_999.wav", Kind = "audio", RelativePath = "chunks/a.wav" },
            ],
        };
        var indices = ChunkArtifactGrouping.ComputeChunkIndices(3, scan);
        Assert.Equal(new[] { 0, 1, 2 }, indices);
    }

    [Fact]
    public void ComputeChunkIndices_without_total_unions_file_and_split_parents()
    {
        var scan = new JobArtifactDirectoryScanner.ScanResult
        {
            Chunks =
            [
                new ArtifactFileEntry { Name = "c_part_001.wav", Kind = "audio", RelativePath = "chunks/c.wav", Index = 1 },
            ],
            SplitChunks =
            [
                new ArtifactFileEntry
                {
                    Name = "x_sub_00.wav",
                    Kind = "audio",
                    RelativePath = "split_chunks/chunk_2/sub_chunks/x.wav",
                    ParentIndex = 2,
                    SubIndex = 0,
                },
            ],
        };
        var indices = ChunkArtifactGrouping.ComputeChunkIndices(0, scan);
        Assert.Equal(new[] { 1, 2 }, indices);
    }

    [Fact]
    public void BuildChunkGroups_places_merged_at_parent_not_in_subchunks()
    {
        var scan = new JobArtifactDirectoryScanner.ScanResult
        {
            Chunks =
            [
                new ArtifactFileEntry { Name = "a_part_000.wav", Kind = "audio", RelativePath = "chunks/a.wav" },
            ],
            SplitChunks =
            [
                new ArtifactFileEntry
                {
                    Name = "chunk_0_merged.json",
                    Kind = "text",
                    RelativePath = "split_chunks/chunk_0/chunk_0_merged.json",
                    ParentIndex = 0,
                    HasTranscript = true,
                    IsTranscript = true,
                },
                new ArtifactFileEntry
                {
                    Name = "s_sub_00.wav",
                    Kind = "audio",
                    RelativePath = "split_chunks/chunk_0/sub_chunks/s.wav",
                    ParentIndex = 0,
                    SubIndex = 0,
                },
            ],
        };
        var groups = ChunkArtifactGrouping.BuildChunkGroups(scan, [0], 1);
        Assert.Single(groups);
        Assert.Single(groups[0].MergedSplitFiles);
        Assert.Contains(groups[0].SubChunks, s => s.SubIndex == 0);
        Assert.DoesNotContain(groups[0].SubChunks, s => s.JsonFiles.Any(f => f.Name.Contains("merged", StringComparison.Ordinal)));
    }

    [Fact]
    public void JobArtifactDirectoryScanner_scans_runtime_sample_when_present()
    {
        var repoRoot = FindRepoRoot();
        var runtimeJob = Path.Combine(
            repoRoot,
            "agent-browser", "runtime", "0b8a3b0b-a847-4d06-835d-d5941ff8ebfd");
        if (!Directory.Exists(runtimeJob))
            return;

        var scan = JobArtifactDirectoryScanner.ScanForChunkGrouping(runtimeJob);
        var total = 0;
        var indices = ChunkArtifactGrouping.ComputeChunkIndices(total, scan);
        if (indices.Length == 0)
            return;

        var groups = ChunkArtifactGrouping.BuildChunkGroups(scan, indices, total);
        Assert.NotEmpty(groups);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "elProyecto.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, "agent-browser")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
