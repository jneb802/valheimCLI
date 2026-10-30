using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace valheimCLI
{
    public static class DungeonInspectionCommands
    {
        public static void Register()
        {
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
    }
}
