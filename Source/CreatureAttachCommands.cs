using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace valheimCLI;

/// <summary>Creature combat state and the vanilla dodge input, for attach and invulnerability checks.</summary>
internal static class CreatureAttachCommands
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly FieldInfo? AttachTargetField = typeof(Attack).GetField("m_attachTarget", Instance);
    private static readonly FieldInfo? IsAttachedField = typeof(Attack).GetField("m_isAttached", Instance);
    private static readonly MethodInfo? DodgeMethod = typeof(Player).GetMethod("Dodge", Instance, null, new[] { typeof(Vector3) }, null);

    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_character_detail", "Read health, ownership, attach, faction, damage modifiers, and statuses of the nearest matching non-player character: <prefab> [radius=30]", args =>
        {
            Player player = Player.m_localPlayer;
            float radius = 30;
            if (player == null || args.Length < 2 || args.Length > 3 || (args.Length == 3 &&
                !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out radius)) || radius <= 0 || radius > 200)
            { args.Context.AddString("ERROR: Local player, prefab, and a radius greater than 0 and at most 200 required"); return; }
            Character? target = Character.GetAllCharacters()
                .Where(character => !character.IsPlayer() && Utils.GetPrefabName(character.gameObject) == args[1])
                .Where(character => Vector3.Distance(character.transform.position, player.transform.position) <= radius)
                .OrderBy(character => Vector3.Distance(character.transform.position, player.transform.position)).FirstOrDefault();
            if (target == null) { args.Context.AddString("ERROR: No matching character in range"); return; }
            ZNetView view = target.GetComponent<ZNetView>();
            ZDO? record = view != null && view.IsValid() ? view.GetZDO() : null;
            args.Context.AddString("CHARACTER_DETAIL prefab=" + args[1] + " zdo=" + (record?.m_uid.ToString() ?? "none") +
                " owner=" + (record?.GetOwner().ToString(CultureInfo.InvariantCulture) ?? "none") + " isOwner=" + (view != null && view.IsValid() && view.IsOwner()) +
                " distance=" + Vector3.Distance(target.transform.position, player.transform.position).ToString("F2", CultureInfo.InvariantCulture));
            args.Context.AddString("health=" + target.GetHealth().ToString("F1", CultureInfo.InvariantCulture) + " maxHealth=" +
                target.GetMaxHealth().ToString("F1", CultureInfo.InvariantCulture) + " level=" + target.GetLevel() + " dead=" + target.IsDead() +
                " faction=" + target.m_faction + " boss=" + target.IsBoss() + " tamed=" + target.IsTamed() +
                " position=" + target.transform.position.ToString("F2"));
            Attack? attack = target is Humanoid humanoid ? humanoid.m_currentAttack : null;
            Transform? attachedTo = attack != null ? AttachTargetField?.GetValue(attack) as Transform : null;
            args.Context.AddString("isAttached=" + target.IsAttached() + " inAttack=" + target.InAttack() +
                " attackAnimation=" + (attack?.m_attackAnimation ?? "none") + " attackDone=" + (attack?.IsDone().ToString() ?? "none") +
                " attackAttached=" + (attack != null ? IsAttachedField?.GetValue(attack) : "none") +
                " attachTarget=" + (attachedTo != null ? attachedTo.name : "none") +
                " attachTargetIsLocalPlayer=" + (attachedTo != null && attachedTo == player.transform));
            args.Context.AddString("damageModifiers=" + Modifiers(target.GetDamageModifiers()));
            args.Context.AddString("statuses=" + string.Join(",", target.GetSEMan().GetStatusEffects().Select(status => status.name)));
            if (record != null)
                args.Context.AddString("encounter=" + record.GetString("VE_Encounter") + " shieldExpiry=" + record.GetLong("VE_ShieldExpiry") +
                    " shieldBroken=" + record.GetBool("VE_ShieldBroken"));
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_dodge", "Queue the vanilla dodge roll relative to the camera: [forward|back|left|right]", args =>
        {
            Player player = Player.m_localPlayer;
            if (player == null || DodgeMethod == null) { args.Context.AddString("ERROR: Local player and Player.Dodge required"); return; }
            Transform view = GameCamera.instance != null ? GameCamera.instance.transform : player.transform;
            Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(view.right, Vector3.up).normalized;
            string direction = args.Length > 1 ? args[1].ToLowerInvariant() : "back";
            Vector3 dodge = direction switch
            {
                "forward" => forward,
                "back" => -forward,
                "left" => -right,
                "right" => right,
                _ => Vector3.zero
            };
            if (dodge == Vector3.zero) { args.Context.AddString("ERROR: Direction must be forward, back, left, or right"); return; }
            DodgeMethod.Invoke(player, new object[] { dodge });
            args.Context.AddString("Dodge queued direction=" + direction + " onGround=" + player.IsOnGround() + " inAttack=" + player.InAttack() +
                " stamina=" + player.GetStamina().ToString("F1", CultureInfo.InvariantCulture));
        }, isCheat: true);
    }

    private static string Modifiers(HitData.DamageModifiers modifiers) =>
        "blunt:" + modifiers.m_blunt + " slash:" + modifiers.m_slash + " pierce:" + modifiers.m_pierce + " chop:" + modifiers.m_chop +
        " pickaxe:" + modifiers.m_pickaxe + " fire:" + modifiers.m_fire + " frost:" + modifiers.m_frost + " lightning:" + modifiers.m_lightning +
        " poison:" + modifiers.m_poison + " spirit:" + modifiers.m_spirit;
}
