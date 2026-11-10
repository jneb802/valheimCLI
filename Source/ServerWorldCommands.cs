using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace valheimCLI;

/// <summary>Operator setup through the CLI's loopback socket on a dedicated validation server.</summary>
internal static class ServerWorldCommands
{
    internal static bool TryExecute(string command, Action<string> output)
    {
        string[] args = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (args.Length == 0 || (args[0] != "cli_server_location_add" && args[0] != "cli_server_location_shift" && args[0] != "cli_server_location_list" && args[0] != "cli_server_save" && args[0] != "cli_server_road_generate")) return false;
        if (ZNet.instance == null || !ZNet.instance.IsServer() || ZoneSystem.instance == null)
        { output("ERROR: Run this command through the CLI socket on the dedicated server"); return true; }
        if (args[0] == "cli_server_location_shift") { Shift(args, output); return true; }
        if (args[0] == "cli_server_save")
        { ZNet.instance.SaveWorldAndPlayerProfiles(); output("OK: Server world save requested; verify completion in the server log"); return true; }
        if (args[0] == "cli_server_road_generate")
        {
            Type? roads = Find("ProceduralRoads.RoadNetworkGenerator");
            if (roads == null) { output("ERROR: ProceduralRoads is absent"); return true; }
            roads.GetMethod("GenerateRoads")!.Invoke(null, new object[] { true });
            roads.GetMethod("SaveGlobalRoadData")!.Invoke(null, null);
            output("OK: Server road generation and persistence completed"); return true;
        }
        if (args[0] == "cli_server_location_list")
        {
            string filter = args.Length > 1 ? args[1] : "";
            foreach (ZoneSystem.LocationInstance location in ZoneSystem.instance.GetLocationList().Where(l =>
                l.m_location.m_prefab.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || l.m_location.m_group.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).Take(100))
                output("location=" + location.m_location.m_prefab.Name + " position=" + location.m_position.ToString("F3") + " placed=" + location.m_placed);
            return true;
        }
        if (args.Length != 4 || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) || float.IsNaN(x) || float.IsNaN(z) || Mathf.Abs(x) > 9500 || Mathf.Abs(z) > 9500)
        { output("ERROR: cli_server_location_add <registered-prefab> <x> <z>"); return true; }
        ZoneSystem zones = ZoneSystem.instance;
        ZoneSystem.ZoneLocation definition = zones.GetLocation(args[1]);
        if (definition == null || !definition.m_prefab.IsValid) { output("ERROR: Registered location prefab is unavailable"); return true; }
        Vector3 requested = new(x, 0, z);
        Vector2s zone = ZoneSystem.GetZone(requested);
        if (!zones.GetGroundHeight(requested, out _)) { output("ERROR: Load the terrain with a client before adding a location; nothing changed"); return true; }
        if (zones.m_locationInstances.ContainsKey(zone)) { output("ERROR: The zone already contains a registered location; it was preserved"); return true; }
        if (Player.GetAllPlayers().Any(p => Utils.DistanceXZ(p.transform.position, requested) < definition.m_exteriorRadius + 10))
        { output("ERROR: A player is too close to the new location"); return true; }
        if (!zones.SpawnLocationMidGame(args[1], requested, out Vector3 position)) { output("ERROR: Location registration failed"); return true; }
        // The normal mid-game registration leaves the location unplaced. Spawn it through the game's own code,
        // then mark it placed, including when its terrain zone was already generated. Never disable world saving.
        zones.SpawnLocation(definition, WorldGenerator.instance.GetSeed() ^ args[1].GetStableHashCode(), position, Quaternion.identity,
            ZoneSystem.SpawnMode.Full, new List<GameObject>());
        ZoneSystem.LocationInstance record = zones.m_locationInstances[zone]; record.m_placed = true; zones.m_locationInstances[zone] = record;
        Type? manager = Find("More_World_Locations_AIO.PortManager");
        object? instance = manager?.GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        if (instance != null) manager!.GetMethod("UpdatePortLocations")?.Invoke(instance, null);
        output("OK: Added saved location=" + args[1] + " position=" + position.ToString("F3") + " worldSavingDisabled=" + zones.m_didZoneTest);
        return true;
    }
    private static Type? Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);

    private static void Shift(string[] args, Action<string> output)
    {
        if (args.Length != 4 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) || float.IsNaN(x) || float.IsNaN(z) || Mathf.Abs(x) > 9500 || Mathf.Abs(z) > 9500)
        { output("ERROR: cli_server_location_shift <x> <z> <delta-y|ground>"); return; }
        ZoneSystem zones = ZoneSystem.instance;
        Vector2s zone = ZoneSystem.GetZone(new Vector3(x, 0, z));
        if (!zones.m_locationInstances.TryGetValue(zone, out ZoneSystem.LocationInstance record) || !record.m_placed)
        { output("ERROR: No placed registered location in this zone"); return; }
        float dy;
        if (args[3] == "ground") dy = WorldGenerator.instance.GetHeight(record.m_position.x, record.m_position.z) - record.m_position.y;
        else if (!float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out dy)) { output("ERROR: Invalid height change"); return; }
        if (float.IsNaN(dy) || Mathf.Abs(dy) > 128) { output("ERROR: Height change must be finite and no more than 128 meters"); return; }
        if (Mathf.Abs(dy) < 0.001f) { output("OK: Location is already at the requested height; nothing changed"); return; }
        ZoneSystem.ZoneLocation definition = record.m_location;
        ZDO[] proxies = ZDOMan.instance.m_objectsByID.Values.Where(d => d.GetPrefab() == "LocationProxy".GetStableHashCode() &&
            d.GetInt(ZDOVars.s_location) == definition.m_prefab.Name.GetStableHashCode() && Vector3.Distance(d.GetPosition(), record.m_position) < 0.5f).ToArray();
        if (proxies.Length != 1) { output("ERROR: Expected exactly one saved location proxy; nothing changed"); return; }
        definition.m_prefab.Load();
        try
        {
            GameObject template = definition.m_prefab.Asset;
            Quaternion rotation = proxies[0].GetRotation();
            Dictionary<int, List<Vector3>> expected = new();
            Vector3 minimum = new(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 maximum = new(float.MinValue, float.MinValue, float.MinValue);
            foreach (ZNetView view in template.GetComponentsInChildren<ZNetView>(true))
            {
                int hash = view.GetPrefabName().GetStableHashCode();
                Vector3 position = record.m_position + rotation * template.transform.InverseTransformPoint(view.transform.position);
                if (!expected.TryGetValue(hash, out List<Vector3>? positions)) expected[hash] = positions = new();
                positions.Add(position); minimum = Vector3.Min(minimum, position); maximum = Vector3.Max(maximum, position);
            }
            List<ZDO> objects = new();
            foreach (ZDO data in ZDOMan.instance.m_objectsByID.Values)
            {
                Vector3 position = data.GetPosition();
                if (position.x < minimum.x - 0.3f || position.x > maximum.x + 0.3f ||
                    position.z < minimum.z - 0.3f || position.z > maximum.z + 0.3f ||
                    position.y < minimum.y - 0.3f || position.y > maximum.y + 0.3f ||
                    !expected.TryGetValue(data.GetPrefab(), out List<Vector3>? positions)) continue;
                if (positions.Any(candidate => (candidate - position).sqrMagnitude < 0.09f)) objects.Add(data);
            }
            if (objects.Count == 0) { output("ERROR: No saved pieces match the location template; nothing changed"); return; }
            objects.Add(proxies[0]); Vector3 offset = Vector3.up * dy;
            foreach (ZDO data in objects.Distinct())
            {
                data.SetOwner(ZDOMan.GetSessionID()); data.SetPosition(data.GetPosition() + offset);
                GameObject instance = ZNetScene.instance.FindInstance(data.m_uid);
                if (instance != null) instance.transform.position = data.GetPosition();
                ZDOMan.instance.ForceSendZDO(data.m_uid);
            }
            record.m_position += offset; zones.m_locationInstances[zone] = record;
            Type? manager = Find("More_World_Locations_AIO.PortManager");
            object? current = manager?.GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            if (current != null) manager!.GetMethod("UpdatePortLocations")?.Invoke(current, null);
            output("OK: Shifted template-matched location=" + definition.m_prefab.Name + " pieces=" + objects.Count + " position=" + record.m_position.ToString("F3") + "; reload the zone before visual checks");
        }
        finally { definition.m_prefab.Release(); }
    }
}
