using Xunit;

namespace valheimCLI.Tests;

/// <summary>
/// Where a dump takes its samples. The point of the lattice is that a dumped
/// position is one the game itself asks about, so a reader offline can return
/// the value unchanged; a window that drifted off it would turn every sample
/// into an interpolation and quietly weaken every measurement made from it.
/// </summary>
public class WorldDumpGridTests
{
    [Theory]
    [InlineData(128)]  // the island grid: i*128 - 10000
    [InlineData(8)]    // road pathfinding cells: multiples of 8
    [InlineData(50)]   // the map dumps used for pictures
    public void TheFullMapRunsFromTheOriginToTheFarEdge(int step)
    {
        Assert.Equal(0, WorldDumpGrid.From(-10000f, step));
        Assert.Equal(-10000f, WorldDumpGrid.At(0, step));
        Assert.Equal(20000 / step, WorldDumpGrid.To(10000f, step));
    }

    [Fact]
    public void EightMetreSamplesLandOnThePathfindersOwnCells()
    {
        // The pathfinder puts cell centres at multiples of 8, and the origin is
        // one, so every sample of an 8 m dump is a cell centre - anywhere in
        // the world, and in a window as much as in the full map.
        for (int index = 0; index < 5; index++)
        {
            Assert.Equal(0f, WorldDumpGrid.At(index, 8) % 8f);
        }

        int from = WorldDumpGrid.From(1234f, 8);
        Assert.True(WorldDumpGrid.At(from, 8) >= 1234f);
        Assert.True(WorldDumpGrid.At(from - 1, 8) < 1234f);
        Assert.Equal(0f, WorldDumpGrid.At(from, 8) % 8f);
    }

    [Fact]
    public void A128MetreWindowKeepsTheIslandGridsOwnOffset()
    {
        // The island grid is not a multiple of 128: it is i*128 - 10000, and
        // -10000 is 112 short of a multiple. A window has to keep that offset
        // or island detection offline would read between the game's samples.
        int from = WorldDumpGrid.From(0f, 128);
        float sample = WorldDumpGrid.At(from, 128);
        Assert.Equal(112f, ((sample % 128f) + 128f) % 128f);
        Assert.True(sample >= 0f && sample < 128f);
    }

    [Fact]
    public void AWindowHoldsOnlySamplesInsideIt()
    {
        // 8 m samples between 1000 and 1064: the ends are on the lattice, so
        // both are included and nothing outside is.
        int from = WorldDumpGrid.From(1000f, 8);
        int to = WorldDumpGrid.To(1064f, 8);
        Assert.Equal(1000f, WorldDumpGrid.At(from, 8));
        Assert.Equal(1064f, WorldDumpGrid.At(to, 8));
        Assert.Equal(9, to - from + 1);

        // A window narrower than one step holds no sample at all, and the
        // caller is told rather than handed an empty file.
        Assert.True(WorldDumpGrid.To(1006f, 8) < WorldDumpGrid.From(1001f, 8));
    }

    [Theory]
    [InlineData("100,200,300", 100f, 200f, 300f)]
    [InlineData("-1234.5,0,64", -1234.5f, 0f, 64f)]
    public void AWindowIsThreeNumbers(string spec, float x, float z, float half)
    {
        Assert.True(WorldDumpGrid.TryParseWindow(spec, out float cx, out float cz, out float parsedHalf));
        Assert.Equal(x, cx);
        Assert.Equal(z, cz);
        Assert.Equal(half, parsedHalf);
    }

    [Theory]
    [InlineData("")]
    [InlineData("100,200")]
    [InlineData("100,200,300,400")]
    [InlineData("100,200,0")]
    [InlineData("100,200,-5")]
    [InlineData("a,b,c")]
    public void AnythingElseIsRefusedRatherThanGuessedAt(string spec)
    {
        Assert.False(WorldDumpGrid.TryParseWindow(spec, out float _, out float _, out float _));
    }

    [Theory]
    [InlineData(1, true, true)]
    [InlineData(4, true, true)]
    [InlineData(1000, true, true)]
    [InlineData(5, false, true)]
    [InlineData(1000, false, true)]
    [InlineData(4, false, false)]  // 1..4 m only inside a window
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    [InlineData(1001, true, false)]
    public void StepsOfOneToFourMetresNeedAWindow(int step, bool windowed, bool allowed)
    {
        Assert.Equal(allowed, WorldDumpGrid.IsAllowedStep(step, windowed));
    }

