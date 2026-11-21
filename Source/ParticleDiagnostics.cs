using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace valheimCLI;

// On-demand inspection only. Never use Renderer.material(s), Shader.Find, or bundle LoadAsset here:
// those can create materials or change which assets are loaded while investigating the fault.
internal static class ParticleDiagnostics
{
    internal static void Register() => new Terminal.ConsoleCommand("cli_particle_report",
        "Save live/prefab particle materials, textures and shader copies for a network object: cli_particle_report <exact-prefab> [radius=20] [label]",
        Run, isCheat: true);

    private static void Run(Terminal.ConsoleEventArgs args)
    {
        float radius = 20;
        if (args.Length < 2 || args.Length > 4 ||
            (args.Length >= 3 && (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
                float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0 || radius > 100)) ||
            (args.Length == 4 && !Regex.IsMatch(args[3], @"\A[A-Za-z0-9_-]{1,64}\z")))
        {
            args.Context.AddString("ERROR: cli_particle_report <exact-prefab> [radius 0<r<=100] [label: letters/digits/_/-; max 64]");
            return;
        }
        Player player = Player.m_localPlayer;
        if (player == null || ZNetScene.instance == null)
        { args.Context.AddString("ERROR: A loaded world and local player are required"); return; }
        GameObject prefab = ZNetScene.instance.GetPrefab(args[1]);
        if (prefab == null) { args.Context.AddString("ERROR: Registered prefab not found: " + args[1]); return; }

        Stopwatch timer = Stopwatch.StartNew();
        // The scene can contain millions of transforms from registered mod prefabs. Use the game's
        // live network-object index instead of enumerating those unrelated hierarchies.
        Transform target = ZNetScene.instance.m_instances.Values.Where(view => view != null).Select(view => view.transform)
            .Where(t => PrefabName(t.name) == args[1] &&
                Vector3.Distance(t.position, player.transform.position) <= radius)
            .OrderBy(t => Vector3.Distance(t.position, player.transform.position)).ThenBy(t => t.GetInstanceID()).FirstOrDefault();
        if (target == null) { args.Context.AddString("ERROR: No matching live network object within radius; move near the effect"); return; }

        Report report = new();
        report.Write("PARTICLE_REPORT version=1 utc=" + DateTime.UtcNow.ToString("O"));
        report.Write($"unity={Application.unityVersion} game={Application.version} graphics={SystemInfo.graphicsDeviceType} gpu={SystemInfo.graphicsDeviceName} shaderLevel={SystemInfo.graphicsShaderLevel} colorSpace={QualitySettings.activeColorSpace}");
        report.Write($"target={args[1]} position={target.position:F3} radius={radius.ToString(CultureInfo.InvariantCulture)}");
        report.Write("NOTE: Object IDs are session-local. Texture hashes cover CPU-readable raw bytes only (up to 16 MiB estimated); unreadable textures have unknown hashes. No GPU readback is performed.");
        report.Write("NOTE: Unity does not expose original bundle/mod ownership for these objects. Loaded bundle names and same-name objects are clues only; unloaded bundles are absent. No assets are loaded by this command.");
        report.Write("NOTE: The registered prefab can already have mod changes; it is not an unmodified vanilla baseline. Missing optional texture properties and unused material slots do not by themselves prove a fault.");
        report.Write($"TIMING targetLookupMs={timer.ElapsedMilliseconds}");
        foreach (BepInEx.PluginInfo plugin in Chainloader.PluginInfos.Values.OrderBy(p => p.Metadata.GUID))
            report.Write($"PLUGIN guid={plugin.Metadata.GUID} version={plugin.Metadata.Version}");
        foreach (AssetBundle bundle in AssetBundle.GetAllLoadedAssetBundles().OrderBy(b => b.name))
            report.Write("LOADED_BUNDLE " + Identity(bundle));
        if (EnvMan.instance != null)
            report.Write($"ENV current={EnvMan.instance.GetCurrentEnvironment()?.m_name} debug={EnvMan.instance.m_debugEnv}");

        long inspectStart = timer.ElapsedMilliseconds;
        InspectRoot("scene", target.gameObject, report);
        InspectRoot("registered", prefab, report);

        // SmokeSpawner emits detached objects. Inspect its GameObject references without invoking getters.
        HashSet<int> referenced = new();
        foreach (Component component in target.GetComponentsInChildren<Component>(true))
        {
            if (component == null || component.GetType().Name != "SmokeSpawner") continue;
            foreach (FieldInfo field in component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.FieldType != typeof(GameObject) || !(field.GetValue(component) is GameObject smoke)) continue;
                report.Write($"REFERENCE path={Path(component.transform, target)} field={field.Name} object={Identity(smoke)}");
                if (!referenced.Add(smoke.GetInstanceID())) continue;
                InspectRoot("smoke-reference", smoke, report);
                // Do not search all scene transforms for detached effects. Their source prefab is
                // enough to inspect renderer-backed smoke; custom instanced rendering is explicit.
                if (smoke.GetComponentsInChildren<Renderer>(true).Length == 0)
                    report.Write("SMOKE_REFERENCE has no Renderer components; custom/instanced rendering material is not resolved by this report");
            }
        }

