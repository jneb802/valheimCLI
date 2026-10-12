using Xunit;

namespace valheimCLI.Tests;

public class TeleportTimingPolicyTests
{
    [Theory]
    [InlineData(1.99f, true, true, false)]
    [InlineData(2.01f, false, true, false)]
    [InlineData(2.01f, true, false, false)]
    [InlineData(2.01f, true, true, true)]
    [InlineData(7.99f, true, true, true)]
    [InlineData(8.01f, true, true, false)]
    public void DistantFloorOnlyShortensAfterMovementAndLoadedGround(float elapsed, bool area, bool floor, bool expected)
        => Assert.Equal(expected, TeleportTimingPolicy.CompleteDistant(elapsed, area, floor));

    [Theory]
    [InlineData(0.49f, false)]
    [InlineData(0.50f, true)]
    [InlineData(1.99f, true)]
    [InlineData(2.00f, false)]
    public void CooldownNeverEndsBeforeHalfASecond(float elapsed, bool expected)
        => Assert.Equal(expected, TeleportTimingPolicy.ReleaseCooldown(elapsed));
}
