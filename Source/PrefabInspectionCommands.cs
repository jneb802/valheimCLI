using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace valheimCLI;

public static class PrefabInspectionCommands
{
    public static void Register()
    {
        new Terminal.ConsoleCommand("cli_prefab_render_state", "Inspect registered prefab components and shared materials without changing them: cli_prefab_render_state <prefab>", args =>
        {
            if (args.Length != 2 || ZNetScene.instance == null)
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
            Write($"prefab={prefab.name} instanceId={prefab.GetInstanceID()}");
            foreach (Transform transform in prefab.GetComponentsInChildren<Transform>(true))
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
            if (lines > 250) args.Context.AddString($"TRUNCATED: {lines - 250} additional lines omitted");
        }, isCheat: true);
    }

    private static string Path(Transform transform) => transform.parent == null ? transform.name : Path(transform.parent) + "/" + transform.name;
}
