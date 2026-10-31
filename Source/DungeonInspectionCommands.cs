using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace valheimCLI
{
    public static class DungeonInspectionCommands
    {
        private static Dictionary<string, (int succeeded, int failed)>? attempts;
        public static void Register()
        {
            new Terminal.ConsoleCommand("cli_spawn_dungeon", "Create a NEW saved test location at the player with a dungeon seed: cli_spawn_dungeon <location> <seed>", args =>
            {
                if (Player.m_localPlayer == null || ZoneSystem.instance == null) { args.Context.AddString("ERROR: No local world/player"); return; }
                if (args.Length != 3 || !int.TryParse(args[2], out int seed) || seed == int.MinValue)
                { args.Context.AddString("ERROR: Specify a location and integer seed other than int.MinValue"); return; }
                ZoneSystem.ZoneLocation location = ZoneSystem.instance.GetLocation(args[1]);
                if (location == null) { args.Context.AddString("ERROR: Unknown location"); return; }
                Vector3 position = Player.m_localPlayer.transform.position;
                if (UnityEngine.Object.FindObjectsByType<LocationProxy>(FindObjectsSortMode.None).Any(proxy => Vector3.Distance(proxy.transform.position, position) < 100f))
                { args.Context.AddString("ERROR: Move at least 100m from existing loaded locations"); return; }
                int previousSeed = DungeonGenerator.m_forceSeed;
                UnityEngine.Random.State randomState = UnityEngine.Random.state;
                location.m_prefab.Load();
                try
                {
                    if (location.m_prefab.Asset.GetComponentsInChildren<DungeonGenerator>(true).Length != 1)
                    { args.Context.AddString("ERROR: Location must contain exactly one dungeon generator"); return; }
                    DungeonGenerator.m_forceSeed = seed;
                    attempts = new Dictionary<string, (int succeeded, int failed)>();
                    Stopwatch timer = Stopwatch.StartNew();
                    ZoneSystem.instance.SpawnLocation(location, seed, position, Quaternion.identity, ZoneSystem.SpawnMode.Full, new List<GameObject>(), true);
                    args.Context.AddString(FormattableString.Invariant($"OK: createdLocation={args[1]} dungeonSeed={seed} position={position.ToString("F3")} elapsedMs={timer.Elapsed.TotalMilliseconds:F1}"));
                    foreach (KeyValuePair<string, (int succeeded, int failed)> attempt in attempts.OrderBy(entry => entry.Key))
                        args.Context.AddString($"PLACEMENTS room={attempt.Key} succeeded={attempt.Value.succeeded} failed={attempt.Value.failed}");
                }
                finally
                {
                    DungeonGenerator.m_forceSeed = previousSeed;
                    attempts = null;
                    UnityEngine.Random.state = randomState;
                    location.m_prefab.Release();
                }
            }, isCheat: true);
            new Terminal.ConsoleCommand("cli_dungeon_state", "Inspect loaded dungeon room counts: cli_dungeon_state [radius=10000]", args =>
            {
                if (Player.m_localPlayer == null) { args.Context.AddString("ERROR: No local player"); return; }
                float radius = 10000f;
                if (args.Length > 2 || (args.Length == 2 && (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) || float.IsNaN(radius) || radius <= 0 || radius > 10000)))
                { args.Context.AddString("ERROR: Radius must be greater than 0 and at most 10000"); return; }
                DungeonGenerator[] dungeons = UnityEngine.Object.FindObjectsByType<DungeonGenerator>(FindObjectsSortMode.None)
                    .Where(dungeon => Vector3.Distance(dungeon.transform.position, Player.m_localPlayer.transform.position) <= radius)
                    .OrderBy(dungeon => Vector3.Distance(dungeon.transform.position, Player.m_localPlayer.transform.position)).ToArray();
                foreach (DungeonGenerator dungeon in dungeons)
                {
                    Room[] rooms = dungeon.GetComponentsInChildren<Room>(true);
                    string seed = dungeon.m_hasGeneratedSeed ? dungeon.m_generatedSeed.ToString(CultureInfo.InvariantCulture) : "unknown-loaded-from-save";
                    args.Context.AddString(FormattableString.Invariant($"DUNGEON name={dungeon.name} position={dungeon.transform.position.ToString("F3")} seed={seed} theme={dungeon.m_themes} rooms={rooms.Length} roomsPending={dungeon.m_roomsToLoad} maxRooms={dungeon.m_maxRooms}"));
                    foreach (IGrouping<string, Room> group in rooms.GroupBy(room => room.name).OrderBy(group => group.Key))
                        args.Context.AddString($"ROOM name={group.Key} count={group.Count()}");
                }
                args.Context.AddString($"OK: loadedDungeons={dungeons.Length}");
            }, isCheat: true);
        }

        [HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.PlaceRoom), typeof(RoomConnection), typeof(DungeonDB.RoomData), typeof(ZoneSystem.SpawnMode))]
        private static class PlacementAttempts
        {
            private static void Postfix(DungeonDB.RoomData roomData, bool __result)
            {
                if (attempts == null) return;
                string name = roomData.m_prefab.Name;
                attempts.TryGetValue(name, out (int succeeded, int failed) count);
                attempts[name] = __result ? (count.succeeded + 1, count.failed) : (count.succeeded, count.failed + 1);
            }
        }
    }
}
