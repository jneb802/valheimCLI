using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace valheimCLI;

internal static class DestructibleCommands
{
    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_destructibles", "Read nearby destructible health: <prefab> [radius=30]", args =>
        {
            if (args.Length < 2 || Player.m_localPlayer == null) return;
            if (!TryRadius(args.Length > 2 ? args[2] : "30", out float radius))
            { args.Context.AddString("ERROR: Radius must be greater than 0 and at most 200"); return; }
            foreach (Destructible target in Find(args[1], radius))
            {
                ZNetView view = target.GetComponent<ZNetView>();
                args.Context.AddString($"prefab={target.name} position={target.transform.position:F3} health={view?.GetZDO()?.GetFloat(ZDOVars.s_health, target.m_health)} owner={view?.IsOwner()} id={view?.GetZDO()?.m_uid}");
            }
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_damage_destructible", "Send typed damage through normal destructible RPC: <prefab> <type> <amount> [radius=30]", args =>
        {
            if (args.Length < 4 || Player.m_localPlayer == null) return;
            if (!float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float amount) ||
                float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0 || amount > 1000000)
            { args.Context.AddString("ERROR: Damage must be between 0 and 1000000"); return; }
            HitData hit = new();
            switch (args[2].ToLowerInvariant())
            {
                case "damage": hit.m_damage.m_damage = amount; break;
                case "blunt": hit.m_damage.m_blunt = amount; break;
                case "slash": hit.m_damage.m_slash = amount; break;
                case "pierce": hit.m_damage.m_pierce = amount; break;
                case "chop": hit.m_damage.m_chop = amount; break;
                case "pickaxe": hit.m_damage.m_pickaxe = amount; break;
                case "fire": hit.m_damage.m_fire = amount; break;
                case "frost": hit.m_damage.m_frost = amount; break;
                case "lightning": hit.m_damage.m_lightning = amount; break;
                case "poison": hit.m_damage.m_poison = amount; break;
                case "spirit": hit.m_damage.m_spirit = amount; break;
                default: args.Context.AddString("ERROR: Unknown damage type"); return;
            }
            if (!TryRadius(args.Length > 4 ? args[4] : "30", out float radius))
            { args.Context.AddString("ERROR: Radius must be greater than 0 and at most 200"); return; }
            Destructible? target = Find(args[1], radius).FirstOrDefault();
            if (target == null) { args.Context.AddString("ERROR: No matching destructible"); return; }
            hit.SetAttacker(Player.m_localPlayer);
            hit.m_point = target.transform.position;
            hit.m_toolTier = 10;
            target.Damage(hit);
            args.Context.AddString($"Sent {args[2]}={amount} to {target.name}; read cli_destructibles after the RPC completes.");
        }, isCheat: true);
    }

    private static bool TryRadius(string value, out float radius) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out radius) && radius > 0 && radius <= 200;

    private static Destructible[] Find(string prefab, float radius) => UnityEngine.Object.FindObjectsByType<Destructible>(FindObjectsSortMode.None)
        .Where(target => global::Utils.GetPrefabName(target.gameObject) == prefab &&
            Vector3.Distance(target.transform.position, Player.m_localPlayer.transform.position) <= radius)
        .OrderBy(target => Vector3.Distance(target.transform.position, Player.m_localPlayer.transform.position)).ToArray();
}
