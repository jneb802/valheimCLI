using Xunit;

namespace valheimCLI.Tests;

/// <summary>
/// cli_destroy_prefabs_at: destroy around any loaded point without moving the player, several
/// patterns per call, the radius rule of cli_destroy_nearby_prefabs, counts per pattern.
/// </summary>
public class DestroyAtRequestTests
{
    [Fact]
    public void ParsesPointPatternsAndRadius()
    {
        Assert.True(DestroyAtRequest.TryParse(Args("cli_destroy_prefabs_at 5136.5 66 -5408 YggdrasilTree,Pinetree_01 30"), out DestroyAtRequest r, out string error));
        Assert.Equal(string.Empty, error);
        Assert.Equal(5136.5f, r.X);
        Assert.Equal(66f, r.Y);
        Assert.Equal(-5408f, r.Z);
        Assert.Equal(new[] { "YggdrasilTree", "Pinetree_01" }, r.Patterns);
        Assert.Equal(30f, r.Radius);

        Assert.True(DestroyAtRequest.TryParse(Args("cli_destroy_prefabs_at 1 2 3 Beech"), out r, out _));
        Assert.Equal(DestroyAtRequest.DefaultRadius, r.Radius);
        Assert.Equal(new[] { "Beech" }, r.Patterns);
    }

    [Theory]
    [InlineData("0.1", 0.5f)]
    [InlineData("200", 60f)]
    [InlineData("12.5", 12.5f)]
    public void RadiusIsClampedAsTheNearbyCommandClampsIt(string radius, float expected)
    {
        Assert.True(DestroyAtRequest.TryParse(Args("cli_destroy_prefabs_at 1 2 3 Beech " + radius), out DestroyAtRequest r, out _));
        Assert.Equal(expected, r.Radius);
    }

    [Theory]
    [InlineData("cli_destroy_prefabs_at")]
    [InlineData("cli_destroy_prefabs_at 1 2 3")]
    [InlineData("cli_destroy_prefabs_at 1 2 Beech 10")]
    [InlineData("cli_destroy_prefabs_at x 2 3 Beech")]
    [InlineData("cli_destroy_prefabs_at 1 2 3 Beech,,Pine")]
    [InlineData("cli_destroy_prefabs_at 1 2 3 Beech, 10")]
    [InlineData("cli_destroy_prefabs_at 1 2 3 Beech wide")]
    [InlineData("cli_destroy_prefabs_at 1 2 3 Beech 10 extra")]
    [InlineData("cli_destroy_prefabs_at 1 NaN 3 Beech")]
    public void RefusesBadInput(string line)
    {
        Assert.False(DestroyAtRequest.TryParse(Args(line), out _, out string error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void ClampRadiusTurnsNotANumberIntoTheDefault()
    {
        Assert.Equal(DestroyAtRequest.DefaultRadius, DestroyAtRequest.ClampRadius(float.NaN));
        Assert.Equal(60f, DestroyAtRequest.ClampRadius(float.PositiveInfinity));
    }

    [Fact]
    public void PatternsMatchCaseInsensitivelyAndCountToTheFirst()
    {
        Assert.True(PrefabPatterns.TryParse("pine, Beech ,pine", out List<string> patterns, out _));
        Assert.Equal(new[] { "pine", "Beech" }, patterns); // trimmed, duplicates once
        Assert.Equal(0, PrefabPatterns.Match("Pinetree_01", patterns));
        Assert.Equal(1, PrefabPatterns.Match("Beech_small1", patterns));
        Assert.Equal(-1, PrefabPatterns.Match("Birch1", patterns));
        Assert.True(PrefabPatterns.TryParse("tree,Pine", out patterns, out _));
        Assert.Equal(0, PrefabPatterns.Match("Pinetree_01", patterns)); // both match: the first pattern counts it
        Assert.Equal("tree:3,Pine:0", PrefabPatterns.FormatCounts(patterns, new[] { 3, 0 }));
    }

    private static string[] Args(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
