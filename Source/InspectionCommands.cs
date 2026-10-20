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
        new Terminal.ConsoleCommand("cli_components", "List loaded components and positions: <type-name> [radius=100]", args =>
        {
            if (args.Length < 2 || Player.m_localPlayer == null) return;
            float radius = 100;
            if (args.Length > 2 && (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
                float.IsNaN(radius) || radius <= 0 || radius > 20000))
            { args.Context.AddString("ERROR: Radius must be 0–20000 metres"); return; }
            foreach (MonoBehaviour component in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Where(c => c != null && (c.GetType().Name == args[1] || c.GetType().FullName == args[1]))
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
            object? value = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
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
