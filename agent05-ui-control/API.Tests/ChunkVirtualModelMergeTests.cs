using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

public class ChunkVirtualModelMergeTests
{
    [Fact]
    public void Merge_preserves_cancelled_when_incoming_is_placeholder_pending()
    {
        var prev = new List<ChunkVirtualModelEntry>
        {
            new()
            {
                Index = 0,
                State = "Cancelled",
                StartedAt = "2020-01-01T00:00:00Z",
                CompletedAt = "2020-01-01T00:01:00Z",
            },
            new() { Index = 1, State = "Completed", CompletedAt = "2020-01-01T00:02:00Z" },
        };
        var live = new List<ChunkVirtualModelEntry>
        {
            new() { Index = 0, State = "Pending" },
            new() { Index = 1, State = "Pending" },
        };

        var merged = ChunkVirtualModelMerge.Merge(prev, live);

        Assert.Equal(2, merged.Count);
        Assert.Equal("Cancelled", merged[0].State);
        Assert.Equal("Completed", merged[1].State);
    }

    [Fact]
    public void Merge_takes_running_from_live_over_terminal_placeholder_conflict_unlikely_uses_live()
    {
        var prev = new List<ChunkVirtualModelEntry>
        {
            new() { Index = 0, State = "Cancelled", CompletedAt = "2020-01-01T00:00:00Z" },
        };
        var live = new List<ChunkVirtualModelEntry>
        {
            new()
            {
                Index = 0,
                State = "Running",
                StartedAt = "2020-01-02T00:00:00Z",
            },
        };

        var merged = ChunkVirtualModelMerge.Merge(prev, live);

        Assert.Single(merged);
        Assert.Equal("Running", merged[0].State);
    }

    [Fact]
    public void Merge_empty_incoming_returns_previous()
    {
        var prev = new List<ChunkVirtualModelEntry> { new() { Index = 0, State = "Failed" } };
        var merged = ChunkVirtualModelMerge.Merge(prev, Array.Empty<ChunkVirtualModelEntry>());
        Assert.Single(merged);
        Assert.Equal("Failed", merged[0].State);
    }

    [Fact]
    public void Merge_preserves_running_when_incoming_is_placeholder_pending()
    {
        var prev = new List<ChunkVirtualModelEntry>
        {
            new()
            {
                Index = 10,
                State = "Running",
                StartedAt = "2020-01-01T00:00:00Z",
                TranscriptActivityLog = "line-a",
            },
        };
        var live = new List<ChunkVirtualModelEntry>
        {
            new() { Index = 10, State = "Pending" },
        };

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
            new() { Index = 8, State = "Completed", CompletedAt = "2020-01-01T00:00:00Z" },
            new() { Index = 9, State = "Running", StartedAt = "2020-01-01T00:01:00Z" },
        };
        var live = new List<ChunkVirtualModelEntry>
        {
            new() { Index = 8, State = "Completed", CompletedAt = "2020-01-01T00:00:00Z" },
        };

        var merged = ChunkVirtualModelMerge.Merge(prev, live);

        Assert.Equal(2, merged.Count);
        Assert.Contains(merged, e => e.Index == 9 && e.State == "Running");
    }

    [Fact]
    public void Merge_combines_activity_log_when_live_extends_prev()
    {
        var prev = new List<ChunkVirtualModelEntry>
        {
            new()
            {
                Index = 0,
                State = "Running",
                StartedAt = "2020-01-01T00:00:00Z",
                TranscriptActivityLog = "a\nb",
            },
        };
        var live = new List<ChunkVirtualModelEntry>
        {
            new()
            {
                Index = 0,
                State = "Running",
                StartedAt = "2020-01-01T00:00:00Z",
                TranscriptActivityLog = "a\nb\nc",
            },
        };

        var merged = ChunkVirtualModelMerge.Merge(prev, live);

        Assert.Single(merged);
        Assert.Equal("a\nb\nc", merged[0].TranscriptActivityLog);
    }
}
