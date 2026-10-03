namespace valheimCLI
{
    /// <summary>Test-only thresholds. The game still decides whether a destination has loaded ground.</summary>
    internal static class TeleportTimingPolicy
    {
        internal const float MinimumMoveSeconds = 2f;
        internal const float ShortCooldownSeconds = 0.5f;
        internal const float VanillaDistantFloorSeconds = 8f;
        internal const float VanillaCooldownSeconds = 2f;

        internal static bool CompleteDistant(float elapsed, bool areaReady, bool floorReady) =>
            elapsed > MinimumMoveSeconds && elapsed < VanillaDistantFloorSeconds && areaReady && floorReady;

        internal static bool ReleaseCooldown(float elapsed) =>
            elapsed >= ShortCooldownSeconds && elapsed < VanillaCooldownSeconds;
    }
}
