using System.Text.Json;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Agent04.Features.Transcription.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agent04.Tests;

/// <summary>
/// An operator split cuts a chunk into parts that are transcribed on their own; the merge has to put
/// every part back at its place inside the chunk.
/// </summary>
public sealed class SubChunkTimelineTests
{
    [Fact]
    public void FromPlan_keeps_the_start_of_every_part_and_the_overlap_as_guard()
    {
        var plan = OperatorChunkSplitPlanner.PlanEqualSegmentsWithOverlap(158, 4, 1.0);
        var doc = SubChunkTimelineFile.FromPlan(0, 158, plan, new[] { "a_sub_00.wav", "a_sub_01.wav", "a_sub_02.wav", "a_sub_03.wav" });

        Assert.Equal(new[] { 0.0, 39.25, 78.5, 117.75 }, doc.SubChunks.Select(s => s.OffsetSec));
        Assert.Equal(new[] { 0.0, 1.0, 1.0, 1.0 }, doc.SubChunks.Select(s => Math.Round(s.EmitGuardSec, 6)));
        Assert.Equal("a_sub_02.wav", doc.SubChunks[2].File);
        Assert.False(doc.Derived);
    }

    [Theory]
    [InlineData(158.0, 4, 1.0)]
    [InlineData(157.3, 2, 1.0)]
    [InlineData(600.0, 7, 2.0)]
    [InlineData(40.0, 3, 0.0)]
    public void Derive_finds_the_places_the_planner_chose(double duration, int parts, double overlap)
    {
        var plan = OperatorChunkSplitPlanner.PlanEqualSegmentsWithOverlap(duration, parts, overlap);

        var doc = SubChunkTimelineFile.Derive(0, duration, plan[0].DurationSec, parts);

        Assert.NotNull(doc);
        Assert.True(doc!.Derived);
        for (var k = 0; k < parts; k++)
        {
            Assert.Equal(plan[k].StartSec, doc.SubChunks[k].OffsetSec, 6);
            Assert.Equal(k == 0 ? 0.0 : overlap, doc.SubChunks[k].EmitGuardSec, 6);
        }
    }

    [Fact]
    public void Derive_gives_nothing_for_durations_that_cannot_be_a_split()
    {
        Assert.Null(SubChunkTimelineFile.Derive(0, 100, 0, 4));
        Assert.Null(SubChunkTimelineFile.Derive(0, 0, 30, 4));
        Assert.Null(SubChunkTimelineFile.Derive(0, 100, 30, 1));
        Assert.Null(SubChunkTimelineFile.Derive(0, 100, 150, 4));
    }

    [Fact]
    public async Task Timeline_file_is_saved_and_read_back()
    {
        var folder = Path.Combine(Path.GetTempPath(), "agent04_sub_timeline_" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(await SubChunkTimelineFile.TryLoadAsync(folder, CancellationToken.None));

            var plan = OperatorChunkSplitPlanner.PlanEqualSegmentsWithOverlap(60, 2, 1.0);
            await SubChunkTimelineFile.SaveAsync(
                folder, SubChunkTimelineFile.FromPlan(3, 60, plan, new[] { "x_sub_00.wav", "x_sub_01.wav" }), CancellationToken.None);

            var loaded = await SubChunkTimelineFile.TryLoadAsync(folder, CancellationToken.None);
            Assert.NotNull(loaded);
            Assert.Equal(3, loaded!.ParentChunkIndex);
            Assert.Equal(29.5, SubChunkTimelineFile.Find(loaded, 1)!.OffsetSec, 6);
            Assert.Null(SubChunkTimelineFile.Find(loaded, 2));
        }
        finally
        {
            TryDelete(folder);
        }
    }

