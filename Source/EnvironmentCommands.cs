using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace valheimCLI
{
    internal static class EnvironmentCommands
    {
        internal static void Register()
        {
            new Terminal.ConsoleCommand("cli_environment_state", "Print environment state and nearby environment volumes: cli_environment_state [radius]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                float radius = 150;
                if (args.Length > 1 && (!float.TryParse(args[1], out radius) || radius <= 0))
                {
                    args.Context.AddString("Usage: cli_environment_state [positive radius]");
                    return;
                }
                Player player = Player.m_localPlayer;
                EnvMan manager = EnvMan.instance;
                if (player == null || manager == null)
                {
                    args.Context.AddString("ERROR: Local player and environment manager are required");
                    return;
                }
                BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                string forced = typeof(EnvMan).GetField("m_forceEnv", flags)?.GetValue(manager) as string ?? "";
                EnvSetup? current = typeof(EnvMan).GetField("m_currentEnv", flags)?.GetValue(manager) as EnvSetup;
                EnvZone? triggered = typeof(EnvZone).GetField("s_triggered", flags)?.GetValue(null) as EnvZone;
                args.Context.AddString($"ENVIRONMENT position={player.transform.position:F2} effective={manager.GetCurrentEnvironment()?.m_name} weather={current?.m_name} forced={forced} debug={manager.m_debugEnv} triggered={(triggered ? Path(triggered.transform) : "none")} sleeping={player.GetComponent<Rigidbody>()?.IsSleeping()}");
                if (!string.IsNullOrEmpty(manager.m_debugEnv))
                    args.Context.AddString("ENVIRONMENT_NOTE: Debug weather prevents environment zones from applying their forced environment. Use resetenv to restore normal zone behavior.");
                foreach (EnvZone zone in UnityEngine.Object.FindObjectsByType<EnvZone>(FindObjectsSortMode.None).Where(z => Vector3.Distance(z.transform.position, player.transform.position) <= radius).OrderBy(z => Vector3.Distance(z.transform.position, player.transform.position)))
                {
                    Collider[] colliders = zone.GetComponents<Collider>();
                    args.Context.AddString($"ENV_ZONE id={zone.GetInstanceID()} path={Path(zone.transform)} environment={zone.m_environment} force={zone.m_force} enabled={zone.enabled} position={zone.transform.position:F2} scale={zone.transform.lossyScale:F2} layer={LayerMask.LayerToName(zone.gameObject.layer)} colliders={colliders.Length}");
                    foreach (Collider collider in colliders)
                        args.Context.AddString($"ENV_COLLIDER type={collider.GetType().Name} enabled={collider.enabled} trigger={collider.isTrigger} containsPlayer={collider.bounds.Contains(player.transform.position)} center={collider.bounds.center:F2} size={collider.bounds.size:F2}");
                }
            });
        }

        private static string Path(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }
            return path;
        }
    }
}
