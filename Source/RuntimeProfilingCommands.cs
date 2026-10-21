using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Unity.Profiling;
using UnityEngine;

namespace valheimCLI;

internal static class RuntimeProfilingCommands
{
    private static readonly Dictionary<string, ProfilerRecorder> recorders = new();
    private static readonly HashSet<string> timings = new();
    private static readonly Dictionary<GameObject, Presentation> hidden = new();

    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_profile_start", "Record runtime frame counters until cli_profile_stop (4096 samples per counter).", args =>
        {
            Dispose();
            Add("Main Thread", ProfilerCategory.Internal, true);
            Add("Render Thread", ProfilerCategory.Internal, true);
            Add("Draw Calls Count", ProfilerCategory.Render, false);
            Add("Batches Count", ProfilerCategory.Render, false);
            Add("SetPass Calls Count", ProfilerCategory.Render, false);
            Add("Triangles Count", ProfilerCategory.Render, false);
            Add("Vertices Count", ProfilerCategory.Render, false);
            args.Context.AddString("PROFILE started counters=" + string.Join(",", recorders.Keys));
        });
        new Terminal.ConsoleCommand("cli_profile_stop", "Report and dispose runtime frame counters. Time counters are milliseconds; others are counts.", args =>
        {
            try
            {
                foreach (KeyValuePair<string, ProfilerRecorder> entry in recorders)
                {
                    ProfilerRecorder recorder = entry.Value;
                    recorder.Stop();
                    double divisor = timings.Contains(entry.Key) ? 1000000d : 1d;
                    double[] samples = recorder.ToArray().Select(sample => sample.Value / divisor).OrderBy(value => value).ToArray();
                    if (samples.Length == 0) { args.Context.AddString("PROFILE counter=" + entry.Key + " samples=0"); continue; }
                    args.Context.AddString(string.Format(CultureInfo.InvariantCulture,
                        "PROFILE counter={0} unit={1} samples={2} mean={3:F3} p95={4:F3} max={5:F3}",
                        entry.Key, timings.Contains(entry.Key) ? "ms" : "count", samples.Length,
                        samples.Average(), samples[(int)Math.Ceiling(samples.Length * .95) - 1], samples[samples.Length - 1]));
                }
            }
            finally { Dispose(); }
        });
        new Terminal.ConsoleCommand("cli_scene_presentation", "Toggle test scene visuals/effects while preserving colliders: <exact-name> <x> <y> <z> <radius> <true|false>.", args =>
        {
            if (args.Length != 7) throw new ArgumentException("Expected exact-name x y z radius true|false");
            Vector3 center = new(Parse(args[2]), Parse(args[3]), Parse(args[4]));
            float radius = Parse(args[5]);
            bool active = bool.Parse(args[6]);
            GameObject[] matches = Roots(args[1]).Where(item => Vector3.Distance(item.transform.position, center) <= radius).ToArray();
            foreach (GameObject item in matches)
            {
                if (!active && !hidden.ContainsKey(item)) hidden.Add(item, new Presentation(item));
                if (active && hidden.TryGetValue(item, out Presentation presentation))
                { presentation.Restore(); hidden.Remove(item); }
                args.Context.AddString("SCENE presentation=" + active + " name=" + item.name + " position=" + item.transform.position);
            }
            args.Context.AddString("SCENE matched=" + matches.Length);
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_scene_counts", "Count objects and enabled behaviours under exact matching scene roots: <exact-name>", args =>
        {
            if (args.Length != 2) throw new ArgumentException("Expected exact-name");
            foreach (GameObject root in Roots(args[1]))
            {
                MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
                ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
                args.Context.AddString("SCENE name=" + root.name + " position=" + root.transform.position +
                    " active=" + root.activeInHierarchy + " objects=" + root.GetComponentsInChildren<Transform>(true).Length +
                    " renderers=" + renderers.Length + " enabledRenderers=" + renderers.Count(item => item.enabled && item.gameObject.activeInHierarchy) +
                    " colliders=" + colliders.Length + " enabledColliders=" + colliders.Count(item => item.enabled && item.gameObject.activeInHierarchy) +
                    " particles=" + particles.Length + " playingParticles=" + particles.Count(item => item.isPlaying) +
                    " playingAudio=" + root.GetComponentsInChildren<AudioSource>(true).Count(item => item.isPlaying));
                foreach (IGrouping<string, MonoBehaviour> group in behaviours.Where(item => item != null && item.isActiveAndEnabled).GroupBy(item => item.GetType().FullName))
                    args.Context.AddString("BEHAVIOUR type=" + group.Key + " active=" + group.Count());
            }
        });
    }

    private static float Parse(string value) => float.Parse(value, CultureInfo.InvariantCulture);
    private static GameObject[] Roots(string name) => Resources.FindObjectsOfTypeAll<GameObject>()
        .Where(item => item.name == name && item.transform.parent == null && item.scene.IsValid() && item.scene.isLoaded).ToArray();

    private static void Add(string name, ProfilerCategory category, bool timing)
    {
        ProfilerRecorder recorder = ProfilerRecorder.StartNew(category, name, 4096);
        if (!recorder.Valid) { recorder.Dispose(); return; }
        recorders.Add(name, recorder);
        if (timing) timings.Add(name);
    }

    internal static void Dispose()
    {
        foreach (ProfilerRecorder recorder in recorders.Values) recorder.Dispose();
        recorders.Clear();
        timings.Clear();
    }

    internal static void RestorePresentation()
    {
        foreach (Presentation presentation in hidden.Values) presentation.Restore();
        hidden.Clear();
    }

    private sealed class Presentation
    {
        private readonly Renderer[] renderers;
        private readonly Behaviour[] behaviours;
        private readonly ParticleSystem[] particles;
        private readonly AudioSource[] audio;

        internal Presentation(GameObject root)
        {
            renderers = root.GetComponentsInChildren<Renderer>(true).Where(item => item.enabled).ToArray();
            behaviours = root.GetComponentsInChildren<Behaviour>(true)
                .Where(item => item.enabled && (item is MonoBehaviour || item is Light || item is Animator)).ToArray();
            particles = root.GetComponentsInChildren<ParticleSystem>(true).Where(item => item.isPlaying).ToArray();
            audio = root.GetComponentsInChildren<AudioSource>(true).Where(item => item.isPlaying).ToArray();
            foreach (Renderer item in renderers) item.enabled = false;
            foreach (Behaviour item in behaviours) item.enabled = false;
            foreach (ParticleSystem item in particles) item.Pause(false);
            foreach (AudioSource item in audio) item.Pause();
        }

        internal void Restore()
        {
            foreach (Renderer item in renderers) if (item != null) item.enabled = true;
            foreach (Behaviour item in behaviours) if (item != null) item.enabled = true;
            foreach (ParticleSystem item in particles) if (item != null) item.Play(false);
            foreach (AudioSource item in audio) if (item != null) item.UnPause();
        }
    }
}