    [Fact]
    public async Task Merge_puts_each_part_at_its_place_from_the_saved_timeline()
    {
        var job = NewSplitJob();
        try
        {
            SaveResult(job, 0, 0, new ASRSegment(0.1, 0.5, "uno", "A"), new ASRSegment(28.0, 29.4, "dos", "A"));
            // 0.2–0.9 repeats the tail of part 0 (it lies inside the 1 s overlap) and must not appear twice.
            SaveResult(job, 1, 0, new ASRSegment(0.2, 0.9, "dos", "A"), new ASRSegment(2.0, 3.0, "tres", "A"));
            await SaveTimelineAsync(job, 60, 2, 1.0);

            var (ok, msg) = await MergeAsync(job);

            Assert.True(ok, msg);
            Assert.Equal(new[] { 0.1, 28.0, 31.5 }, MergedStarts(job));
            var md = await File.ReadAllTextAsync(Path.Combine(job.ChunkFolder, "chunk_0_merged.md"));
            Assert.Contains("- 31.50 ", md);
            Assert.DoesNotContain("- 2.00 ", md);
        }
        finally
        {
            TryDelete(job.Root);
        }
    }

    [Fact]
    public async Task Merge_measures_the_places_from_the_audio_when_the_split_has_no_timeline()
    {
        var job = NewSplitJob();
        try
        {
            SaveResult(job, 0, 0, new ASRSegment(1.0, 2.0, "uno", "A"));
            SaveResult(job, 1, 0, new ASRSegment(5.0, 6.0, "dos", "A"));

            // The chunk is 100 s long and the first of two parts is 52 s: the second part starts at 48 s.
            var (ok, msg) = await MergeAsync(job, path => Path.GetFileName(path).Contains("_sub_", StringComparison.Ordinal) ? 52.0 : 100.0);

            Assert.True(ok, msg);
            Assert.Equal(new[] { 1.0, 53.0 }, MergedStarts(job));
            var saved = await SubChunkTimelineFile.TryLoadAsync(job.ChunkFolder, CancellationToken.None);
            Assert.NotNull(saved);
            Assert.True(saved!.Derived);
            Assert.Equal(48.0, SubChunkTimelineFile.Find(saved, 1)!.OffsetSec, 6);
            Assert.Equal(4.0, SubChunkTimelineFile.Find(saved, 1)!.EmitGuardSec, 6);
        }
        finally
        {
            TryDelete(job.Root);
        }
    }

    [Fact]
    public async Task Merge_refuses_when_the_places_of_the_parts_are_unknown()
    {
        var job = NewSplitJob();
        try
        {
            SaveResult(job, 0, 0, new ASRSegment(1.0, 2.0, "uno", "A"));
            SaveResult(job, 1, 0, new ASRSegment(5.0, 6.0, "dos", "A"));

            var (ok, msg) = await MergeAsync(job);

            Assert.False(ok);
            Assert.Equal("split_merge_sub_offsets_unknown", msg);
            Assert.False(File.Exists(Path.Combine(job.ChunkFolder, "chunk_0_merged.json")));
            Assert.False(File.Exists(Path.Combine(job.ChunkFolder, "chunk_0_merged.md")));
        }
        finally
        {
            TryDelete(job.Root);
        }
    }

    [Fact]
    public async Task Merge_takes_offsets_that_the_results_already_carry()
    {
        var job = NewSplitJob();
        try
        {
            SaveResult(job, 0, 0, new ASRSegment(1.0, 2.0, "uno", "A"));
            SaveResult(job, 1, 30, new ASRSegment(2.0, 3.0, "dos", "A"));

            var (ok, msg) = await MergeAsync(job);

            Assert.True(ok, msg);
            Assert.Equal(new[] { 1.0, 32.0 }, MergedStarts(job));
        }
        finally
        {
            TryDelete(job.Root);
        }
    }

