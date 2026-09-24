using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using HarmonyLib;
using UnityEngine;

namespace valheimCLI
{
    [HarmonyPatch]
    internal static class TerrainDiagnostics
    {
        internal static bool TraceEnabled;

        internal static void Register()
        {
            new Terminal.ConsoleCommand("cli_terrain_snapshot", "Read terrain height and stored terrain data: cli_terrain_snapshot <x> <z>", args =>
            {
                if (args.Length != 3 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                    !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
                    float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z))
                {
                    args.Context.AddString("Usage: cli_terrain_snapshot <x> <z>");
                    return;
                }
                if (ZDOMan.instance == null || WorldGenerator.instance == null)
                {
                    args.Context.AddString("ERROR: No world is loaded.");
                    return;
                }
                Vector3 position = new Vector3(x, 0f, z);
                Heightmap heightmap = Heightmap.FindHeightmap(position);
                string height = heightmap == null ? "unloaded" : ZoneSystem.instance.GetGroundHeight(position).ToString("R", CultureInfo.InvariantCulture);
                args.Context.AddString($"TERRAIN x={x} z={z} biome={WorldGenerator.instance.GetBiome(x, z)} height={height} base={WorldGenerator.instance.GetHeight(x, z)}");
                int prefab = "_TerrainCompiler".GetStableHashCode();
                ZDO[] records = ZDOMan.instance.m_objectsByID.Values.Where(record => record.GetPrefab() == prefab &&
                    Math.Abs(record.GetPosition().x - x) <= 64f && Math.Abs(record.GetPosition().z - z) <= 64f).ToArray();
                args.Context.AddString($"TERRAIN records={records.Length} scope=locally-known");
                foreach (ZDO record in records)
                {
                    byte[] data = record.GetByteArray(ZDOVars.s_TCData);
                    string hash = "none";
                    if (data != null)
                    {
                        using SHA256 sha = SHA256.Create();
                        hash = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
                    }
                    args.Context.AddString($"TC id={record.m_uid} pos={record.GetPosition()} revision={record.DataRevision} bytes={data?.Length ?? 0} sha256={hash}");
                }
            });
            new Terminal.ConsoleCommand("cli_terrain_trace", "Enable terrain creation/destruction logs: cli_terrain_trace <true|false>", args =>
            {
                if (args.Length != 2 || !bool.TryParse(args[1], out bool enabled))
                {
                    args.Context.AddString("Usage: cli_terrain_trace <true|false>");
                    return;
                }
                TraceEnabled = enabled;
                args.Context.AddString($"OK: terrain trace={enabled}");
            });
        }

        [HarmonyPatch(typeof(TerrainComp), "Awake"), HarmonyPrefix]
        private static void OnAwake(TerrainComp __instance)
        {
            if (!TraceEnabled) return;
            ZDO? record = __instance.GetComponent<ZNetView>()?.GetZDO();
            valheimCLIPlugin.Log.LogInfo($"TERRAIN_TRACE awake id={record?.m_uid} pos={__instance.transform.position} bytes={record?.GetByteArray(ZDOVars.s_TCData)?.Length ?? 0}");
        }

        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Destroy)), HarmonyPrefix]
        private static void OnDestroy(GameObject go)
        {
            if (!TraceEnabled || go == null || go.GetComponent<TerrainComp>() == null) return;
            ZDO? record = go.GetComponent<ZNetView>()?.GetZDO();
            valheimCLIPlugin.Log.LogInfo($"TERRAIN_TRACE destroy id={record?.m_uid} pos={go.transform.position} stack={Environment.StackTrace}");
        }
    }
}
