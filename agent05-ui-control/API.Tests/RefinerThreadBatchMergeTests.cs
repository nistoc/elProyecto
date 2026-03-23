using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

public class RefinerThreadBatchMergeTests
{
    [Fact]
    public void Apply_input_ready_sets_before_pending_after()
    {
        var snap = new JobSnapshot();
        var u = new RefineStatusUpdate(
            "j1", "Running", 0, null, 1, 3, null, null, null, 1,
            "input_ready", 0, null, null,
            "before\n", null, "batch 1/3 input_ready");

        RefinerThreadBatchMerge.Apply(snap, u);

        Assert.Single(snap.RefinerThreadBatches!);
        var row = snap.RefinerThreadBatches![0];
        Assert.Equal(0, row.BatchIndex);
        Assert.Equal(3, row.TotalBatches);
        Assert.Equal("before\n", row.BeforeText);
        Assert.Null(row.AfterText);
    }

    [Fact]
    public void Apply_output_ready_updates_same_row_after()
    {
        var snap = new JobSnapshot();
        RefinerThreadBatchMerge.Apply(snap, new RefineStatusUpdate(
            "j1", "Running", 0, null, 1, 3, null, null, null, 1,
            "input_ready", 0, null, null, "in", null, null));
        RefinerThreadBatchMerge.Apply(snap, new RefineStatusUpdate(
            "j1", "Running", 33, null, 1, 3, null, null, null, 2,
            "output_ready", 0, null, null, "in", "out", null));

        Assert.Single(snap.RefinerThreadBatches!);
        Assert.Equal("in", snap.RefinerThreadBatches![0].BeforeText);
        Assert.Equal("out", snap.RefinerThreadBatches![0].AfterText);
    }

    [Fact]
    public void Apply_skips_when_batch_event_index_negative()
    {
        var snap = new JobSnapshot();
        var u = new RefineStatusUpdate(
            "j1", "Running", 10, null, 2, 5, null, null, null, 3,
            "input_ready", -1, null, null,
            "body", null, null);

        RefinerThreadBatchMerge.Apply(snap, u);

        Assert.Null(snap.RefinerThreadBatches);
    }

    [Fact]
    public void Apply_second_input_ready_same_index_keeps_after_once_output_ready_applied()
    {
        var snap = new JobSnapshot();
        RefinerThreadBatchMerge.Apply(snap, new RefineStatusUpdate(
            "j1", "Running", 0, null, 1, 3, null, null, null, 1,
            "input_ready", 0, null, null, "in", null, null));
        RefinerThreadBatchMerge.Apply(snap, new RefineStatusUpdate(
            "j1", "Running", 0, null, 1, 3, null, null, null, 2,
            "output_ready", 0, null, null, "in", "out", null));
        RefinerThreadBatchMerge.Apply(snap, new RefineStatusUpdate(
            "j1", "Running", 0, null, 1, 3, null, null, null, 3,
            "input_ready", 0, null, null, "in2", null, null));

        Assert.Single(snap.RefinerThreadBatches!);
        Assert.Equal("out", snap.RefinerThreadBatches![0].AfterText);
        Assert.Equal("in2", snap.RefinerThreadBatches![0].BeforeText);
    }
}
