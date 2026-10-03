using Xunit;

namespace valheimCLI.Tests;

public class TeleportTimelineTests
{
    [Fact]
    public void RecordsEachTransitionOnceAndDoesNotFinishBeforeTheGameDoes()
    {
        var trace = new TeleportTimeline();
        trace.Observe(5, false, false, false, false);
        Assert.False(trace.Started);
        trace.Observe(20, true, false, false, false);
        trace.Observe(2020, true, true, false, false);
        trace.Observe(3000, true, true, true, false);
        trace.Observe(4000, true, true, true, true);
        Assert.False(trace.Finished);
        trace.Observe(8050, false, true, true, true);
        trace.Observe(9000, false, true, true, true);
        Assert.Equal(20, trace.RequestedMs);
        Assert.Equal(2020, trace.MovedMs);
        Assert.Equal(3000, trace.AreaReadyMs);
        Assert.Equal(4000, trace.FloorReadyMs);
        Assert.Equal(8050, trace.DoneMs);
    }

    [Fact]
    public void MissingFloorStaysVisibleWhenTeleportEndsThroughFallback()
    {
        var trace = new TeleportTimeline();
        trace.Observe(10, true, false, false, false);
        trace.Observe(15000, false, true, true, false);
        Assert.Equal(-1, trace.FloorReadyMs);
        Assert.True(trace.Finished);
    }
}
