using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace valheimCLI;

public static class PrefabInspectionCommands
{
    public static void Register()
    {
        new Terminal.ConsoleCommand("cli_loaded_asset_state", "Inspect loaded assets by exact name without changing them: cli_loaded_asset_state <name>", args =>
        {
            if (args.Length != 2)
            {
                args.Context.AddString("ERROR: Exact asset name is required");
                return;
            }
            UnityEngine.Object[] assets = Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
                .Where(asset => asset != null && asset.name == args[1]).ToArray();
            args.Context.AddString($"matchingAssets={assets.Length}");
            foreach (UnityEngine.Object asset in assets.Take(20))
            {
                args.Context.AddString($"name={asset.name} type={asset.GetType().FullName} instanceId={asset.GetInstanceID()} hideFlags={asset.hideFlags}");
                try
                {
                    string json = JsonUtility.ToJson(asset);
                    args.Context.AddString(json.Length > 4000 ? json.Substring(0, 4000) + " [TRUNCATED]" : json);
                }
                catch (Exception exception) { args.Context.AddString($"Serialization unavailable: {exception.GetType().Name}"); }
            }
            if (assets.Length > 20) args.Context.AddString($"TRUNCATED: {assets.Length - 20} additional assets omitted");
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_prefab_render_state", "Inspect prefab components and shared materials without changing them: cli_prefab_render_state <prefab> [copies]", args =>
        {
            if ((args.Length != 2 && (args.Length != 3 || args[2] != "copies")) || ZNetScene.instance == null)
            {
                args.Context.AddString("ERROR: Exact prefab name and a loaded world are required");
                return;
            }
            GameObject prefab = ZNetScene.instance.GetPrefab(args[1]);
            if (prefab == null)
            {
                args.Context.AddString("ERROR: Registered prefab not found");
                return;
            }
            int lines = 0;
            void Write(string value)
            {
                if (lines++ < 250) args.Context.AddString(value);
            }
            GameObject[] copies = args.Length == 3
                ? new[] { prefab }.Concat(Resources.FindObjectsOfTypeAll<GameObject>().Where(candidate => candidate.name == args[1] && !candidate.scene.IsValid())).Distinct().ToArray()
                : new[] { prefab };
            Write($"registeredId={prefab.GetInstanceID()} loadedCopies={copies.Length}");
            foreach (GameObject copy in copies.Take(8))
            {
                Write($"prefab={copy.name} instanceId={copy.GetInstanceID()}");
                foreach (Transform transform in copy.GetComponentsInChildren<Transform>(true))
                {
                    Component[] components = transform.GetComponents<Component>();
                    Write($"path={Path(transform)} components=[{string.Join(",", components.Select(component => component == null ? "MISSING" : component.GetType().FullName))}]");
                    foreach (Renderer renderer in transform.GetComponents<Renderer>())
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material == null) { Write("material=MISSING"); continue; }
                        Shader shader = material.shader;
                        Write($"material={material.name} id={material.GetInstanceID()} shader={(shader == null ? "MISSING" : shader.name)} shaderId={(shader == null ? 0 : shader.GetInstanceID())} supported={(shader != null && shader.isSupported)} queue={material.renderQueue} keywords=[{string.Join(",", material.shaderKeywords)}]");
                        foreach (string property in new[] { "_Mode", "_SrcBlend", "_DstBlend", "_ZWrite", "_Glossiness", "_Metallic" })
                            if (material.HasProperty(property)) Write(property + "=" + material.GetFloat(property).ToString(CultureInfo.InvariantCulture));
                        if (material.HasProperty("_Color")) Write("_Color=" + material.GetColor("_Color").ToString("F3"));
                    }
                }
            }
            if (copies.Length > 8) Write($"TRUNCATED: {copies.Length - 8} additional copies omitted");
            if (lines > 250) args.Context.AddString($"TRUNCATED: {lines - 250} additional lines omitted");
        }, isCheat: true);
    }

    private static string Path(Transform transform) => transform.parent == null ? transform.name : Path(transform.parent) + "/" + transform.name;
}