        report.Write($"TIMING materialInspectionMs={timer.ElapsedMilliseconds - inspectStart}");
        long shaderStart = timer.ElapsedMilliseconds;
        Shader[] shaders = Resources.FindObjectsOfTypeAll<Shader>();
        foreach (string name in report.ShaderNames.OrderBy(n => n))
        {
            Shader[] matches = shaders.Where(s => s.name == name).OrderBy(s => s.GetInstanceID()).ToArray();
            report.Write($"SHADER_COPIES name={name} count={matches.Length}");
            foreach (Shader shader in matches.Take(16)) report.Write($"SHADER_COPY {Identity(shader)} supported={shader.isSupported} renderQueue={shader.renderQueue}");
            if (matches.Length > 16) report.Write("TRUNCATED shader copies");
        }
        report.Write($"TIMING shaderCopiesMs={timer.ElapsedMilliseconds - shaderStart}");
        report.Write($"INSPECTION elapsedMs={timer.ElapsedMilliseconds} renderers={report.Renderers} uniqueMaterials={report.Materials.Count}");
        string directory = System.IO.Path.Combine(Paths.ConfigPath, "valheimCLI", "diagnostics");
        Directory.CreateDirectory(directory);
        string label = args.Length == 4 ? args[3] : "particles";
        string file = System.IO.Path.Combine(directory, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + label + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
        File.WriteAllText(file, report.ToString(), Encoding.UTF8);
        string summary = $"PARTICLE_REPORT path={file} renderers={report.Renderers} materials={report.Materials.Count} missingMaterials={report.MissingMaterials} missingTextures={report.MissingTextures} unsupportedShaders={report.UnsupportedShaders} truncated={report.Truncated} elapsedMs={timer.ElapsedMilliseconds}";
        args.Context.AddString("OK: " + summary);
        valheimCLIPlugin.Log.LogInfo(summary);
    }

