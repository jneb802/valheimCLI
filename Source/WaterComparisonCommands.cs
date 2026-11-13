using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace valheimCLI;

internal static class WaterComparisonCommands
{
    private static WaterVolume? selected;
    private static Material? originalMaterial;
    private static Material? testMaterial;
    private static bool originalEnabled;
    private static Transform? root;
    private static Vector3 rootPosition;
    private static Transform? waterRoot;
    private static Vector3 waterScale;
    private static GameObject? comparison;
    private static GameObject? pool;
    private static Material? floorMaterial;
    private static readonly Dictionary<EnvZone, string> environments = new();

    internal static void Register() => new Terminal.ConsoleCommand("cli_water_compare",
        "Reversible local water test: select [radius=100] | pool <prefab> <x> <y> <z> | state | float <property> <value> | native <prefab> | original | environment <name> | scale <x> <y> <z> | move <dx> <dy> <dz> | reset", Run, isCheat: true);

    private static float Number(string value)
    {
        float number = float.Parse(value, CultureInfo.InvariantCulture);
        if (float.IsNaN(number) || float.IsInfinity(number)) throw new ArgumentException("Numbers must be finite");
        return number;
    }

    private static Vector3 Vector(Terminal.ConsoleEventArgs args) => new(Number(args[2]), Number(args[3]), Number(args[4]));

