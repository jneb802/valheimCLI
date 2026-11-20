using System;
using System.Collections.Generic;
using System.Globalization;

namespace valheimCLI
{
    /// <summary>
    /// Prefab-name patterns as cli_destroy_nearby_prefabs matches them (a case-insensitive part of the
    /// prefab name), several to a call as a comma list. An object that matches more than one pattern
    /// counts to the first.
    /// </summary>
    public static class PrefabPatterns
    {
        /// <summary>Splits a comma list; refuses an empty list or an empty entry ("a,,b").</summary>
        public static bool TryParse(string text, out List<string> patterns, out string error)
        {
            patterns = new List<string>();
            error = string.Empty;
            foreach (string part in text.Split(','))
            {
                string pattern = part.Trim();
                if (pattern.Length == 0)
                {
                    error = "ERROR: empty pattern in '" + text + "'";
                    return false;
                }

                if (!patterns.Exists(p => p.Equals(pattern, StringComparison.OrdinalIgnoreCase))) patterns.Add(pattern);
            }

            return true;
        }

        /// <summary>The index of the first pattern that <paramref name="prefabName"/> contains, or -1.</summary>
        public static int Match(string prefabName, IReadOnlyList<string> patterns)
        {
            for (int i = 0; i < patterns.Count; i++)
            {
                if (prefabName.IndexOf(patterns[i], StringComparison.OrdinalIgnoreCase) >= 0) return i;
            }

            return -1;
        }

        /// <summary>"a:3,b:0": the count for every pattern, in the order given.</summary>
        public static string FormatCounts(IReadOnlyList<string> patterns, IReadOnlyList<int> counts)
        {
            List<string> parts = new List<string>(patterns.Count);
            for (int i = 0; i < patterns.Count; i++) parts.Add(patterns[i] + ":" + counts[i].ToString(CultureInfo.InvariantCulture));
            return string.Join(",", parts);
        }
    }

    /// <summary>A parsed <c>cli_destroy_prefabs_at</c> request.</summary>
    public sealed class DestroyAtRequest
    {
        public const string Usage = "Usage: cli_destroy_prefabs_at <x> <y> <z> <pattern[,pattern...]> [radius=15]";
        /// <summary>The radius limits cli_destroy_nearby_prefabs clamps to.</summary>
        public const float MinimumRadius = 0.5f;
        public const float MaximumRadius = 60f;
        public const float DefaultRadius = 15f;

        public float X { get; private set; }
        public float Y { get; private set; }
        public float Z { get; private set; }
        public List<string> Patterns { get; private set; } = new List<string>();
        public float Radius { get; private set; } = DefaultRadius;

        public static bool TryParse(IReadOnlyList<string> args, out DestroyAtRequest request, out string error)
        {
            request = new DestroyAtRequest();
            error = Usage;
            if (args.Count < 5 || args.Count > 6) return false;
            if (!CommandArguments.TryFiniteFloat(args[1], out float x) ||
                !CommandArguments.TryFiniteFloat(args[2], out float y) ||
                !CommandArguments.TryFiniteFloat(args[3], out float z))
            {
                error = "ERROR: x y z must be numbers. " + Usage;
                return false;
            }

            if (!PrefabPatterns.TryParse(args[4], out List<string> patterns, out error)) return false;
            request.X = x;
            request.Y = y;
            request.Z = z;
            request.Patterns = patterns;
            if (args.Count == 6)
            {
                if (!CommandArguments.TryFiniteFloat(args[5], out float radius))
                {
                    error = "ERROR: radius must be a number. " + Usage;
                    return false;
                }

                request.Radius = ClampRadius(radius);
            }

            error = string.Empty;
            return true;
        }

        /// <summary>The radius rule of cli_destroy_nearby_prefabs: clamped to 0.5..60 m.</summary>
        public static float ClampRadius(float radius) => radius < MinimumRadius ? MinimumRadius : (radius > MaximumRadius ? MaximumRadius : radius);
    }
}
