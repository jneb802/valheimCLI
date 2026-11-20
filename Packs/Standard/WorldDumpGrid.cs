using System;
using System.Globalization;
using System.IO;

namespace valheimCLI
{
    /// <summary>
    /// Where a world dump takes its samples, and how a window is asked for.
    ///
    /// The game samples its island grid at i*128 - 10000 and a road pathfinder
    /// walks cells at multiples of 8, and 10000 is a whole number of 8s, so one
    /// origin serves both: a sample at -10000 + i*step is a position the game
    /// itself asks about. A reader offline can then return that value unchanged
    /// instead of interpolating toward it, which is the difference between a
    /// dump that reproduces the world and one that approximates it.
    ///
    /// Plain .NET on purpose: this is the part of the dump that has to be right,
    /// and it is tested without the game.
    /// </summary>
    internal static class WorldDumpGrid
    {
        public const float Origin = -10000f;

        /// <summary>
        /// The dump's columns. <c>height</c> is WorldGenerator.GetHeight (the point's own biome);
        /// <c>blended_height</c> is the ground HeightmapBuilder builds there before terrain
        /// modifiers (its four corner biomes mixed). They differ wherever a 64 m heightmap's corners
        /// are not all one biome.
        /// </summary>
        public const string Header = "x,z,height,biome,river,river_width,base_height,blended_height";

        /// <summary>The finest step: the game's heightmap vertices are 1 m apart.</summary>
        public const int MinimumStep = 1;

        /// <summary>
        /// The finest step without a window. Steps of 1 m to 4 m resolve the few-metre noise
        /// of Swamp and Black Forest ground that an 8 m lattice aliases, and only a window keeps
        /// them under <see cref="MaximumSamples"/>.
        /// </summary>
        public const int MinimumWholeMapStep = 5;

        public const int MaximumStep = 1000;

        /// <summary>The most samples one dump writes: it runs on the game thread.</summary>
        public const long MaximumSamples = 1000000;

        /// <summary>The one refusal text for a step <see cref="IsAllowedStep"/> refuses.</summary>
        public static readonly string StepRule = string.Format(CultureInfo.InvariantCulture,
            "step must be {0}..{2} metres, or {1}..{2} metres with --window", MinimumWholeMapStep, MinimumStep, MaximumStep);

        /// <summary>Whether a dump may use this step; 1 m to 4 m need a window.</summary>
        public static bool IsAllowedStep(int step, bool windowed) =>
            step >= (windowed ? MinimumStep : MinimumWholeMapStep) && step <= MaximumStep;

        /// <summary>
        /// A generator value as the dump and cli_generator_at write it: the shortest text that
        /// parses back to the same float, in the invariant culture. A rounded value flips a
        /// threshold rule offline wherever the true value lies within the rounding of it.
        /// The game runs on Mono, whose "R" for a float is not documented to round-trip, so a
        /// text that does not parse back exactly falls back to "G9", which always does.
        /// </summary>
        public static string Format(float value)
        {
            string text = value.ToString("R", CultureInfo.InvariantCulture);
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                && parsed.Equals(value))
            {
                return text;
            }
            return value.ToString("G9", CultureInfo.InvariantCulture);
        }

        /// <summary>First lattice index at or above a coordinate.</summary>
        public static int From(float coordinate, int step) =>
            (int)Math.Ceiling((coordinate - Origin) / step);

        /// <summary>Last lattice index at or below a coordinate.</summary>
        public static int To(float coordinate, int step) =>
            (int)Math.Floor((coordinate - Origin) / step);

        /// <summary>The world coordinate of a lattice index.</summary>
        public static float At(int index, int step) => Origin + index * step;

        /// <summary>
        /// A window as "cx,cz,half". Rejected rather than guessed at when it is
        /// not three numbers with a positive half-size.
        /// </summary>
        public static bool TryParseWindow(string spec, out float centerX, out float centerZ, out float half)
        {
            centerX = 0f;
            centerZ = 0f;
            half = 0f;

            if (string.IsNullOrWhiteSpace(spec))
            {
                return false;
            }

            string[] parts = spec.Split(',');
            if (parts.Length != 3)
            {
                return false;
            }

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out centerX)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out centerZ)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out half)
                || !IsFinite(centerX) || !IsFinite(centerZ) || !IsFinite(half)
                || Math.Abs(centerX) > 1000000f || Math.Abs(centerZ) > 1000000f
                || half <= 0f || half > 20000f)
            {
                return false;
            }

            return true;
        }
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        // The exporter runs on the game thread: refuse accidentally huge work
        // before creating files. Use a window for fine-grained samples.
        internal static bool TrySampleCount(int xFrom, int xTo, int zFrom, int zTo, int step, bool windowed, out long count)
        {
            count = 0;
            if (!IsAllowedStep(step, windowed) || xTo < xFrom || zTo < zFrom)
            {
                return false;
            }
            long width = (long)xTo - xFrom + 1;
            long height = (long)zTo - zFrom + 1;
            if (width > MaximumSamples || height > MaximumSamples || width > MaximumSamples / height)
            {
                return false;
            }
            count = width * height;
            return true;
        }

        /// <summary>
        /// Writes the header and one row per lattice node, z outer and x inner. A row depends
        /// only on the node's position and the sampler, never on the step or the window, so a
        /// node two dumps of one world share is the same bytes in both.
        /// </summary>
        internal static int Write(TextWriter writer, int ixFrom, int ixTo, int izFrom, int izTo, int step,
            Func<float, float, WorldDumpSample> sample)
        {
            writer.WriteLine(Header);
            int samples = 0;
            for (int iz = izFrom; iz <= izTo; iz++)
            {
                float z = At(iz, step);
                for (int ix = ixFrom; ix <= ixTo; ix++)
                {
                    float x = At(ix, step);
                    WriteRow(writer, x, z, sample(x, z));
                    samples++;
                }
            }
            return samples;
        }

        private static void WriteRow(TextWriter writer, float x, float z, WorldDumpSample sample)
        {
            writer.Write(Format(x));
            writer.Write(',');
            writer.Write(Format(z));
            writer.Write(',');
            writer.Write(Format(sample.Height));
            writer.Write(',');
            writer.Write(sample.Biome);
            writer.Write(',');
            writer.Write(Format(sample.River));
            writer.Write(',');
            writer.Write(Format(sample.RiverWidth));
            writer.Write(',');
            writer.Write(Format(sample.BaseHeight));
            writer.Write(',');
            writer.WriteLine(Format(sample.BlendedHeight));
        }
    }

    /// <summary>The generator's facts at one dump node, before formatting.</summary>
    internal readonly struct WorldDumpSample
    {
        public WorldDumpSample(float height, string biome, float river, float riverWidth, float baseHeight, float blendedHeight)
        {
            Height = height;
            Biome = biome;
            River = river;
            RiverWidth = riverWidth;
            BaseHeight = baseHeight;
            BlendedHeight = blendedHeight;
        }

        public float Height { get; }
        public string Biome { get; }
        public float River { get; }
        public float RiverWidth { get; }
        public float BaseHeight { get; }
        public float BlendedHeight { get; }
    }
}
