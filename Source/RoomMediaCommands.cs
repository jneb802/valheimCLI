using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace valheimCLI;

internal static class RoomMediaCommands
{
    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_music_state", "Read current music and audio playback state", args =>
        {
            MusicMan manager = MusicMan.instance;
            if (manager == null) { args.Context.AddString("ERROR: No music manager"); return; }
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            AudioSource? source = typeof(MusicMan).GetField("m_musicSource", flags)?.GetValue(manager) as AudioSource;
            string? location = typeof(MusicMan).GetField("m_locationMusic", flags)?.GetValue(manager) as string;
            args.Context.AddString($"MUSIC current={manager.GetCurrentMusic()} location={location} lastLocation={manager.m_lastLocationMusic} continuous={Settings.ContinousMusic} masterMusicVolume={MusicMan.m_masterMusicVolume} listenerVolume={AudioListener.volume} listenerPaused={AudioListener.pause}");
            if (source != null)
                args.Context.AddString($"MUSIC_SOURCE playing={source.isPlaying} clip={source.clip?.name} time={source.time:F3} length={source.clip?.length:F3} volume={source.volume:F4} mute={source.mute} loop={source.loop} enabled={source.enabled} active={source.gameObject.activeInHierarchy} mixer={source.outputAudioMixerGroup?.name}");
        });
        new Terminal.ConsoleCommand("cli_water_state", "Read nearest water surfaces: cli_water_state [radius=100] [limit=5]", args =>
        {
            Player player = Player.m_localPlayer;
            float radius = 100;
            int limit = 5;
            if (player == null) { args.Context.AddString("ERROR: No local player"); return; }
            if (args.Length > 3 || args.Length > 1 && (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) || float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0 || radius > 10000) ||
                args.Length > 2 && (!int.TryParse(args[2], out limit) || limit < 1 || limit > 100))
            { args.Context.AddString("ERROR: Radius must be 0–10000 metres and limit 1–100"); return; }
            WaterVolume[] volumes = UnityEngine.Object.FindObjectsByType<WaterVolume>(FindObjectsSortMode.None)
                .Where(w => Vector3.Distance(w.transform.position, player.transform.position) <= radius)
                .OrderBy(w => Vector3.Distance(w.transform.position, player.transform.position)).ToArray();
            args.Context.AddString($"WATER count={volumes.Length} showing={Math.Min(limit, volumes.Length)}");
            foreach (WaterVolume water in volumes.Take(limit))
            {
                MeshRenderer surface = water.m_waterSurface;
                Collider collider = water.GetComponent<Collider>();
                args.Context.AddString($"WATER_VOLUME path={Path(water.transform)} position={water.transform.position:F3} scale={water.transform.lossyScale:F3} depth={water.m_forceDepth} containsPlayer={(collider != null && collider.bounds.Contains(player.transform.position))}");
                if (surface == null) { args.Context.AddString("WATER_SURFACE missing=True"); continue; }
                Mesh? mesh = surface.GetComponent<MeshFilter>()?.sharedMesh;
                args.Context.AddString($"WATER_SURFACE path={Path(surface.transform)} enabled={surface.enabled} active={surface.gameObject.activeInHierarchy} visible={surface.isVisible} forceOff={surface.forceRenderingOff} mesh={mesh?.name} vertices={mesh?.vertexCount} bounds={surface.bounds}");
                foreach (Material material in surface.sharedMaterials)
                {
                    args.Context.AddString(material == null ? "WATER_MATERIAL missing=True" : $"WATER_MATERIAL name={material.name} shader={material.shader?.name} supported={material.shader?.isSupported} queue={material.renderQueue}");
                    if (material == null) continue;
                    foreach (string property in new[] { "_WaterEdge", "_AshlandsDistance", "_AshlandsYOffset", "_VisibleMaxDistance", "_visibleMinDistance", "_WaveHeight" })
                        if (material.HasProperty(property)) args.Context.AddString($"WATER_FLOAT {property}={material.GetFloat(property):R}");
                    foreach (string property in material.GetTexturePropertyNames())
                        args.Context.AddString($"WATER_TEXTURE {property}={material.GetTexture(property)?.name ?? "null"}");
                }
            }
        });
        new Terminal.ConsoleCommand("cli_water_material_float", "Set one float on nearest water's instance material: <property> <value> [radius=100]", args =>
        {
            Player player = Player.m_localPlayer;
            float radius = 100;
            if (player == null || args.Length < 3 || args.Length > 4 ||
                !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || float.IsNaN(value) || float.IsInfinity(value) ||
                args.Length > 3 && (!float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) || float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0 || radius > 10000))
            { args.Context.AddString("ERROR: Specify a property, finite value and radius 0–10000 while in a world"); return; }
            WaterVolume? water = UnityEngine.Object.FindObjectsByType<WaterVolume>(FindObjectsSortMode.None)
                .Where(w => w.m_waterSurface != null && Vector3.Distance(w.transform.position, player.transform.position) <= radius)
                .OrderBy(w => Vector3.Distance(w.transform.position, player.transform.position)).FirstOrDefault();
            Material? shared = water?.m_waterSurface.sharedMaterial;
            if (shared == null || !shared.HasProperty(args[1])) { args.Context.AddString("ERROR: No nearby water material with that property"); return; }
            UnityEngine.Rendering.ShaderPropertyType type = shared.shader.GetPropertyType(shared.shader.FindPropertyIndex(args[1]));
            if (type != UnityEngine.Rendering.ShaderPropertyType.Float && type != UnityEngine.Rendering.ShaderPropertyType.Range)
            { args.Context.AddString("ERROR: Property is not a float"); return; }
            Material material = water!.m_waterSurface.material;
            float before = material.GetFloat(args[1]);
            material.SetFloat(args[1], value);
            args.Context.AddString($"WATER_FLOAT_SET path={Path(water.transform)} property={args[1]} before={before:R} after={material.GetFloat(args[1]):R}");
        }, isCheat: true);
    }

    private static string Path(Transform transform) => transform.parent == null ? transform.name : Path(transform.parent) + "/" + transform.name;
}
