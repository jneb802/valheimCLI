using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace valheimCLI;

internal static class InspectionCommands
{
    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_raycast", "Inspect solid geometry: <x> <y> <z> <dx> <dy> <dz> [distance=20]", args =>
        {
            float[] values = new float[6];
            if (args.Length < 7 || args.Length > 8)
            { args.Context.AddString("Usage: cli_raycast <x> <y> <z> <dx> <dy> <dz> [distance=20]"); return; }
            for (int i = 0; i < values.Length; i++)
                if (!float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) ||
                    float.IsNaN(values[i]) || float.IsInfinity(values[i]))
                { args.Context.AddString("ERROR: Coordinates and direction must be finite numbers"); return; }
            float distance = 20;
            if (args.Length == 8 && (!float.TryParse(args[7], NumberStyles.Float, CultureInfo.InvariantCulture, out distance) ||
                float.IsNaN(distance) || float.IsInfinity(distance) || distance <= 0 || distance > 20000))
            { args.Context.AddString("ERROR: Distance must be 0–20000 metres"); return; }
            Vector3 origin = new(values[0], values[1], values[2]);
            Vector3 direction = new(values[3], values[4], values[5]);
            if (direction.sqrMagnitude < 0.000001f)
            { args.Context.AddString("ERROR: Direction must be nonzero"); return; }
            int mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
            RaycastHit[] hits = Physics.RaycastAll(origin, direction.normalized, distance, mask, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits.OrderBy(h => h.distance).Take(12))
                args.Context.AddString(FormattableString.Invariant(
                    $"RAY_HIT id={hit.collider.GetInstanceID()} name={hit.collider.name} point={hit.point.x:F2},{hit.point.y:F2},{hit.point.z:F2} normal={hit.normal.x:F2},{hit.normal.y:F2},{hit.normal.z:F2} distance={hit.distance:F2}"));
            args.Context.AddString("OK: solid ray hits=" + hits.Length);
        });
        new Terminal.ConsoleCommand("cli_asset_bundles", "List loaded Unity asset bundles: [name-filter]", args =>
        {
            foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles().OrderBy(b => b.name))
                if (args.Length < 2 || bundle.name.IndexOf(args[1], StringComparison.OrdinalIgnoreCase) >= 0)
                    args.Context.AddString("ASSET_BUNDLE name=" + bundle.name);
        });
        new Terminal.ConsoleCommand("cli_components", "List loaded components and positions: <type-name> [radius=100] [name-filter]", args =>
        {
            if (args.Length < 2 || Player.m_localPlayer == null) return;
            float radius = 100;
            if (args.Length > 2 && (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
                float.IsNaN(radius) || radius <= 0 || radius > 20000))
            { args.Context.AddString("ERROR: Radius must be 0–20000 metres"); return; }
            foreach (Component component in UnityEngine.Object.FindObjectsByType<Component>(FindObjectsSortMode.None)
                .Where(c => c != null && (c.GetType().Name == args[1] || c.GetType().FullName == args[1]))
                .Where(c => args.Length < 4 || c.name.IndexOf(args[3], StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(c => Vector3.Distance(c.transform.position, Player.m_localPlayer.transform.position) <= radius)
                .OrderBy(c => Vector3.Distance(c.transform.position, Player.m_localPlayer.transform.position)).Take(50))
            {
                string hover = component is Hoverable target ? target.GetHoverName() : "";
                args.Context.AddString(FormattableString.Invariant($"id={component.GetInstanceID()} type={component.GetType().FullName} name={component.name} position={component.transform.position.x:F2},{component.transform.position.y:F2},{component.transform.position.z:F2} hover={hover}"));
            }
        });
        new Terminal.ConsoleCommand("cli_component_value", "Read a field/property path: <component-instance-id> <member.path>", args =>
        {
            if (args.Length != 3 || !int.TryParse(args[1], out int id)) return;
            object? value = UnityEngine.Object.FindObjectsByType<Component>(FindObjectsSortMode.None)
                .FirstOrDefault(c => c.GetInstanceID() == id);
            if (value == null) { args.Context.AddString("ERROR: Component not loaded"); return; }
            try
            {
                foreach (string name in args[2].Split('.'))
                {
                    if (value == null) break;
                    Type type = value.GetType();
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    FieldInfo? field = type.GetField(name, flags);
                    PropertyInfo? property = type.GetProperty(name, flags);
                    if (field == null && (property == null || property.GetIndexParameters().Length != 0))
                        throw new ArgumentException("Member not found: " + name);
                    value = field != null ? field.GetValue(value) : property!.GetValue(value);
                }
                args.Context.AddString($"value={value ?? "null"}");
            }
            catch (Exception exception) { args.Context.AddString("ERROR: " + exception.GetBaseException().Message); }
        });
        new Terminal.ConsoleCommand("cli_player_custom_data", "Read saved player data by required prefix: <prefix>", args =>
        {
            if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]) || Player.m_localPlayer == null) return;
            foreach (System.Collections.Generic.KeyValuePair<string, string> entry in Player.m_localPlayer.m_customData
                .Where(pair => pair.Key.StartsWith(args[1], StringComparison.Ordinal)).OrderBy(pair => pair.Key))
                args.Context.AddString(entry.Key + "=" + entry.Value);
        });
    }
}
