using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace valheimCLI;

internal static class NavigationCommands
{
    private const string PatchId = "valheimCLI.navigation-probe";
    private static readonly Harmony Patches = new(PatchId);
    private static readonly Dictionary<Collider, bool> Disabled = new();
    private static readonly List<float> Frames = new();
    private static bool active;
    private static long started;
    private static long removalTicks;
    private static long maxRemovalTicks;
    private static int removalCalls;
    private static int removedLinks;
    private static int removedTiles;
    private static int maxQueue;
    private static int builds;
    private static int rebuilds;
    private static int enqueuedLinks;
    private static int changedEdges;
    private static int orderOnlyEdges;
    private static FieldInfo Links => AccessTools.Field(typeof(Pathfinding), "m_linkRemoveQueue");
    private static FieldInfo Tiles => AccessTools.Field(typeof(Pathfinding), "m_tileRemoveQueue");
    private static int Count(FieldInfo field, object instance) => ((ICollection)field.GetValue(instance)).Count;

    internal static void Register()
    {
        NavigationTraceCommands.Register();
        new Terminal.ConsoleCommand("cli_nav_probe", "Opt-in navigation timing and counters: start | reset | status | stop", args =>
        {
            try
            {
                string action = args.Length > 1 ? args[1] : "status";
                if (action == "start")
                {
                    if (!active)
                    {
                        try
                        {
                            Patch("DestroyQueuedNavmeshData", nameof(RemovalBefore), nameof(RemovalAfter));
                            Patch("BuildTile", nameof(BuildBefore), null);
                            Patch("RebuildLinks", nameof(RebuildBefore), nameof(RebuildAfter));
                            Patch("CompareLinks", null, nameof(CompareAfter));
                            active = true;
                        }
                        catch { Patches.UnpatchSelf(); throw; }
                    }
                    Reset();
                }
                else if (action == "reset") Reset();
                else if (action != "status" && action != "stop") throw new ArgumentException("Use start, reset, status, or stop.");
                args.Context.AddString(Status());
                if (action == "stop") { Patches.UnpatchSelf(); active = false; }
            }
            catch (Exception exception) { args.Context.AddString("ERROR: " + exception.GetBaseException().Message); }
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_colliders_box", "Inspect or reversibly disable solid colliders: inspect|disable x y z sizeX sizeY sizeZ; restore", args =>
        {
            try
            {
                if (args.Length == 2 && args[1] == "restore")
                {
                    int count = Disabled.Count;
                    Restore();
                    args.Context.AddString($"Restored {count} collider references.");
                    return;
                }
                if (args.Length != 8 || (args[1] != "inspect" && args[1] != "disable")) throw new ArgumentException("Use inspect|disable x y z sizeX sizeY sizeZ, or restore.");
                float[] values = Enumerable.Range(2, 6).Select(index => float.Parse(args[index], CultureInfo.InvariantCulture)).ToArray();
                if (values.Any(value => float.IsNaN(value) || float.IsInfinity(value)) || values.Skip(3).Any(value => value <= 0 || value > 12000)) throw new ArgumentException("Finite coordinates and positive sizes <= 12000 required.");
                Bounds box = new(new Vector3(values[0], values[1], values[2]), new Vector3(values[3], values[4], values[5]));
                Collider[] colliders = Physics.OverlapBox(box.center, box.extents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)
                    .Where(collider => collider.enabled && collider.GetComponentInParent<Character>() == null).Distinct().ToArray();
                args.Context.AddString($"Solid colliders={colliders.Length} box={box} savedDisabled={Disabled.Count}");
                foreach (IGrouping<string, Collider> group in colliders.GroupBy(collider => $"yBand={Mathf.FloorToInt(collider.bounds.center.y / 100) * 100} layer={LayerMask.LayerToName(collider.gameObject.layer)} root={RootName(collider.transform)}").OrderBy(group => group.Key))
                    args.Context.AddString($"{group.Key} count={group.Count()}");
                if (args[1] == "disable")
                {
                    // Require the full bounds to fit. Do not disable a terrain or room collider
                    // that only touches the selection from outside it.
                    Collider[] selected = colliders.Where(collider => box.Contains(collider.bounds.min) && box.Contains(collider.bounds.max)).ToArray();
                    if (Player.m_localPlayer != null && box.Contains(Player.m_localPlayer.transform.position)) throw new InvalidOperationException("Move the player outside the selected box before disabling its colliders.");
                    foreach (Collider collider in selected)
                    {
                        if (!Disabled.ContainsKey(collider)) Disabled.Add(collider, collider.enabled);
                        collider.enabled = false;
                    }
                    Physics.SyncTransforms();
                    args.Context.AddString($"Disabled {selected.Length}; excluded {colliders.Length - selected.Length} crossing selection bounds; total saved={Disabled.Count}");
                }
            }
            catch (Exception exception) { args.Context.AddString("ERROR: " + exception.GetBaseException().Message); }
        }, isCheat: true);
    }

    private static string RootName(Transform transform)
    {
        while (transform.parent != null) transform = transform.parent;
        return transform.name;
    }

    internal static void Update()
    {
        if (active && Player.m_localPlayer != null && Frames.Count < 600000) Frames.Add(Time.unscaledDeltaTime);
    }

    internal static void Shutdown()
    {
        NavigationTraceCommands.Stop();
        Patches.UnpatchSelf();
        active = false;
        Restore();
    }

    private static void Restore()
    {
        foreach (KeyValuePair<Collider, bool> entry in Disabled)
            if (entry.Key != null) entry.Key.enabled = entry.Value;
        Disabled.Clear();
        Physics.SyncTransforms();
    }

    private static void Patch(string method, string? prefix, string? postfix)
    {
        MethodInfo target = AccessTools.Method(typeof(Pathfinding), method) ?? throw new MissingMethodException(typeof(Pathfinding).FullName, method);
        Patches.Patch(target, prefix == null ? null : new HarmonyMethod(typeof(NavigationCommands), prefix), postfix == null ? null : new HarmonyMethod(typeof(NavigationCommands), postfix));
    }

    private static void Reset()
    {
        started = Stopwatch.GetTimestamp();
        removalTicks = maxRemovalTicks = 0;
        removalCalls = removedLinks = removedTiles = maxQueue = builds = rebuilds = enqueuedLinks = changedEdges = orderOnlyEdges = 0;
        Frames.Clear();
    }

    private static string Status()
    {
        double elapsed = started == 0 ? 0 : (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
        float[] sorted = Frames.OrderBy(value => value).ToArray();
        double seconds = Frames.Sum(value => (double)value);
        float p99 = sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(sorted.Length * 0.99))] * 1000;
        Pathfinding navigation = Pathfinding.instance;
        return FormattableString.Invariant($"active={active} elapsedSeconds={elapsed:F3} frames={Frames.Count} fps={(seconds > 0 ? Frames.Count / seconds : 0):F3} frameP99Ms={p99:F3} removalCalls={removalCalls} removalTotalMs={removalTicks * 1000.0 / Stopwatch.Frequency:F3} removalMaxMs={maxRemovalTicks * 1000.0 / Stopwatch.Frequency:F3} removedLinks={removedLinks} removedTiles={removedTiles} maxLinkQueue={maxQueue} currentLinkQueue={(navigation != null ? Count(Links, navigation) : 0)} builds={builds} linkRebuilds={rebuilds} enqueuedLinks={enqueuedLinks} changedEdges={changedEdges} orderOnlyChangedEdges={orderOnlyEdges}");
    }

