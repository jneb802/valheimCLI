using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace valheimCLI
{
    // The game update is the source of truth. One armed trace records transitions in
    // memory; a caller waits for one completion instead of querying the player remotely
    // on a timer. Times have frame precision and are relative to the arm operation.
    internal static class TeleportTrace
    {
        private static readonly FieldInfo? TargetField = typeof(Player).GetField("m_teleportTargetPos", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? DistantField = typeof(Player).GetField("m_distantTeleport", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Stopwatch Clock = new Stopwatch();
        private static int _generation;
        private static Player? _player;
        private static Vector3 _origin;
        private static Vector3 _target;
        private static TeleportTimeline _timeline = new TeleportTimeline();
        private static bool _interrupted, _distant;
        private static Vector3 _final;

        internal static bool Arm(out int generation, out string error)
        {
            generation = 0;
            error = "";
            Player? player = Player.m_localPlayer;
            if (player == null) { error = "no local player"; return false; }
            if (TargetField == null || DistantField == null)
            { error = "this game build has no auditable teleport fields"; return false; }
            if (player.IsTeleporting()) { error = "a teleport is already in progress"; return false; }
            _player = player;
            _origin = player.transform.position;
            _interrupted = _distant = false;
            _timeline = new TeleportTimeline();
            Clock.Restart();
            generation = ++_generation;
            return true;
        }

        internal static void Tick()
        {
            Player? player = _player;
            if (player == null || _timeline.Finished || _interrupted) return;
            if (Player.m_localPlayer != player)
            { _interrupted = true; return; }
            bool teleporting = player.IsTeleporting();
            if (!_timeline.Started)
            {
                if (!teleporting) return;
                // The player may have walked between arming and the game's
                // acceptance. Measure movement from the first in-flight frame.
                _origin = player.transform.position;
                _target = (Vector3)TargetField!.GetValue(player);
                _distant = (bool)DistantField!.GetValue(player);
            }
            long now = Clock.ElapsedMilliseconds;
            Vector3 p = player.transform.position;
            bool moved = (p - _origin).sqrMagnitude > 25f;
            bool areaReady = ZNetScene.instance != null && ZNetScene.instance.IsAreaReady(_target);
            bool floorReady = areaReady && ZoneSystem.instance != null && ZoneSystem.instance.FindFloor(_target, out _);
            _timeline.Observe(now, teleporting, moved, areaReady, floorReady);
            if (_timeline.Finished) _final = p;
        }

        internal static bool TryResult(int generation, out string line)
        {
            if (generation != _generation || _player == null)
            { line = "ERROR: code=teleport_trace_unknown"; return true; }
            if (!_timeline.Finished && !_interrupted) { line = ""; return false; }
            if (!_timeline.Started || _interrupted)
            { line = "ERROR: code=teleport_trace_interrupted reason=player changed before teleport"; return true; }
            line = string.Format(CultureInfo.InvariantCulture,
                "OK: TELEPORT_TRACE id={0} distant={1} requestedMs={2} movedMs={3} areaReadyMs={4} floorReadyMs={5} doneMs={6} floorAtDone={7} final={8:F2},{9:F2},{10:F2}",
                generation, _distant, _timeline.RequestedMs, _timeline.MovedMs, _timeline.AreaReadyMs, _timeline.FloorReadyMs, _timeline.DoneMs, _timeline.FloorReadyMs >= 0, _final.x, _final.y, _final.z);
            return true;
        }
    }
}
