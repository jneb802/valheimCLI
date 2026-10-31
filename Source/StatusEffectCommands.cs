using System;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace valheimCLI;

internal static class StatusEffectCommands
{
    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_status_effects", "Read status durations and shield damage: cli_status_effects [self|all|prefab] [radius]", args =>
        {
            Player player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("ERROR: Local player required"); return; }
            string target = args.Length > 1 ? args[1] : "self";
            float radius = 100;
            if (args.Length > 3 || (args.Length > 2 && (!float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
                float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0 || radius > 10000)))
            { args.Context.AddString("ERROR: Usage: cli_status_effects [self|all|prefab] [radius 0..10000]"); return; }
            Character[] characters = Character.GetAllCharacters().Where(c => c != null &&
                Vector3.Distance(c.transform.position, player.transform.position) <= radius &&
                (target == "self" ? c == player : target == "all" || string.Equals(Utils.GetPrefabName(c.gameObject), target, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(c => Vector3.Distance(c.transform.position, player.transform.position)).ToArray();
            if (characters.Length == 0) { args.Context.AddString("STATUS_TARGETS count=0"); return; }
            foreach (Character character in characters)
            {
                StatusEffect[] statuses = character.GetSEMan().GetStatusEffects().ToArray();
                ZDO? record = character.GetComponent<ZNetView>()?.GetZDO();
                args.Context.AddString($"STATUS_CHARACTER prefab={Utils.GetPrefabName(character.gameObject)} id={record?.m_uid.ToString() ?? "none"} position={character.transform.position:F2} count={statuses.Length}");
                foreach (StatusEffect status in statuses)
                {
                    string shield = status is SE_Shield ? $" absorbed={AccessTools.Field(typeof(SE_Shield), "m_damage").GetValue(status)} capacity={AccessTools.Field(typeof(SE_Shield), "m_totalAbsorbDamage").GetValue(status)}" : "";
                    args.Context.AddString(FormattableString.Invariant($"STATUS name={status.name} ttl={status.m_ttl:F3} elapsed={status.GetDuration():F3} remaining={status.GetRemaningTime():F3} cooldown={status.m_cooldownIcon} icon={status.GetIconText().Replace(" ", "_")}{shield}"));
                }
            }
        });
    }
}