    private struct RemovalState { internal long Start; internal int Links; internal int Tiles; }
    private static void RemovalBefore(Pathfinding __instance, out RemovalState __state)
    {
        __state = new RemovalState { Links = Count(Links, __instance), Tiles = Count(Tiles, __instance), Start = Stopwatch.GetTimestamp() };
        maxQueue = Math.Max(maxQueue, __state.Links);
    }
    private static void RemovalAfter(Pathfinding __instance, RemovalState __state)
    {
        long ticks = Stopwatch.GetTimestamp() - __state.Start;
        removalTicks += ticks;
        maxRemovalTicks = Math.Max(maxRemovalTicks, ticks);
        removalCalls++;
        removedLinks += Math.Max(0, __state.Links - Count(Links, __instance));
        removedTiles += Math.Max(0, __state.Tiles - Count(Tiles, __instance));
    }
    private static void BuildBefore() => builds++;
    private static void RebuildBefore(Pathfinding __instance, out int __state) => __state = Count(Links, __instance);
    private static void RebuildAfter(Pathfinding __instance, int __state)
    {
        rebuilds++;
        int count = Count(Links, __instance);
        enqueuedLinks += Math.Max(0, count - __state);
        maxQueue = Math.Max(maxQueue, count);
    }
    private static void CompareAfter(IList __0, IList __1, bool __result)
    {
        if (__result) return;
        changedEdges++;
        if (__0.Count != __1.Count || __0.Count == 0) return;
        // This extra check only runs on changed, equal-sized lists while the probe is enabled.
        List<Vector3> remaining = __0.Cast<Vector3>().ToList();
        foreach (object link in __1)
        {
            Vector3 point = (Vector3)link.GetType().GetProperty("Key")!.GetValue(link);
            int index = remaining.FindIndex(candidate => candidate == point);
            if (index < 0) return;
            remaining.RemoveAt(index);
        }
        orderOnlyEdges++;
    }
}