    private static void InspectRoot(string scope, GameObject root, Report report)
    {
        report.Write($"ROOT scope={scope} object={Identity(root)} position={root.transform.position:F3} active={root.activeInHierarchy}");
        Fireplace fire = root.GetComponent<Fireplace>();
        if (fire != null) report.Write($"FIRE wet={fire.m_wet} blocked={fire.m_blocked} highActive={fire.m_enabledObjectHigh?.activeInHierarchy} lowActive={fire.m_enabledObjectLow?.activeInHierarchy}");
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers.Take(128))
        {
            report.Renderers++;
            report.Write($"RENDERER path={Path(renderer.transform, root.transform)} type={renderer.GetType().Name} enabled={renderer.enabled} activeSelf={renderer.gameObject.activeSelf} activeHierarchy={renderer.gameObject.activeInHierarchy} propertyBlock={renderer.HasPropertyBlock()}");
            ParticleSystem particle = renderer.GetComponent<ParticleSystem>();
            if (particle != null)
            {
                ParticleSystem.MainModule main = particle.main;
                ParticleSystem.TextureSheetAnimationModule sheet = particle.textureSheetAnimation;
                report.Write($"PARTICLE playing={particle.isPlaying} emitting={particle.isEmitting} count={particle.particleCount} simulationSpace={main.simulationSpace} startColor={main.startColor.mode}:{main.startColor.colorMin:F3}:{main.startColor.colorMax:F3} startSize={main.startSize.mode}:{main.startSize.constantMin}:{main.startSize.constantMax} textureSheet={sheet.enabled} tiles={sheet.numTilesX}x{sheet.numTilesY} sheetMode={sheet.mode}");
            }
            if (renderer is ParticleSystemRenderer particleRenderer)
            {
                List<ParticleSystemVertexStream> streams = new();
                particleRenderer.GetActiveVertexStreams(streams);
                report.Write($"PARTICLE_RENDERER mode={particleRenderer.renderMode} alignment={particleRenderer.alignment} streams={string.Join(",", streams)}");
            }
            Material[] materials = renderer.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                Material material = materials[slot];
                report.Write($"SLOT index={slot} material={Identity(material)}");
                if (material == null) { report.MissingMaterials++; continue; }
                InspectMaterial(material, report);
                MaterialPropertyBlock block = new();
                renderer.GetPropertyBlock(block, slot);
                InspectBlock("slot", block, material, report);
                renderer.GetPropertyBlock(block);
                InspectBlock("renderer", block, material, report);
            }
        }
        if (renderers.Length > 128) report.Write("TRUNCATED renderers");
    }

    private static void InspectMaterial(Material material, Report report)
    {
        if (!report.Materials.Add(material.GetInstanceID())) return;
        Shader shader = material.shader;
        report.Write($"MATERIAL {Identity(material)} shader={Identity(shader)} supported={shader != null && shader.isSupported} queue={material.renderQueue} passCount={material.passCount} keywords=[{string.Join(",", material.shaderKeywords.OrderBy(k => k))}]");
        if (shader == null || !shader.isSupported) report.UnsupportedShaders++;
        if (shader == null) return;
        report.ShaderNames.Add(shader.name);
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            string property = shader.GetPropertyName(i);
            ShaderPropertyType type = shader.GetPropertyType(i);
            if (type == ShaderPropertyType.Texture)
            {
                Texture texture = material.GetTexture(property);
                if (texture == null) report.MissingTextures++;
                report.Write($"TEXTURE property={property} {TextureInfo(texture, report)} scale={material.GetTextureScale(property):F4} offset={material.GetTextureOffset(property):F4}");
            }
            else if (type == ShaderPropertyType.Color) report.Write($"PROPERTY name={property} type={type} value={material.GetColor(property):F4}");
            else if (type == ShaderPropertyType.Vector) report.Write($"PROPERTY name={property} type={type} value={material.GetVector(property):F4}");
            else if (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range)
                report.Write($"PROPERTY name={property} type={type} value={material.GetFloat(property).ToString("R", CultureInfo.InvariantCulture)}");
            else if (type == ShaderPropertyType.Int) report.Write($"PROPERTY name={property} type={type} value={material.GetInteger(property)}");
        }
    }

    private static void InspectBlock(string scope, MaterialPropertyBlock block, Material material, Report report)
    {
        if (block.isEmpty || material.shader == null) return;
        report.Write($"PROPERTY_BLOCK scope={scope} nonempty=true (slot block takes precedence over renderer block; undeclared properties are not enumerable)");
        Shader shader = material.shader;
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            string property = shader.GetPropertyName(i);
            int id = Shader.PropertyToID(property);
            if (!block.HasProperty(id)) continue;
            ShaderPropertyType type = shader.GetPropertyType(i);
            if (type == ShaderPropertyType.Texture) report.Write($"OVERRIDE {property} {TextureInfo(block.GetTexture(id), report)}");
            else if (type == ShaderPropertyType.Color || type == ShaderPropertyType.Vector) report.Write($"OVERRIDE {property}={block.GetVector(id):F4}");
            else if (type == ShaderPropertyType.Int) report.Write($"OVERRIDE {property}={block.GetInteger(id)}");
            else report.Write($"OVERRIDE {property}={block.GetFloat(id).ToString("R", CultureInfo.InvariantCulture)}");
        }
    }

    private static string TextureInfo(Texture? texture, Report report)
    {
        if (texture == null) return "MISSING (may be optional shader property)";
        if (!report.TextureHashes.TryGetValue(texture.GetInstanceID(), out string hash))
        {
            hash = "unknown:not-readable-or-too-large";
            if (texture is Texture2D image && image.isReadable && (long)image.width * image.height * 32 <= 16 * 1024 * 1024)
            {
                try
                {
                    using SHA256 sha = SHA256.Create();
                    hash = BitConverter.ToString(sha.ComputeHash(image.GetRawTextureData())).Replace("-", "").ToLowerInvariant();
                }
                catch (Exception exception) { hash = "unknown:" + exception.GetType().Name; }
            }
            report.TextureHashes[texture.GetInstanceID()] = hash;
        }
        return $"{Identity(texture)} size={texture.width}x{texture.height} dimension={texture.dimension} graphicsFormat={texture.graphicsFormat} filter={texture.filterMode} wrap={texture.wrapMode} rawSha256={hash}";
    }
    private static string Identity(Object value) => value == null ? "MISSING" : $"name=[{value.name}] id={value.GetInstanceID()} type={value.GetType().Name}";
    private static string PrefabName(string name) => name.EndsWith("(Clone)", StringComparison.Ordinal) ? name.Substring(0, name.Length - 7) : name;
    private static string Path(Transform value, Transform root) => value == root || value.parent == null ? value.name : Path(value.parent, root) + "/" + value.name;

    private sealed class Report
    {
        private readonly StringBuilder text = new();
        private int lines;
        internal readonly HashSet<int> Materials = new();
        internal readonly HashSet<string> ShaderNames = new(StringComparer.Ordinal);
        internal readonly Dictionary<int, string> TextureHashes = new();
        internal int Renderers, MissingMaterials, MissingTextures, UnsupportedShaders;
        internal bool Truncated;
        internal void Write(string line)
        {
            if (line.StartsWith("TRUNCATED", StringComparison.Ordinal)) Truncated = true;
            if (++lines > 5000 || text.Length > 1000000) { Truncated = true; return; }
            text.AppendLine(line);
        }
        public override string ToString() => text.ToString() + (Truncated ? "TRUNCATED: report limit reached\n" : "");
    }
}
