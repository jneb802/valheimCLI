using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;

namespace valheimCLI;

internal static class PerformanceCommands
{
    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_performance_snapshot",
            "Count loaded scene objects and memory. This scan can cause a slow frame; exclude its sample from FPS comparisons.", args =>
        {
            Stopwatch timer = Stopwatch.StartNew();
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>()
                .Where(item => item.scene.IsValid() && item.scene.isLoaded).ToArray();
            Renderer[] renderers = SceneComponents<Renderer>();
            ParticleSystem[] particles = SceneComponents<ParticleSystem>();
            Light[] lights = SceneComponents<Light>();
            args.Context.AddString("PERFORMANCE utc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                " sceneObjects=" + objects.Length + " activeObjects=" + objects.Count(item => item.activeInHierarchy) +
                " renderers=" + renderers.Length + " enabledRenderers=" + renderers.Count(item => item.enabled && item.gameObject.activeInHierarchy) +
                " visibleRenderers=" + renderers.Count(item => item.enabled && item.isVisible) +
                " particles=" + particles.Length + " playingParticles=" + particles.Count(item => item.isPlaying) +
                " particleCount=" + particles.Sum(item => (long)item.particleCount) +
                " lights=" + lights.Length + " activeLights=" + lights.Count(item => item.enabled && item.gameObject.activeInHierarchy) +
                " characters=" + Character.GetAllCharacters().Count +
                " managedBytes=" + GC.GetTotalMemory(false) +
                " unityAllocatedBytes=" + Profiler.GetTotalAllocatedMemoryLong() +
                " unityReservedBytes=" + Profiler.GetTotalReservedMemoryLong() +
                " loadedMaterials=" + Resources.FindObjectsOfTypeAll<Material>().Length +
                " loadedMeshes=" + Resources.FindObjectsOfTypeAll<Mesh>().Length +
                " width=" + Screen.width + " height=" + Screen.height +
                " fullscreen=" + Screen.fullScreen + " quality=" + QualitySettings.GetQualityLevel() +
                " vsync=" + QualitySettings.vSyncCount + " targetFps=" + Application.targetFrameRate +
                " scanMs=" + timer.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture));
        });
    }

    private static T[] SceneComponents<T>() where T : Component =>
        Resources.FindObjectsOfTypeAll<T>().Where(item => item.gameObject.scene.IsValid() && item.gameObject.scene.isLoaded).ToArray();
}