    [Fact]
    public void AOneMetreWindowIsCountedAndStillCapped()
    {
        // The issue's example: a 120 x 120 m window over a dungeon's footprint.
        int from = WorldDumpGrid.From(940f, 1), to = WorldDumpGrid.To(1060f, 1);
        Assert.True(WorldDumpGrid.TrySampleCount(from, to, from, to, 1, out long count));
        Assert.Equal(121L * 121L, count);

        // A 1,201 m square at 1 m is 1,442,401 samples: over the cap, refused before any file.
        int wideFrom = WorldDumpGrid.From(400f, 1), wideTo = WorldDumpGrid.To(1600f, 1);
        Assert.False(WorldDumpGrid.TrySampleCount(wideFrom, wideTo, wideFrom, wideTo, 1, out _));
    }

    [Theory]
    [InlineData(56.123456f)]
    [InlineData(29.95f)]       // a land line's neighbour: 0.1 m rounding would put it on 30.0
    [InlineData(-0.0001234f)]
    [InlineData(0.29914027f)]  // a base height
    [InlineData(1e-7f)]
    [InlineData(1234.5677f)]
    public void ValuesAreWrittenAtFullFloatPrecision(float value)
    {
        string text = WorldDumpGrid.Format(value);
        Assert.Equal(value, float.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain(" ", text);
        Assert.DoesNotContain(",", text); // a CSV cell, whatever the machine's culture
    }

    [Fact]
    public void TheHeaderNamesEveryColumnARowWrites()
    {
        Assert.Equal("x,z,height,biome,river,river_width,base_height,blended_height", WorldDumpGrid.Header);
        using StringWriter writer = new StringWriter();
        WorldDumpGrid.Write(writer, 0, 1, 0, 0, 8, (x, z) => Noise(x, z));
        string[] lines = writer.ToString().Split(writer.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(WorldDumpGrid.Header, lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.All(lines.Skip(1), line => Assert.Equal(8, line.Split(',').Length));
        Assert.StartsWith("-10000,-10000,", lines[1]);
        Assert.StartsWith("-9992,-10000,", lines[2]);
    }

    [Fact]
    public void AOneMetreWindowHoldsTheEightMetreDumpsNodesByteForByte()
    {
        // cli_world_dump 1 --window 1000,-2000,60 against cli_world_dump 8 --window 1000,-2000,60:
        // the 1 m dump holds every 8 m node, and each of those rows is the same text.
        string fine = Dump(1000f, -2000f, 60f, 1, out int fineSamples);
        string coarse = Dump(1000f, -2000f, 60f, 8, out int coarseSamples);
        Assert.Equal(121 * 121, fineSamples);
        Assert.Equal(15 * 15, coarseSamples);

        string[] fineLines = fine.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string[] coarseLines = coarse.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(fineLines[0], coarseLines[0]);
        HashSet<string> fineRows = new HashSet<string>(fineLines.Skip(1), StringComparer.Ordinal);
        Assert.All(coarseLines.Skip(1), row => Assert.Contains(row, fineRows));
        Assert.Equal(coarseSamples, coarseLines.Length - 1);
    }

    private static string Dump(float cx, float cz, float half, int step, out int samples)
    {
        int ixFrom = WorldDumpGrid.From(cx - half, step), ixTo = WorldDumpGrid.To(cx + half, step);
        int izFrom = WorldDumpGrid.From(cz - half, step), izTo = WorldDumpGrid.To(cz + half, step);
        using StringWriter writer = new StringWriter();
        writer.NewLine = "\n";
        samples = WorldDumpGrid.Write(writer, ixFrom, ixTo, izFrom, izTo, step, (x, z) => Noise(x, z));
        return writer.ToString();
    }

    // Swamp-like ground: a few metres of period, values that no short decimal holds.
    private static WorldDumpSample Noise(float x, float z) => new WorldDumpSample(
        30f + 3f * MathF.Sin(x * 1.37f) * MathF.Cos(z * 0.91f), "Swamp", MathF.Abs(MathF.Sin(x * 0.01f)), 40.123f,
        0.3f + 0.01f * MathF.Sin(z), 30.5f + 2.9f * MathF.Sin(x * 1.37f + 0.2f));
}