    private static void Run(Terminal.ConsoleEventArgs args)
    {
        try
        {
            if (args.Length < 2) throw new ArgumentException("Specify an action");
            string action = args[1];
            if (action == "reset") { Reset(); args.Context.AddString("WATER_COMPARE restored"); return; }
            if (action == "pool")
            {
                if (args.Length != 6 || Player.m_localPlayer == null) throw new ArgumentException("pool <prefab> <x> <y> <z> while in a world");
                GameObject prefab = WaterPrefab(args[2]);
                if (prefab.GetComponentInChildren<ZNetView>(true) != null) throw new ArgumentException("Pool prefab must have no network components");
                Vector3 position = new(Number(args[3]), Number(args[4]), Number(args[5]));
                Material? floorSource = ZNetScene.instance.GetPrefab("stone_floor_2x2")?.GetComponentInChildren<MeshRenderer>(true)?.sharedMaterial;
                if (floorSource == null || !floorSource.shader.isSupported) throw new InvalidOperationException("Native stone floor material unavailable");
                Reset();
                pool = new GameObject("CLI_WaterPool");
                pool.transform.position = position;
                GameObject water = UnityEngine.Object.Instantiate(prefab, pool.transform);
                water.transform.localPosition = Vector3.zero;
                water.transform.localRotation = Quaternion.identity;
                water.transform.localScale = new Vector3(14, 1, 16);
                water.SetActive(true);
                selected = water.GetComponentInChildren<WaterVolume>(true);
                PrepareSelection();
                selected.m_waterSurface.material.SetFloat("_WaterEdge", 30000);
                floorMaterial = new Material(floorSource);
                GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "PoolFloor";
                floor.transform.SetParent(pool.transform, false);
                floor.transform.localPosition = new Vector3(0, -1, 0);
                floor.transform.localScale = new Vector3(18, 0.2f, 20);
                floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                for (int i = 0; i < 3; i++)
                {
                    GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    marker.name = "DepthMarker";
                    marker.transform.SetParent(pool.transform, false);
                    marker.transform.localPosition = new Vector3(-4 + i * 4, -0.5f, 0);
                    marker.transform.localScale = new Vector3(1, 1.5f, 1);
                    marker.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                }
            }
            if (action == "select")
            {
                Player player = Player.m_localPlayer;
                if (player == null) throw new InvalidOperationException("No local player");
                float radius = args.Length > 2 ? Number(args[2]) : 100;
                if (radius <= 0 || radius > 10000) throw new ArgumentException("Radius must be 0–10000");
                Reset();
                selected = UnityEngine.Object.FindObjectsByType<WaterVolume>(FindObjectsSortMode.None)
                    .Where(w => w.m_waterSurface != null && Vector3.Distance(w.transform.position, player.transform.position) <= radius)
                    .OrderBy(w => Vector3.Distance(w.transform.position, player.transform.position)).FirstOrDefault();
                if (selected == null) throw new InvalidOperationException("No water in radius");
                PrepareSelection();
            }
            if (selected == null || root == null || waterRoot == null || testMaterial == null)
                throw new InvalidOperationException("Select water first");
            if (action == "float")
            {
                if (args.Length != 4) throw new ArgumentException("float <property> <value>");
                Material material = (comparison != null ? comparison.GetComponentInChildren<WaterVolume>() : selected).m_waterSurface.material;
                int index = material.shader.FindPropertyIndex(args[2]);
                if (index < 0) throw new ArgumentException("Unknown shader property");
                UnityEngine.Rendering.ShaderPropertyType type = material.shader.GetPropertyType(index);
                if (type != UnityEngine.Rendering.ShaderPropertyType.Float && type != UnityEngine.Rendering.ShaderPropertyType.Range)
                    throw new ArgumentException("Property is not a float");
                float before = material.GetFloat(args[2]);
                material.SetFloat(args[2], Number(args[3]));
                args.Context.AddString($"WATER_COMPARE_FLOAT {args[2]} before={before:R} after={material.GetFloat(args[2]):R}");
            }
            else if (action == "native")
            {
                if (args.Length != 3) throw new ArgumentException("native <prefab>");
                GameObject prefab = WaterPrefab(args[2]);
                RemoveComparison();
                comparison = UnityEngine.Object.Instantiate(prefab, waterRoot.position, waterRoot.rotation);
                comparison.name = "CLI_WaterComparison";
                comparison.transform.localScale = waterRoot.lossyScale;
                comparison.SetActive(true);
                WaterVolume water = comparison.GetComponentInChildren<WaterVolume>(true);
                Material material = water.m_waterSurface.material;
                if (material.HasProperty("_WaterEdge")) material.SetFloat("_WaterEdge", testMaterial.GetFloat("_WaterEdge"));
                foreach (Collider collider in comparison.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                selected.m_waterSurface.enabled = false;
            }
            else if (action == "original") RemoveComparison();
            else if (action == "environment")
            {
                if (args.Length != 3 || EnvMan.instance.GetEnv(args[2]) == null) throw new ArgumentException("Specify an existing environment");
                foreach (EnvZone zone in root.GetComponentsInChildren<EnvZone>(true))
                {
                    if (!environments.ContainsKey(zone)) environments.Add(zone, zone.m_environment);
                    zone.m_environment = args[2];
                }
            }
            else if (action == "scale")
            {
                if (args.Length != 5) throw new ArgumentException("scale <x> <y> <z>");
                Vector3 scale = Vector(args);
                if (scale.x <= 0 || scale.y <= 0 || scale.z <= 0) throw new ArgumentException("Scale must be positive");
                RemoveComparison();
                waterRoot.localScale = scale;
            }
            else if (action == "move")
            {
                if (args.Length != 5) throw new ArgumentException("move <dx> <dy> <dz> relative to original room position");
                RemoveComparison();
                root.position = rootPosition + Vector(args);
            }
            else if (action != "select" && action != "state" && action != "pool") throw new ArgumentException("Unknown action: " + action);
            WaterVolume target = comparison != null ? comparison.GetComponentInChildren<WaterVolume>() : selected;
            Material active = target.m_waterSurface.sharedMaterial;
            args.Context.AddString($"WATER_COMPARE selectedId={selected.GetInstanceID()} selectedPosition={selected.transform.position:F3} root={root.name} rootPosition={root.position:F3} source={(comparison != null ? "native" : "original")} scale={waterRoot.lossyScale:F3} depth={target.m_forceDepth:R} shader={active.shader.name} shaderId={active.shader.GetInstanceID()}");
            if (action == "state")
            {
                args.Context.AddString("WATER_COMPARE_VOLUMES count=" + root.GetComponentsInChildren<WaterVolume>(true).Length);
                args.Context.AddString("WATER_COMPARE_KEYWORDS enabled=" + string.Join(",", active.shaderKeywords) + " available=" + string.Join(",", active.shader.keywordSpace.keywordNames));
                for (int i = 0; i < active.shader.GetPropertyCount(); i++)
                {
                    string name = active.shader.GetPropertyName(i);
                    UnityEngine.Rendering.ShaderPropertyType type = active.shader.GetPropertyType(i);
                    if (type == UnityEngine.Rendering.ShaderPropertyType.Float || type == UnityEngine.Rendering.ShaderPropertyType.Range)
                        args.Context.AddString($"WATER_COMPARE_PROPERTY {name}={active.GetFloat(name):R}");
                    else if (type == UnityEngine.Rendering.ShaderPropertyType.Color || type == UnityEngine.Rendering.ShaderPropertyType.Vector)
                        args.Context.AddString($"WATER_COMPARE_PROPERTY {name}={active.GetVector(name):F4}");
                    else if (type == UnityEngine.Rendering.ShaderPropertyType.Texture)
                        args.Context.AddString($"WATER_COMPARE_TEXTURE {name}={active.GetTexture(name)?.name ?? "null"}");
                }
            }
        }
        catch (Exception exception) { args.Context.AddString("ERROR: " + exception.GetBaseException().Message); }
    }

    private static GameObject WaterPrefab(string name)
    {
        Type? manager = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Jotunn.Managers.PrefabManager")).FirstOrDefault(t => t != null);
        object? instance = manager?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
        GameObject? prefab = manager?.GetMethod("GetPrefab", new[] { typeof(string) })?.Invoke(instance, new object[] { name }) as GameObject;
        if (prefab == null || prefab.GetComponentsInChildren<WaterVolume>(true).Length != 1)
            throw new ArgumentException("Specify a prefab with exactly one water volume: " + name);
        return prefab;
    }

    private static void PrepareSelection()
    {
        originalMaterial = selected!.m_waterSurface.sharedMaterial;
        if (originalMaterial == null) { selected = null; throw new InvalidOperationException("No surface material"); }
        originalEnabled = selected.m_waterSurface.enabled;
        Material copy = new(originalMaterial);
        selected.m_waterSurface.sharedMaterial = copy;
        testMaterial = selected.m_waterSurface.material;
        if (copy != testMaterial) UnityEngine.Object.Destroy(copy);
        waterRoot = selected.transform.parent ?? selected.transform;
        waterScale = waterRoot.localScale;
        root = waterRoot.root;
        rootPosition = root.position;
    }

    private static void RemoveComparison()
    {
        if (comparison != null)
        {
            foreach (WaterVolume water in comparison.GetComponentsInChildren<WaterVolume>(true))
                if (water.m_waterSurface != null) UnityEngine.Object.Destroy(water.m_waterSurface.sharedMaterial);
            comparison.SetActive(false);
            UnityEngine.Object.Destroy(comparison);
            comparison = null;
        }
        if (selected != null) selected.m_waterSurface.enabled = originalEnabled;
    }

    private static void Reset()
    {
        RemoveComparison();
        if (root != null) root.position = rootPosition;
        if (waterRoot != null) waterRoot.localScale = waterScale;
        foreach (KeyValuePair<EnvZone, string> pair in environments)
            if (pair.Key != null) pair.Key.m_environment = pair.Value;
        environments.Clear();
        if (selected != null)
        {
            Material active = selected.m_waterSurface.sharedMaterial;
            selected.m_waterSurface.sharedMaterial = originalMaterial;
            if (active != originalMaterial && active != testMaterial) UnityEngine.Object.Destroy(active);
        }
        if (testMaterial != null) UnityEngine.Object.Destroy(testMaterial);
        selected = null;
        root = null;
        waterRoot = null;
        originalMaterial = null;
        testMaterial = null;
        if (pool != null) UnityEngine.Object.Destroy(pool);
        if (floorMaterial != null) UnityEngine.Object.Destroy(floorMaterial);
        pool = null;
        floorMaterial = null;
    }
}
