using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

public class JobSnapshotDiskEnricherTests
{
    [Fact]
    public void TryEnrichFromDisk_loads_transcription_work_state_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xtract-disk-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string json = """
{
  "schemaVersion": 1,
  "totalChunks": 2,
  "recoveredFromArtifacts": false,
  "chunks": [
    { "index": 0, "state": "Completed", "startedAt": "2020-01-01T00:00:00Z", "completedAt": "2020-01-01T00:01:00Z" },
    { "index": 1, "state": "Pending" }
  ]
}
""";
            File.WriteAllText(Path.Combine(dir, JobSnapshotDiskEnricher.TranscriptionWorkStateFileName), json);
            var snap = new JobSnapshot { Id = "abc", Status = "completed", Phase = "idle" };
            JobSnapshotDiskEnricher.TryEnrichFromDisk(snap, dir, NullLogger.Instance);
            Assert.NotNull(snap.Chunks);
            Assert.Equal(2, snap.Chunks!.Total);
            Assert.NotNull(snap.Chunks.ChunkVirtualModel);
            Assert.Equal(2, snap.Chunks.ChunkVirtualModel!.Count);
            Assert.Equal("Completed", snap.Chunks.ChunkVirtualModel[0].State);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryEnrichFromDisk_applies_xtract_ui_state_for_archive_snapshot()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xtract-ui-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(
                Path.Combine(dir, JobSnapshotDiskEnricher.XtractUiStateFileName),
                """{"phase":"completed","status":"done","agent04JobId":"grpc-1"}""");
            var snap = new JobSnapshot { Id = "abc", Status = "completed", Phase = "idle" };
            JobSnapshotDiskEnricher.TryEnrichFromDisk(snap, dir, NullLogger.Instance);
            Assert.Equal("completed", snap.Phase);
            Assert.Equal("done", snap.Status);
            Assert.Equal("grpc-1", snap.Agent04JobId);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryEnrichFromDisk_applies_agent06_refine_job_id_from_xtract_ui_state()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xtract-ui-agent06-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(
                Path.Combine(dir, JobSnapshotDiskEnricher.XtractUiStateFileName),
                """{"phase":"refiner_paused","status":"running","agent06RefineJobId":"deadbeef"}""");
            var snap = new JobSnapshot { Id = "abc", Status = "completed", Phase = "idle" };
            JobSnapshotDiskEnricher.TryEnrichFromDisk(snap, dir, NullLogger.Instance);
            Assert.Equal("refiner_paused", snap.Phase);
            Assert.Equal("running", snap.Status);
            Assert.Equal("deadbeef", snap.Agent06RefineJobId);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryEnrichFromDisk_loads_sub_chunk_rows_from_work_state_json()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xtract-disk-subvm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            const string json = """
{
  "schemaVersion": 2,
  "totalChunks": 1,
  "recoveredFromArtifacts": false,
  "chunks": [
    { "index": 0, "state": "Completed", "startedAt": "2020-01-01T00:00:00Z", "completedAt": "2020-01-01T00:01:00Z" },
    { "index": 0, "state": "Running", "startedAt": "2020-01-01T00:02:00Z", "isSubChunk": true, "parentChunkIndex": 0, "subChunkIndex": 1 }
  ]
}
""";
            File.WriteAllText(Path.Combine(dir, JobSnapshotDiskEnricher.TranscriptionWorkStateFileName), json);
            var snap = new JobSnapshot { Id = "abc", Status = "completed", Phase = "idle" };
            JobSnapshotDiskEnricher.TryEnrichFromDisk(snap, dir, NullLogger.Instance);
            Assert.NotNull(snap.Chunks?.ChunkVirtualModel);
            Assert.Equal(2, snap.Chunks!.ChunkVirtualModel!.Count);
            var sub = snap.Chunks.ChunkVirtualModel.First(e => e.IsSubChunk);
            Assert.Equal(0, sub.ParentChunkIndex);
            Assert.Equal(1, sub.SubChunkIndex);
            Assert.Equal("Running", sub.State);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryEnrichFromDisk_skips_refiner_threads_hydrate_when_phase_refiner_and_batches_empty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xtract-refiner-live-" + Guid.NewGuid().ToString("N"));
        var threads = Path.Combine(dir, "refiner_threads");
        Directory.CreateDirectory(threads);
        try
        {
            File.WriteAllText(
                Path.Combine(threads, "batch_0001_of_0003.json"),
                """{"batchIndex":0,"totalBatches":3,"beforeText":"a","afterText":null}""");
            var snap = new JobSnapshot
            {
                Id = "job1",
                Status = "running",
                Phase = "refiner",
                RefinerThreadBatches = Array.Empty<RefinerThreadBatchEntry>(),
            };
            JobSnapshotDiskEnricher.TryEnrichFromDisk(snap, dir, NullLogger.Instance);
            Assert.Empty(snap.RefinerThreadBatches!);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryEnrichFromDisk_hydrates_refiner_threads_when_phase_refiner_paused_and_batches_empty()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xtract-refiner-pause-" + Guid.NewGuid().ToString("N"));
        var threads = Path.Combine(dir, "refiner_threads");
        Directory.CreateDirectory(threads);
        try
        {
            File.WriteAllText(
                Path.Combine(threads, "batch_0001_of_0003.json"),
                """{"batchIndex":0,"totalBatches":3,"beforeText":"a","afterText":"done"}""");
            var snap = new JobSnapshot
            {
                Id = "job1",
                Status = "running",
                Phase = "refiner_paused",
                RefinerThreadBatches = Array.Empty<RefinerThreadBatchEntry>(),
            };
            JobSnapshotDiskEnricher.TryEnrichFromDisk(snap, dir, NullLogger.Instance);
            Assert.Single(snap.RefinerThreadBatches!);
            Assert.Equal("done", snap.RefinerThreadBatches![0].AfterText);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryEnrichFromDisk_heuristic_chunks_folder_when_no_state_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "xtract-heur-" + Guid.NewGuid().ToString("N"));
        var chunks = Path.Combine(dir, "chunks");
        Directory.CreateDirectory(chunks);
        try
        {
            File.WriteAllText(Path.Combine(chunks, "0.wav"), "");
            File.WriteAllText(Path.Combine(chunks, "1.wav"), "");
            var snap = new JobSnapshot { Id = "abc", Status = "completed", Phase = "idle" };
            JobSnapshotDiskEnricher.TryEnrichFromDisk(snap, dir, NullLogger.Instance);
            Assert.NotNull(snap.Chunks);
            Assert.Equal(2, snap.Chunks!.Total);
            Assert.NotNull(snap.Chunks.ChunkVirtualModel);
            Assert.Equal(2, snap.Chunks.ChunkVirtualModel!.Count);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }
}
