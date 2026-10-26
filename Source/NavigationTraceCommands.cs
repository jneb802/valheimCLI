using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace valheimCLI;

// Separate from the lightweight timing probe: detailed capture allocates and must
// not be used to estimate normal frame times. It never changes the hit buffer.
internal static class NavigationTraceCommands
{
    private static readonly Harmony Patches = new("valheimCLI.navigation-edge-trace");
    private static readonly FieldInfo HitBuffer = AccessTools.Field(typeof(Pathfinding), "tempHitArray");
    private static readonly Dictionary<string, Edge> Previous = new();
    private static readonly List<Change> Changes = new();
    private static Edge? current;
    private static bool capturing;
    private static int agentFilter;
    private static int limit;
    private static int comparisons;
    private static int unchanged;
    private static int firstObservations;
    private static int droppedEdges;
    private static string error = "";

    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_nav_trace", "Detailed bounded navigation-edge trace: start [agentType=0] [changes=32] | status | dump [name] | stop", args =>
        {
            try
            {
                string action = args.Length > 1 ? args[1] : "status";
                if (action == "start")
                {
                    int selectedAgent = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 0;
                    int selectedLimit = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 32;
                    if (selectedAgent < 0 || !Enum.IsDefined(typeof(Pathfinding.AgentType), selectedAgent) && selectedAgent != 0)
                        throw new ArgumentException("Unknown agent type; 0 means all types.");
                    if (selectedLimit < 1 || selectedLimit > 128) throw new ArgumentException("Change limit must be 1..128.");
                    Stop();
                    Previous.Clear();
                    Changes.Clear();
                    agentFilter = selectedAgent;
                    limit = selectedLimit;
                    comparisons = unchanged = firstObservations = droppedEdges = 0;
                    error = "";
                    try
                    {
                        Patches.Patch(AccessTools.Method(typeof(Pathfinding), "ConnectAlongEdge"),
                            prefix: Method(nameof(EdgeBefore)), postfix: Method(nameof(EdgeAfter)));
                        Patches.Patch(AccessTools.Method(typeof(Pathfinding), "FindGround"),
                            transpiler: Method(nameof(CaptureHitsTranspiler)));
                        Patches.Patch(AccessTools.Method(typeof(Pathfinding), "CompareLinks"),
                            postfix: Method(nameof(CompareAfter)));
                        capturing = true;
                    }
                    catch { Stop(); throw; }
                }
                else if (action == "stop") Stop();
                else if (action == "dump")
                {
                    string name = args.Length > 2 ? args[2] : "navigation-edges";
                    if (name.Length > 80 || name.Any(character => !char.IsLetterOrDigit(character) && character != '-' && character != '_'))
                        throw new ArgumentException("Use a simple file name containing letters, digits, '-' or '_'.");
                    string directory = Path.Combine(Utils.GetSaveDataPath(FileHelpers.FileSource.Local), "valheimCLI", "navigation");
                    Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, name + ".json");
                    Pathfinding navigation = Pathfinding.instance;
                    if (navigation == null) throw new InvalidOperationException("Pathfinding is unavailable.");
                    Dump dump = new()
                    {
                        utc = DateTime.UtcNow.ToString("O"),
                        agentFilter = agentFilter, comparisons = comparisons, unchanged = unchanged,
                        firstObservations = firstObservations, droppedEdges = droppedEdges, error = error,
                        layers = navigation.m_layers.value, waterLayers = navigation.m_waterLayers.value,
                        layerNames = LayerNames(navigation.m_layers.value), waterLayerNames = LayerNames(navigation.m_waterLayers.value),
                        tileSize = navigation.m_tileSize, updateInterval = navigation.m_updateInterval, tileTimeout = navigation.m_tileTimeout,
                        patches = PatchOwners(), changes = Changes.ToArray(), lastEdges = Previous.Values.ToArray()
                    };
                    StringBuilder json = new();
                    WriteJson(json, dump);
                    File.WriteAllText(path, json.ToString());
                    args.Context.AddString("Saved navigation trace: " + path);
                }
                else if (action != "status") throw new ArgumentException("Use start, status, dump, or stop.");
                args.Context.AddString($"capturing={capturing} agent={agentFilter} changes={Changes.Count}/{limit} trackedEdges={Previous.Count} comparisons={comparisons} unchanged={unchanged} firstObservations={firstObservations} droppedEdges={droppedEdges} error={error}");
            }
            catch (Exception exception) { args.Context.AddString("ERROR: " + exception.GetBaseException().Message); }
        }, isCheat: true);
    }

    internal static void Stop()
    {
        capturing = false;
        current = null;
        Patches.UnpatchSelf();
    }

    private static HarmonyMethod Method(string name) => new(typeof(NavigationTraceCommands), name);

    // Serialize only fields in these bounded snapshot records. Unity's native
    // serializer can omit custom nested record arrays in a player build.
    private static void WriteJson(StringBuilder output, object? value)
    {
        if (value == null) { output.Append("null"); return; }
        if (value is string text)
        {
            output.Append('"');
            foreach (char character in text)
            {
                if (character == '"' || character == '\\') output.Append('\\').Append(character);
                else if (character < 32) output.Append("\\u").Append(((int)character).ToString("x4"));
                else output.Append(character);
            }
            output.Append('"');
            return;
        }
        if (value is bool flag) { output.Append(flag ? "true" : "false"); return; }
        if (value is float number)
        {
            output.Append(float.IsNaN(number) || float.IsInfinity(number) ? "null" : number.ToString("R", CultureInfo.InvariantCulture));
            return;
        }
        if (value is int integer) { output.Append(integer.ToString(CultureInfo.InvariantCulture)); return; }
        bool first = true;
        if (value is IEnumerable sequence)
        {
            output.Append('[');
            foreach (object item in sequence)
            {
                if (!first) output.Append(',');
                first = false;
                WriteJson(output, item);
            }
            output.Append(']');
            return;
        }
        output.Append('{');
        foreach (FieldInfo field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!first) output.Append(',');
            first = false;
            WriteJson(output, field.Name);
            output.Append(':');
            WriteJson(output, field.GetValue(value));
        }
        output.Append('}');
    }

    private static string[] LayerNames(int mask) => Enumerable.Range(0, 32)
        .Where(index => (mask & (1 << index)) != 0).Select(LayerMask.LayerToName).ToArray();

    private static string[] PatchOwners()
    {
        List<string> result = new();
        foreach (string name in new[] { "BuildTile", "FindGround", "ConnectAlongEdge", "CompareLinks", "DestroyQueuedNavmeshData" })
        {
            HarmonyLib.Patches? patches = Harmony.GetPatchInfo(AccessTools.Method(typeof(Pathfinding), name));
            if (patches != null)
                foreach (string owner in patches.Owners) result.Add(name + ":" + owner);
        }
        return result.ToArray();
    }

    private static void EdgeBefore(Vector3 __1, Vector3 __2, object __4)
    {
        current = null;
        if (!capturing) return;
        try
        {
            int agent = Convert.ToInt32(AccessTools.Field(__4.GetType(), "m_agentType").GetValue(__4));
            if (agentFilter != 0 && agent != agentFilter) return;
            string key = FormattableString.Invariant($"{agent}:{__1.x:R},{__1.z:R}:{__2.x:R},{__2.z:R}");
            if (Previous.Count >= 256 && !Previous.ContainsKey(key)) { droppedEdges++; return; }
            NavMeshBuildSettings settings = (NavMeshBuildSettings)AccessTools.Field(__4.GetType(), "m_build").GetValue(__4);
            current = new Edge { key = key, time = Time.realtimeSinceStartup, frame = Time.frameCount,
                agent = agent, agentHeight = settings.agentHeight, agentRadius = settings.agentRadius,
                swimDepth = (float)AccessTools.Field(__4.GetType(), "m_swimDepth").GetValue(__4),
                from = __1, to = __2 };
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void EdgeAfter() => current = null;

    private static IEnumerable<CodeInstruction> CaptureHitsTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> code = instructions.ToList();
        int matches = 0;
        for (int index = 0; index < code.Count; index++)
        {
            if (code[index].operand is not MethodInfo method || method.DeclaringType != typeof(Physics) || method.Name != "RaycastNonAlloc") continue;
            // The native call's return count stays on the stack for the original
            // method. Capture a duplicate count and read only the valid buffer slice.
            CodeInstruction[] capture =
            {
                new(OpCodes.Dup), new(OpCodes.Ldarg_0), new(OpCodes.Ldarg_1), new(OpCodes.Ldarg_2),
                new(OpCodes.Call, AccessTools.Method(typeof(NavigationTraceCommands), nameof(CaptureHits)))
            };
            code.InsertRange(index + 1, capture);
            index += capture.Length;
            matches++;
        }
        if (matches != 1) throw new InvalidOperationException($"Expected one FindGround raycast; found {matches}.");
        return code;
    }

    private static void CaptureHits(int count, Pathfinding navigation, Vector3 origin, bool water)
    {
        if (current == null || !capturing) return;
        try
        {
            if (current.rays.Count >= 128) throw new InvalidOperationException("Edge exceeded the ray capture bound.");
            RaycastHit[] buffer = (RaycastHit[])HitBuffer.GetValue(navigation);
            Ray ray = new() { origin = origin, includesWater = water, bufferFull = count == buffer.Length };
            for (int index = 0; index < count; index++)
            {
                RaycastHit hit = buffer[index];
                Collider collider = hit.collider;
                if (collider == null) throw new InvalidOperationException("Raycast hit lost its collider during capture.");
                Transform transform = collider.transform;
                MeshCollider? mesh = collider as MeshCollider;
                ray.hits.Add(new Hit
                {
                    collider = collider.GetInstanceID(), name = Hierarchy(transform), type = collider.GetType().Name,
                    layer = collider.gameObject.layer, trigger = collider.isTrigger,
                    point = hit.point, normal = hit.normal, distance = hit.distance, triangle = hit.triangleIndex,
                    position = transform.position, rotation = transform.rotation, scale = transform.lossyScale,
                    boundsMin = collider.bounds.min, boundsMax = collider.bounds.max,
                    mesh = mesh != null && mesh.sharedMesh != null ? mesh.sharedMesh.GetInstanceID() : 0
                });
            }
            current.rays.Add(ray);
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static void CompareAfter(IList __0, IList __1, bool __result)
    {
        if (current == null || !capturing) return;
        try
        {
            comparisons++;
            current.points = __0.Cast<Vector3>().ToArray();
            current.oldPoints = __1.Cast<object>().Select(link => (Vector3)link.GetType().GetProperty("Key")!.GetValue(link)).ToArray();
            current.matchesExisting = __result;
            if (__result) unchanged++;
            if (Previous.TryGetValue(current.key, out Edge before))
            {
                if (!__result && current.oldPoints.Length > 0)
                {
                    Changes.Add(new Change { before = before, after = current });
                    if (Changes.Count >= limit) capturing = false;
                }
            }
            else firstObservations++;
            Previous[current.key] = current;
        }
        catch (Exception exception) { Fail(exception); }
    }

    private static string Hierarchy(Transform transform)
    {
        List<string> names = new();
        for (Transform node = transform; node != null; node = node.parent) names.Add(node.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private static void Fail(Exception exception)
    {
        error = exception.GetBaseException().Message;
        capturing = false;
        current = null;
    }

    [Serializable] private sealed class Dump
    {
        public string utc = "", error = "";
        public int agentFilter, comparisons, unchanged, firstObservations, droppedEdges, layers, waterLayers;
        public float tileSize, updateInterval, tileTimeout;
        public string[] layerNames = Array.Empty<string>(), waterLayerNames = Array.Empty<string>(), patches = Array.Empty<string>();
        public Change[] changes = Array.Empty<Change>();
        public Edge[] lastEdges = Array.Empty<Edge>();
    }
    [Serializable] private sealed class Change { public Edge before = new(), after = new(); }
    [Serializable] private sealed class Edge
    {
        public string key = "";
        public float time, agentHeight, agentRadius, swimDepth;
        public int frame, agent;
        public Vector3 from, to;
        public bool matchesExisting;
        public Vector3[] points = Array.Empty<Vector3>(), oldPoints = Array.Empty<Vector3>();
        public List<Ray> rays = new();
    }
    [Serializable] private sealed class Ray
    {
        public Vector3 origin;
        public bool includesWater, bufferFull;
        public List<Hit> hits = new();
    }
    [Serializable] private sealed class Hit
    {
        public int collider, layer, triangle, mesh;
        public string name = "", type = "";
        public bool trigger;
        public float distance;
        public Vector3 point, normal, position, scale, boundsMin, boundsMax;
        public Quaternion rotation;
    }
}
