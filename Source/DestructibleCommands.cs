using System.Globalization;
using System.Linq;
using UnityEngine;

namespace valheimCLI
{
    public static class DestructibleCommands
    {
        public static void Register()
        {
            new Terminal.ConsoleCommand("cli_destructibles", "Inspect loaded destructibles: cli_destructibles <prefab|network-id> [radius]", args => Run(args, false), isCheat: true);
            new Terminal.ConsoleCommand("cli_damage_destructible", "Hit one living destructible through its normal damage RPC: cli_damage_destructible <prefab|network-id> <damage> <pickaxe|blunt|slash> [radius]", args => Run(args, true), isCheat: true);
        }

        private static void Run(Terminal.ConsoleEventArgs args, bool damage)
        {
            Player player = Player.m_localPlayer;
            int required = damage ? 4 : 2;
            if (player == null || args.Length < required || args.Length > required + 1)
            {
                args.Context.AddString("ERROR: Local player and the documented command arguments are required");
                return;
            }
            float radius = 30f;
            if (args.Length > required && !Number(args[required], 1000f, out radius))
            {
                args.Context.AddString("ERROR: Radius must be greater than zero and at most 1000");
                return;
            }
            float amount = 0f;
            if (damage && (!Number(args[2], 100000f, out amount) || (args[3] != "pickaxe" && args[3] != "blunt" && args[3] != "slash")))
            {
                args.Context.AddString("ERROR: Damage must be greater than zero and at most 100000; type must be pickaxe, blunt, or slash");
                return;
            }
            Destructible[] targets = Object.FindObjectsByType<Destructible>(FindObjectsSortMode.None)
                .Where(target => target.gameObject.activeInHierarchy && Vector3.Distance(target.transform.position, player.transform.position) <= radius)
                .OrderBy(target => Vector3.Distance(target.transform.position, player.transform.position)).ToArray();
            int count = 0;
            foreach (Destructible target in targets)
            {
                ZNetView view = target.GetComponent<ZNetView>();
                if (view == null) continue;
                ZDO data = view.GetZDO();
                if (data == null) continue;
                string prefab = Utils.GetPrefabName(target.gameObject);
                if (prefab != args[1] && data.m_uid.ToString() != args[1]) continue;
                float health = data.GetFloat(ZDOVars.s_health, target.m_health + Game.m_worldLevel * target.m_health * Game.instance.m_worldLevelMineHPMultiplier);
                if (damage && health <= 0f) continue;
                args.Context.AddString($"prefab={prefab} position={target.transform.position.ToString("F3")} health={health} id={data.m_uid} owner={view.IsOwner()}");
                count++;
                if (!damage) continue;
                HitData hit = new HitData();
                hit.m_point = target.transform.position;
                hit.m_dir = (hit.m_point - player.transform.position).normalized;
                hit.m_toolTier = 0;
                hit.SetAttacker(player);
                if (args[3] == "pickaxe") hit.m_damage.m_pickaxe = amount;
                else if (args[3] == "blunt") hit.m_damage.m_blunt = amount;
                else hit.m_damage.m_slash = amount;
                target.Damage(hit);
                args.Context.AddString("Damage request sent; inspect the target again to verify the owner applied it");
                break;
            }
            if (count == 0) args.Context.AddString("ERROR: No matching networked target in radius");
        }

        private static bool Number(string text, float maximum, out float value) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && value > 0f && value <= maximum;
    }
}