    [Fact]
    public async Task Merge_of_a_silence_compressed_job_prints_times_of_the_recording()
    {
        var job = NewSplitJob();
        try
        {
            SaveResult(job, 0, 0, new ASRSegment(0.1, 0.5, "uno", "A"));
            SaveResult(job, 1, 0, new ASRSegment(2.0, 3.0, "dos", "A"));
            await SaveTimelineAsync(job, 60, 2, 1.0);
            // 30 s of speech, then 100 s of silence replaced by a 0.2 s gap, then speech from 130 s on.
            await SpeechTimeMapFile.SaveAsync(job.Root, new SilenceCompressionReport
            {
                InputPath = "lesson.wav",
                OutputPath = "lesson_silc.wav",
                InputDurationSec = 200,
                OutputDurationSec = 100.2,
                KeepSilenceSec = 0.2,
                AppliedCompression = true,
                SpeechPieces = new[] { new SpeechPiece(0, 30, 0), new SpeechPiece(130, 200, 30.2) }
            });

            var (ok, msg) = await MergeAsync(job);

            Assert.True(ok, msg);
            // Part 1 starts 29.5 s into the chunk: 2.0 s there is 31.5 s of the compressed file, 1.3 s into the second piece.
            Assert.Equal(new[] { 0.1, 31.5 }, MergedStarts(job));
            var md = await File.ReadAllTextAsync(Path.Combine(job.ChunkFolder, "chunk_0_merged.md"));
            Assert.Contains("- 0.10 ", md);
            Assert.Contains("- 131.30 ", md);
        }
        finally
        {
            TryDelete(job.Root);
        }
    }

    private sealed record SplitJob(string Root, string ChunkFolder, string ResultsDir);

    /// <summary>A job folder with chunk 0 split into two parts (audio files are empty: only their names matter).</summary>
    private static SplitJob NewSplitJob()
    {
        var root = Path.Combine(Path.GetTempPath(), "agent04_sub_timeline_job_" + Guid.NewGuid().ToString("N"));
        var chunkFolder = Path.Combine(root, "split_chunks", "chunk_0");
        var subChunksDir = Path.Combine(chunkFolder, "sub_chunks");
        var resultsDir = Path.Combine(chunkFolder, "results");
        Directory.CreateDirectory(Path.Combine(root, "chunks"));
        Directory.CreateDirectory(subChunksDir);
        Directory.CreateDirectory(resultsDir);
        File.WriteAllText(Path.Combine(root, "chunks", "clip_part_000.m4a"), "");
        File.WriteAllText(Path.Combine(subChunksDir, "clip_part_000_sub_00.m4a"), "");
        File.WriteAllText(Path.Combine(subChunksDir, "clip_part_000_sub_01.m4a"), "");
        return new SplitJob(root, chunkFolder, resultsDir);
    }

    private static void SaveResult(SplitJob job, int subIndex, double storedOffset, params ASRSegment[] segments) =>
        SubChunkResultWriter.Save(
            job.ResultsDir,
            subIndex,
            new TranscriptionResult($"clip_part_000_sub_{subIndex:D2}.m4a", storedOffset, 0, segments, new Dictionary<string, object?>()));

    private static Task SaveTimelineAsync(SplitJob job, double parentDurationSec, int parts, double overlapSec) =>
        SubChunkTimelineFile.SaveAsync(
            job.ChunkFolder,
            SubChunkTimelineFile.FromPlan(
                0,
                parentDurationSec,
                OperatorChunkSplitPlanner.PlanEqualSegmentsWithOverlap(parentDurationSec, parts, overlapSec),
                Enumerable.Range(0, parts).Select(k => $"clip_part_000_sub_{k:D2}.m4a").ToList()),
            CancellationToken.None);

    private static Task<(bool Ok, string Message)> MergeAsync(SplitJob job, Func<string, double>? probe = null) =>
        SplitChunkMergeIntegrator.TryRebuildSplitMergedForChunkAsync(
            new TranscriptionConfig(new Dictionary<string, object?>()),
            job.Root,
            "job-test",
            0,
            new TranscriptionMerger(),
            NullLogger.Instance,
            CancellationToken.None,
            probe);

    private static double[] MergedStarts(SplitJob job)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(job.ChunkFolder, "chunk_0_merged.json")));
        return doc.RootElement.GetProperty("segments").EnumerateArray()
            .Select(s => Math.Round(s.GetProperty("start").GetDouble(), 6))
            .ToArray();
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch
        {
            /* a leftover temp folder does not fail a test */
        }
    }
}
