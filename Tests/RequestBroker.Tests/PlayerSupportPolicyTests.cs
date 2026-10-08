using valheimCLI.Extensions;
using Xunit;

namespace valheimCLI.Tests;

public class PlayerSupportPolicyTests
{
    [Fact]
    public void StandingOnExpectedGroundPassesButFallAndWrongHeightDoNot()
    {
        Assert.True(PlayerSupportPolicy.At(100, 42.5f, -40, 100, 42.5f, -40, 0, true, false, false, false, false));
        Assert.False(PlayerSupportPolicy.At(100, 43f, -40, 100, 42.5f, -40, 0, true, false, false, false, false));
        Assert.False(PlayerSupportPolicy.At(100, 42.5f, -40, 100, 42.5f, -40, 2f, false, false, false, false, false));
        Assert.False(PlayerSupportPolicy.At(100, 42.5f, -40, 100, 42.5f, -40, 0, true, false, false, false, true));
    }

    [Fact]
    public void NonFinitePositionAndPlayerModesCannotPass()
    {
        Assert.False(PlayerSupportPolicy.At(float.NaN, 42, -40, 100, 42, -40, 0, true, false, false, false, false));
        Assert.False(PlayerSupportPolicy.At(100, 42, -40, 100, 42, -40, 0, true, true, false, false, false));
        Assert.False(PlayerSupportPolicy.At(100, 42, -40, 100, 42, -40, 0, true, false, true, false, false));
    }
}
