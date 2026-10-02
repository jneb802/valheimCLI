using System;
using System.Collections.Generic;

namespace valheimCLI
{
    /// <summary>Bounds a read-only world-generator point probe before any terrain work runs.</summary>
    internal static class GeneratorPointRequest
    {
        internal const int MaximumPoints = 16;

        internal static bool TryParse(string[] args, out List<float[]> points)
        {
            points = new List<float[]>();
            if (!CommandArguments.TryPoints(args, 1, 2, out points) || points.Count > MaximumPoints)
            {
                points.Clear();
                return false;
            }
            foreach (float[] point in points)
            {
                if (Math.Abs(point[0]) > 10000f || Math.Abs(point[1]) > 10000f)
                {
                    points.Clear();
                    return false;
                }
            }
            return true;
        }
    }
}
