using System;
using HarmonyLib;
using UnityEngine;

namespace valheimCLI;

[HarmonyPatch]
internal static class PlayerLifecycleTrace
{
    private static bool enabled;

    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_trace_player_lifecycle", "Trace player health, load, death, and footstep startup without changing gameplay: <true|false>", args =>
        {
            if (args.Length != 2 || !bool.TryParse(args[1], out bool value))
            { args.Context.AddString("Usage: cli_trace_player_lifecycle <true|false>"); return; }
            enabled = value;
            args.Context.AddString("Player lifecycle trace=" + enabled);
        });
    }

    private static void Write(string message) => valheimCLIPlugin.Log.LogInfo($"LIFECYCLE frame={Time.frameCount} time={Time.unscaledTime:F3} {message}");
    private static string State(Character player) => $"object={player.name} instance={player.GetInstanceID()} pos={player.transform.position:F3} health={player.GetHealth():F3} dead={player.IsDead()}";

    [HarmonyPatch(typeof(Player), nameof(Player.Load)), HarmonyPostfix]
    private static void Loaded(Player __instance)
    {
        if (enabled) Write("loaded " + State(__instance));
    }

    [HarmonyPatch(typeof(Character), nameof(Character.SetHealth)), HarmonyPrefix]
    private static void Health(Character __instance, float health)
    {
        if (!enabled || __instance is not Player || Mathf.Approximately(__instance.GetHealth(), health)) return;
        Write("setHealth=" + health + " " + State(__instance) + " caller=" + Environment.StackTrace);
    }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath)), HarmonyPrefix]
    private static void Died(Player __instance)
    {
        if (enabled) Write("death " + State(__instance) + " caller=" + Environment.StackTrace);
    }

    [HarmonyPatch(typeof(FootStep), "Start"), HarmonyPrefix]
    private static void Footstep(FootStep __instance)
    {
        if (!enabled) return;
        Animator animator = __instance.GetComponentInChildren<Animator>();
        Character character = __instance.GetComponent<Character>();
        ZNetView view = __instance.GetComponent<ZNetView>();
        if (character is Player || animator == null || character == null || view == null)
            Write($"footstep object={__instance.name} instance={__instance.GetInstanceID()} pos={__instance.transform.position:F3} animator={animator != null} character={character != null} view={view != null}" +
                (character != null ? " " + State(character) : ""));
    }
}
