using System;
using System.Reflection;
using UnityEngine;

namespace valheimCLI
{
    /// <summary>
    /// Opt-in test acceleration, restricted to a launch that declared itself a test.
    /// Mutates only the local player's two teleport clocks. The game's own next
    /// UpdateTeleport still performs its area and floor checks before completion.
    /// </summary>
    internal static class TestTeleportTiming
    {
        internal const string EnvironmentVariable = "VALHEIMCLI_TEST_FAST_TELEPORT";
        private static readonly FieldInfo? Timer = typeof(Player).GetField("m_teleportTimer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? Cooldown = typeof(Player).GetField("m_teleportCooldown", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? Target = typeof(Player).GetField("m_teleportTargetPos", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? Distant = typeof(Player).GetField("m_distantTeleport", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool _enabled;

        internal static bool Enabled => _enabled;
        internal static bool Set(bool enabled, out string error)
        {
            error = "";
            if (enabled && Environment.GetEnvironmentVariable(EnvironmentVariable) != "1")
            { error = EnvironmentVariable + " must be 1 at client launch"; return false; }
            if (enabled && (Timer == null || Cooldown == null || Target == null || Distant == null))
            { error = "this game build has no auditable teleport fields"; return false; }
            if (enabled && Player.m_localPlayer == null)
            { error = "no local player"; return false; }
            _enabled = enabled;
            return true;
        }

        internal static void Tick()
        {
            if (!_enabled) return;
            Player? player = Player.m_localPlayer;
            if (player == null) { _enabled = false; return; }
            if (!player.IsTeleporting())
            {
                float seconds = (float)Cooldown!.GetValue(player);
                if (TeleportTimingPolicy.ReleaseCooldown(seconds)) Cooldown.SetValue(player, TeleportTimingPolicy.VanillaCooldownSeconds);
                return;
            }
            if (!(bool)Distant!.GetValue(player)) return;
            float elapsed = (float)Timer!.GetValue(player);
            if (elapsed <= TeleportTimingPolicy.MinimumMoveSeconds || elapsed >= TeleportTimingPolicy.VanillaDistantFloorSeconds) return;
            Vector3 target = (Vector3)Target!.GetValue(player);
            bool areaReady = ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(target);
            bool floorReady = areaReady && ZoneSystem.instance != null && ZoneSystem.instance.FindFloor(target, out _);
            if (TeleportTimingPolicy.CompleteDistant(elapsed, areaReady, floorReady))
                Timer.SetValue(player, TeleportTimingPolicy.VanillaDistantFloorSeconds + 0.01f);
        }
    }
}
