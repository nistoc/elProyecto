using Agent04.Application;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Agent04.Features.Transcription.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agent04.Tests;

/// <summary>
/// Chunk JSON keeps chunk-local times. A transcript rebuilt from it must land on the timeline of the
/// whole recording, not restart from zero in every chunk.
/// </summary>
public class ChunkTimelineTests
{
    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string NewJobDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "a04timeline_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return root;
    }

    private static TranscriptionPipeline CreatePipeline()
    {
        var artifacts = new ProjectArtifactService(
            new JobArtifactRootRegistry(),
            new ConfigurationBuilder().Build(),
            new PerJobCancellationManagerFactory(),
            new AudioUtils(),
            new TranscriptionOutputWriter(NullLogger<TranscriptionOutputWriter>.Instance),
            new WorkspaceRoot(Path.GetTempPath()),
            new TestHostEnvironment(),
            NullLogger<ProjectArtifactService>.Instance);
        // Rebuild never transcribes and never reads the cache.
        return new TranscriptionPipeline(new AudioUtils(), null!, null!, new TranscriptionMerger(), artifacts);
    }

    /// <summary>Three chunk audio stubs, their chunk-local JSON and the timeline a real run would have written.</summary>
    private static async Task<ChunkInfo[]> SeedThreeChunkJobAsync(string root, bool withTimeline)
    {
        var chunksDir = Path.Combine(root, "chunks");
        var jsonDir = Path.Combine(root, "chunks_json");
        Directory.CreateDirectory(chunksDir);
        Directory.CreateDirectory(jsonDir);

        var infos = new ChunkInfo[3];
        for (var i = 0; i < 3; i++)
        {
            var audio = Path.Combine(chunksDir, $"lesson_part_00{i}.wav");
            await File.WriteAllBytesAsync(audio, new byte[] { 0 });
            infos[i] = new ChunkInfo(audio, i * 157.0, i == 0 ? 0.0 : 1.0);

            // 0.40 s sits inside the 1 s overlap with the previous chunk; 5.00 s does not.
            await File.WriteAllTextAsync(
                Path.Combine(jsonDir, $"lesson_part_00{i}.json"),
                $$"""
                {
                  "text": "dup {{i}} line {{i}}",
                  "segments": [
                    { "start": 0.4, "end": 0.9, "text": "dup {{i}}", "speaker": "A" },
                    { "start": 5.0, "end": 6.5, "text": "line {{i}}", "speaker": "A" }
                  ]
                }
                """);
        }

        if (withTimeline)
            await ChunkTimelineFile.SaveAsync(root, infos, CancellationToken.None);
        return infos;
    }

    private static async Task<TranscriptionConfig> LoadDefaultConfigAsync() =>
        await TranscriptionConfig.FromFileAsync(Path.Combine(AppContext.BaseDirectory, "config", "default.json"));

    private static List<double> TimestampsOf(string mdPath) =>
        File.ReadAllLines(mdPath)
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Select(l => double.Parse(l.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

    [Fact]
    public async Task Rebuild_puts_regenerated_chunk_markdown_on_the_recording_timeline()
    {
        var root = NewJobDir();
        try
        {
            await SeedThreeChunkJobAsync(root, withTimeline: true);

            await CreatePipeline().RebuildCombinedOutputsFromPerChunkJsonAsync(
                await LoadDefaultConfigAsync(), root, "job1", null, CancellationToken.None);

            var chunk1 = await File.ReadAllTextAsync(Path.Combine(root, "chunks_md", "lesson_part_001.md"));
            Assert.Contains("- 162.00 speaker_0: \"line 1\"", chunk1);
            // The segment inside the overlap was already emitted by chunk 0: not repeated.
            Assert.DoesNotContain("dup 1", chunk1);

            var chunk0 = await File.ReadAllTextAsync(Path.Combine(root, "chunks_md", "lesson_part_000.md"));
            Assert.Contains("- 0.40 speaker_0: \"dup 0\"", chunk0);

            var combined = Directory.GetFiles(root, "transcript_*.md").Single();
            var times = TimestampsOf(combined);
            Assert.Equal(new[] { 0.40, 5.00, 162.00, 319.00 }, times);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Rebuild_keeps_an_existing_chunk_markdown_untouched()
    {
        var root = NewJobDir();
        try
        {
            await SeedThreeChunkJobAsync(root, withTimeline: true);
            var mdDir = Path.Combine(root, "chunks_md");
            Directory.CreateDirectory(mdDir);
            const string edited = ">>>>>>>\n- 163.50 speaker_0: \"edited by the operator\"\n<<<<<\n";
            await File.WriteAllTextAsync(Path.Combine(mdDir, "lesson_part_001.md"), edited);

            await CreatePipeline().RebuildCombinedOutputsFromPerChunkJsonAsync(
                await LoadDefaultConfigAsync(), root, "job1", null, CancellationToken.None);

            Assert.Equal(edited, await File.ReadAllTextAsync(Path.Combine(mdDir, "lesson_part_001.md")));
            var combined = await File.ReadAllTextAsync(Directory.GetFiles(root, "transcript_*.md").Single());
            Assert.Contains("edited by the operator", combined);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Timeline_file_round_trips_and_matches_by_name_then_by_index()
    {
        var root = NewJobDir();
        try
        {
            var infos = await SeedThreeChunkJobAsync(root, withTimeline: true);

            var doc = await ChunkTimelineFile.TryLoadAsync(root, CancellationToken.None);

            Assert.NotNull(doc);
            Assert.Equal(3, doc!.Chunks.Count);
            var byName = ChunkTimelineFile.Find(doc, index: 0, infos[2].Path);
            Assert.Equal(314.0, byName!.OffsetSec);
            Assert.Equal(1.0, byName.EmitGuardSec);
            // A re-encoded chunk keeps its index but has another file name.
            var byIndex = ChunkTimelineFile.Find(doc, index: 1, Path.Combine(root, "chunks", "lesson_part_001_re.wav"));
            Assert.Equal(157.0, byIndex!.OffsetSec);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_timeline_file_loads_as_null()
    {
        var root = NewJobDir();
        try
        {
            Assert.Null(await ChunkTimelineFile.TryLoadAsync(root, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, 158.0, 1.0, 0.0, 0.0)]
    [InlineData(1, 158.0, 1.0, 157.0, 1.0)]
    [InlineData(7, 158.0, 1.0, 1099.0, 1.0)]
    // A stream-copied m4a chunk is a few frames off the whole-second window.
    [InlineData(3, 157.98, 1.0, 471.0, 1.0)]
    [InlineData(2, 480.02, 0.0, 960.0, 0.0)]
    public void Derive_repeats_the_chunker_arithmetic(int index, double firstChunkSec, double overlapSec, double offset, double guard)
    {
        var (offsetSec, emitGuardSec) = ChunkTimelineFile.Derive(index, firstChunkSec, overlapSec);

        Assert.Equal(offset, offsetSec, precision: 6);
        Assert.Equal(guard, emitGuardSec, precision: 6);
    }
}
