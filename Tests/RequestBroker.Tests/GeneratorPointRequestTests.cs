using Xunit;

namespace valheimCLI.Tests;

public sealed class GeneratorPointRequestTests
{
    [Fact]
    public void OneOrMorePairsAreAcceptedInOrder()
    {
        Assert.True(GeneratorPointRequest.TryParse(new[] { "cli_generator_at", "-10000", "10000", "8.5", "-16" }, out var points));
        Assert.Equal(2, points.Count);
        Assert.Equal(new[] { -10000f, 10000f }, points[0]);
        Assert.Equal(new[] { 8.5f, -16f }, points[1]);
    }

    [Fact]
    public void MissingInvalidOrOutOfWorldPointsRefuse()
    {
        string[][] invalid =
        {
            new[] { "cli_generator_at" },
            new[] { "cli_generator_at", "1" },
            new[] { "cli_generator_at", "NaN", "0" },
            new[] { "cli_generator_at", "10001", "0" },
            new[] { "cli_generator_at", "1", "2", "3" }
        };
        foreach (string[] words in invalid)
        {
            Assert.False(GeneratorPointRequest.TryParse(words, out var points));
            Assert.Empty(points);
        }
    }

    [Fact]
    public void MoreThanSixteenPointsRefuseWithoutPartialCapture()
    {
        var words = new[] { "cli_generator_at" }.Concat(Enumerable.Repeat(new[] { "0", "0" }, 17).SelectMany(pair => pair)).ToArray();
        Assert.False(GeneratorPointRequest.TryParse(words, out var points));
        Assert.Empty(points);
    }
}
