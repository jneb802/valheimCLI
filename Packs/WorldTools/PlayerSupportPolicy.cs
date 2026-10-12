using System;

namespace valheimCLI.Extensions
{
    internal static class PlayerSupportPolicy
    {
        internal static bool At(float x, float y, float z, float targetX, float targetY, float targetZ,
            float speed, bool grounded, bool flying, bool attached, bool dead, bool teleporting)
        {
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) ||
                float.IsNaN(z) || float.IsInfinity(z) || float.IsNaN(speed) || float.IsInfinity(speed)) return false;
            float dx = x - targetX, dz = z - targetZ;
            return dx * dx + dz * dz <= 4f && Math.Abs(y - targetY) <= .3f && speed >= 0f && speed <= .15f &&
                grounded && !flying && !attached && !dead && !teleporting;
        }
    }
}
