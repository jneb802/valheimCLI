using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace valheimCLI;

internal static class WorldSpawnCommands
{
    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_world_spawn_probe",
            "Local-world test of SpawnSystem.Spawn (bypasses terrain/weather checks): <prefab> <x> <y> <z>. Reports and removes created creatures.", args =>
        {
            if (args.Length != 5 || !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(args[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
                float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) || float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z))
            {
                args.Context.AddString("ERROR: Usage: cli_world_spawn_probe <prefab> <x> <y> <z>");
                return;
            }
            if (Player.m_localPlayer == null || ZNet.instance == null || !ZNet.instance.IsServer() || ZNet.instance.GetPeers().Count != 0)
            {
                args.Context.AddString("ERROR: Requires a local world with no connected peers.");
                return;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(args[1]);
            SpawnSystem system = UnityEngine.Object.FindFirstObjectByType<SpawnSystem>();
            if (prefab == null || prefab.GetComponent<Character>() == null || system == null || SpawnSystem.m_nospawn)
            {
                args.Context.AddString("ERROR: Requires a creature prefab, loaded SpawnSystem, and spawning enabled.");
                return;
            }
            Vector3 position = new Vector3(x, y, z);
            bool hasTemple = ZoneSystem.instance.GetLocationIcon("StartTemple", out Vector3 temple);
            HashSet<Character> before = new HashSet<Character>(Character.GetAllCharacters());
            SpawnSystem.SpawnData data = new SpawnSystem.SpawnData { m_prefab = prefab, m_minLevel = 1, m_maxLevel = 1, m_spawnAtDay = true };
            try
            {
                AccessTools.Method(typeof(SpawnSystem), "Spawn").Invoke(system, new object[] { data, position, false });
                Character[] created = Character.GetAllCharacters().Where(c => !before.Contains(c) && !c.IsPlayer()).ToArray();
                args.Context.AddString(FormattableString.Invariant($"worldSpawn prefab={prefab.name} position={position:F2} created={created.Length} hasTemple={hasTemple} temple={temple:F2}"));
            }
            finally
            {
                foreach (Character character in Character.GetAllCharacters().Where(c => !before.Contains(c) && !c.IsPlayer()).ToArray())
                    ZNetScene.instance.Destroy(character.gameObject);
            }
        });
    }
}
