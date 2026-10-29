using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;

namespace valheimCLI;

internal static class DungeonTraceCommands
{
    private static ConfigEntry<bool>? enabled;
    private static readonly List<DungeonGenerator> active = new();

    internal static void Register(ConfigFile config)
    {
        enabled = config.Bind("Diagnostics", "TraceDungeonGeneration", false,
            "Log dungeon generation room lists and nested room-list setup stacks. Diagnostic only; disabled by default.");
        new Terminal.ConsoleCommand("cli_dungeon_trace", "Enable dungeon generation trace: cli_dungeon_trace <true|false>", args =>
        {
            if (args.Length != 2 || !bool.TryParse(args[1], out bool value))
            {
                args.Context.AddString("Usage: cli_dungeon_trace <true|false>");
                return;
            }
            enabled.Value = value;
            args.Context.AddString("Dungeon generation trace=" + value);
        });
    }

    private static bool Enabled => enabled?.Value == true;
    private static string Identity(DungeonGenerator generator) =>
        generator.name + "#" + generator.GetInstanceID() + " theme=" + generator.m_themes + " position=" + generator.transform.position;

    // Names and themes are metadata. Do not access RoomInPrefab or Asset here:
    // either can change the very loading sequence that this trace observes.
    private static string Rooms()
    {
        List<DungeonDB.RoomData> rooms = DungeonGenerator.m_availableRooms;
        return "count=" + rooms.Count + " rooms=[" + string.Join(",", rooms.Select(room =>
            room.m_prefab.Name + ":" + room.m_theme)) + "]";
    }

    private static void Write(string message) => valheimCLIPlugin.Log.LogInfo("DUNGEON_TRACE " + message);

    [HarmonyPatch(typeof(ZoneSystem), nameof(ZoneSystem.PokeCanSpawnLocation))]
    private static class LocationReadiness
    {
        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(ZoneSystem __instance, ZoneSystem.ZoneLocation location, bool isFirstSpawn, bool __result)
        {
            if (!Enabled || !isFirstSpawn || !__result || location == null) return;
            foreach (ZoneSystem.LocationPrefabLoadData record in __instance.m_locationPrefabs)
            {
                if (record.PrefabAssetID != location.m_prefab.m_assetID) continue;
                if (!record.IsLoaded || record.m_roomsToLoad > 0)
                    Write("ALLOWED_PENDING location=" + location.m_prefabName + " loaded=" + record.IsLoaded + " roomsPending=" + record.m_roomsToLoad);
                return;
            }
        }
    }

    [HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.Generate), typeof(int), typeof(ZoneSystem.SpawnMode))]
    private static class Generation
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(DungeonGenerator __instance, int seed, ZoneSystem.SpawnMode mode, out bool __state)
        {
            __state = Enabled;
            if (!__state) return;
            active.Add(__instance);
            Write("BEGIN " + Identity(__instance) + " seed=" + seed + " mode=" + mode);
        }

        [HarmonyFinalizer]
        private static void Finalizer(DungeonGenerator __instance, bool __state, Exception? __exception)
        {
            if (!__state) return;
            Write("END " + Identity(__instance) + " exception=" + (__exception?.ToString() ?? "none"));
            active.Remove(__instance);
        }
    }

    [HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.SetupAvailableRooms))]
    private static class Setup
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(DungeonGenerator __instance)
        {
            if (!Enabled || active.Count == 0) return;
            Write("SETUP_BEGIN " + Identity(__instance) + " active=" + Identity(active[active.Count - 1]) + " " + Rooms());
            Write("SETUP_STACK " + new StackTrace(1, true));
        }

        [HarmonyPostfix, HarmonyPriority(Priority.Last)]
        private static void Postfix(DungeonGenerator __instance)
        {
            if (!Enabled || active.Count == 0) return;
            Write("SETUP_END " + Identity(__instance) + " " + Rooms());
        }
    }

    [HarmonyPatch(typeof(DungeonGenerator), nameof(DungeonGenerator.GenerateRooms))]
    private static class Selection
    {
        [HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static void Prefix(DungeonGenerator __instance)
        {
            if (!Enabled || active.Count == 0) return;
            Write("SELECT " + Identity(__instance) + " " + Rooms());
        }
    }
}
