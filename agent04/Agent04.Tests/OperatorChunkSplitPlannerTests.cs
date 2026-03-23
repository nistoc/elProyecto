using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public sealed class OperatorChunkSplitPlannerTests
{
    [Fact]
    public void Plan_two_parts_overlap_1_on_100s_matches_agent01_stride()
    {
        var plan = OperatorChunkSplitPlanner.PlanEqualSegmentsWithOverlap(100.0, 2, 1.0);
        Assert.Equal(2, plan.Count);
        Assert.Equal(0.0, plan[0].StartSec, 3);
        Assert.Equal(50.5, plan[0].DurationSec, 3);
        Assert.Equal(49.5, plan[1].StartSec, 3);
        Assert.Equal(50.5, plan[1].DurationSec, 3);
    }

    [Fact]
    public void Plan_three_parts_overlap_0_is_uniform_thirds()
    {
        var plan = OperatorChunkSplitPlanner.PlanEqualSegmentsWithOverlap(90.0, 3, 0.0);
        Assert.Equal(3, plan.Count);
        Assert.Equal(0.0, plan[0].StartSec, 3);
        Assert.Equal(30.0, plan[0].DurationSec, 3);
        Assert.Equal(30.0, plan[1].StartSec, 3);
        Assert.Equal(30.0, plan[1].DurationSec, 3);
        Assert.Equal(60.0, plan[2].StartSec, 3);
        Assert.Equal(30.0, plan[2].DurationSec, 3);
    }

    [Fact]
    public void Plan_parts_below_2_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OperatorChunkSplitPlanner.PlanEqualSegmentsWithOverlap(10.0, 1, 0.0));
    }
}
