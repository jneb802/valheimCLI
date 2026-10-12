using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace valheimCLI
{
    /// <summary>Read only. The index comes from the game's own sorted live list, not a copy sorted by this pack.</summary>
    public static class TerrainModifierInspection
    {
        private static readonly FieldInfo? CreationTime = typeof(TerrainModifier).GetField(
            "m_creationTime", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public static void At(Terminal.ConsoleEventArgs args, Action<string> output)
        {
            const string usage = "Usage: cli_terrain_modifiers_at <x> <z> [radius=30]";
            if (args.Length < 3 || args.Length > 4 ||
                !CommandArguments.TryFiniteFloat(args[1], out float x) ||
                !CommandArguments.TryFiniteFloat(args[2], out float z))
            {
                output(usage);
                return;
            }
            float radius = 30f;
            if (args.Length == 4 && !CommandArguments.TryRadius(args[3], out radius))
            {
                output(usage);
                return;
            }
            if (!CommandArguments.CanScan(x, z, radius))
            {
                output("ERROR: census coordinates exceed the supported sector range");
                return;
            }
            if (ZoneSystem.instance == null || ZNetScene.instance == null)
            {
                output("ERROR: no world loaded");
                return;
            }
            if (CreationTime == null)
            {
                output("ERROR: this game build has no TerrainModifier.m_creationTime; modifier order cannot be evidenced");
                return;
            }

            List<string> rows = new List<string>();
            int index = 0;
            float radiusSquared = radius * radius;
            foreach (TerrainModifier modifier in TerrainModifier.GetAllInstances())
            {
                int order = index++;
                if (modifier == null) continue;
                Vector3 p = modifier.transform.position;
                float dx = p.x - x, dz = p.z - z;
                if (dx * dx + dz * dz > radiusSquared) continue;
                if (rows.Count == 128)
                {
                    output("ERROR: more than 128 loaded modifiers in radius; use a smaller radius for a complete ordered census");
                    return;
                }
                object? raw = CreationTime.GetValue(modifier);
                if (!(raw is long created))
                {
                    output("ERROR: TerrainModifier.m_creationTime is not a long; modifier order cannot be evidenced");
                    return;
                }
                rows.Add(string.Format(CultureInfo.InvariantCulture,
                    "MODIFIER order={0} name={1} zdo={2} pos={3:F3},{4:F3},{5:F3} player={6} sort={7} created={8} radius={9:F2} level={10} smooth={11} paint={12} enabled={13}",
                    order, RockInspectionCommands.PrefabName(modifier.gameObject), modifier.GetZDOID(),
                    p.x, p.y, p.z, modifier.m_playerModifiction, modifier.m_sortOrder, created,
                    modifier.GetRadius(), modifier.m_level, modifier.m_smooth, modifier.m_paintCleared, modifier.enabled));
            }
            foreach (string row in rows) output(row);
            output(string.Format(CultureInfo.InvariantCulture,
                "OK: TERRAIN_MODIFIERS x={0:F1} z={1:F1} radius={2:F1} count={3} live={4}",
                x, z, radius, rows.Count, index));
        }
    }
}
