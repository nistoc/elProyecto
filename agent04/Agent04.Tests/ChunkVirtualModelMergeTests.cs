using Agent04.Features.Transcription.Infrastructure;
using Agent04.Proto;
using Xunit;

namespace Agent04.Tests;

public class ChunkVirtualModelMergeTests
{
    private static ChunkVirtualModelEntry VmMain(int index, string state, string? started = null, string? completed = null, string? log = null) =>
        new()
        {
            ChunkIndex = index,
            State = state,
            StartedAt = started ?? "",
            CompletedAt = completed ?? "",
            TranscriptActivityLog = log ?? ""
        };

    [Fact]
    public void Merge_preserves_cancelled_when_incoming_is_placeholder_pending()
    {
        var prev = new List<ChunkVirtualModelEntry>
        {
            VmMain(0, "Cancelled", "2020-01-01T00:00:00Z", "2020-01-01T00:01:00Z"),
            VmMain(1, "Completed", completed: "2020-01-01T00:02:00Z"),
        };
        var live = new List<ChunkVirtualModelEntry>
        {
            VmMain(0, "Pending"),
            VmMain(1, "Pending"),
        };

        var merged = ChunkVirtualModelMerge.Merge(prev, live);

        Assert.Equal(2, merged.Count);
        Assert.Equal("Cancelled", merged[0].State);
        Assert.Equal("Completed", merged[1].State);
    }

    [Fact]
    public void Merge_takes_running_from_live_over_cancelled_prev()
    {
        var prev = new List<ChunkVirtualModelEntry>
        {
            VmMain(0, "Cancelled", completed: "2020-01-01T00:00:00Z"),
        };
        var live = new List<ChunkVirtualModelEntry>
        {
            new()
            {
                ChunkIndex = 0,
                State = "Running",
                StartedAt = "2020-01-02T00:00:00Z",
            },
        };

        var merged = ChunkVirtualModelMerge.Merge(prev, live);

        Assert.Single(merged);
        Assert.Equal("Running", merged[0].State);
    }

    [Fact]
    public void Merge_empty_incoming_returns_previous_cloned()
    {
        var prev = new List<ChunkVirtualModelEntry> { VmMain(0, "Failed") };
        var merged = ChunkVirtualModelMerge.Merge(prev, Array.Empty<ChunkVirtualModelEntry>());
        Assert.Single(merged);
        Assert.Equal("Failed", merged[0].State);
    }

    [Fact]
    public void Merge_preserves_running_when_incoming_is_placeholder_pending()
    {
        var prev = new List<ChunkVirtualModelEntry>
        {
            VmMain(10, "Running", "2020-01-01T00:00:00Z", log: "line-a"),
        };
        var live = new List<ChunkVirtualModelEntry> { VmMain(10, "Pending") };

        var merged = ChunkVirtualModelMerge.Merge(prev, live);

        Assert.Single(merged);
        Assert.Equal("Running", merged[0].State);
        Assert.Equal("line-a", merged[0].TranscriptActivityLog);
    }

    [Fact]
    public void Merge_appends_prev_row_missing_from_incoming()
    {
        var prev = new List<ChunkVirtualModelEntry>
        {
            VmMain(8, "Completed", completed: "2020-01-01T00:00:00Z"),
            VmMain(9, "Running", "2020-01-01T00:01:00Z"),
        };
        var live = new List<ChunkVirtualModelEntry>
        {
            VmMain(8, "Completed", completed: "2020-01-01T00:00:00Z"),
        };

        var merged = ChunkVirtualModelMerge.Merge(prev, live);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, e => e.ChunkIndex == 9 && e.State == "Running");
    }

    [Fact]
    public void Merge_combines_activity_log_when_live_extends_prev()
    {
        var prev = new List<ChunkVirtualModelEntry>
        {
            VmMain(0, "Running", "2020-01-01T00:00:00Z", log: "a\nb"),
        };
        var live = new List<ChunkVirtualModelEntry>
        {
            VmMain(0, "Running", "2020-01-01T00:00:00Z", log: "a\nb\nc"),
        };

        var merged = ChunkVirtualModelMerge.Merge(prev, live);

        Assert.Single(merged);
        Assert.Equal("a\nb\nc", merged[0].TranscriptActivityLog);
    }

    [Fact]
    public void Merge_work_state_before_client_live_keeps_rentgen_when_live_strong()
    {
        var work = new List<ChunkVirtualModelEntry>
        {
            VmMain(0, "Completed", "2020-01-01T00:00:00Z", "2020-01-01T00:01:00Z"),
        };
        var clientLive = new List<ChunkVirtualModelEntry>
        {
            new()
            {
                ChunkIndex = 0,
                State = "Completed",
                StartedAt = "2020-02-01T00:00:00Z",
                CompletedAt = "2020-02-01T00:02:00Z",
            },
        };

        var merged = ChunkVirtualModelMerge.Merge(work, clientLive);

        Assert.Single(merged);
        Assert.Equal("2020-02-01T00:00:00Z", merged[0].StartedAt);
    }

    [Fact]
    public void Merge_work_state_before_client_live_fills_when_live_is_placeholder()
    {
        var work = new List<ChunkVirtualModelEntry>
        {
            VmMain(0, "Completed", "2020-01-01T00:00:00Z", "2020-01-01T00:01:00Z"),
        };
        var clientLive = new List<ChunkVirtualModelEntry> { VmMain(0, "Pending") };

        var merged = ChunkVirtualModelMerge.Merge(work, clientLive);

        Assert.Single(merged);
        Assert.Equal("Completed", merged[0].State);
        Assert.Equal("2020-01-01T00:00:00Z", merged[0].StartedAt);
    }
}
