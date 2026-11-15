using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace valheimCLI;

internal static class BossSummonCommands
{
    private const BindingFlags Methods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_spawn_boss_summon", "Spawn a linked Endgame runner: cli_spawn_boss_summon neck [distance=10] [lifetime=35]. Requires a nearby living Uber Eikthyr.", args => Run(args, false), isCheat: true);
        new Terminal.ConsoleCommand("cli_boss_summon_state", "Inspect nearby Endgame runners, their boss links and eligible targets.", args => Run(args, true), isCheat: true);
    }

    private static Type ModType(string name) => AppDomain.CurrentDomain.GetAssemblies()
        .Select(assembly => assembly.GetType("ValheimEndgame." + name)).FirstOrDefault(type => type != null)
        ?? throw new InvalidOperationException("ValheimEndgame with Uber bosses must be loaded.");

    private static object? Invoke(Type type, string method, params object?[] arguments) =>
        (type.GetMethod(method, Methods) ?? throw new MissingMethodException(type.FullName, method)).Invoke(null, arguments);

    private static void Run(Terminal.ConsoleEventArgs args, bool inspect)
    {
        try
        {
            Player player = Player.m_localPlayer;
            if (player == null || ZNetScene.instance == null) throw new InvalidOperationException("Enter a world first.");
            Type ability = ModType("UberAbility");
            Type runnerType = ModType("LightningRunner");
            if (inspect)
            {
                Character[] runners = Character.GetAllCharacters().Where(character => character != null &&
                    character.GetComponent(runnerType) != null && Vector3.Distance(character.transform.position, player.transform.position) <= 100).ToArray();
                foreach (Character runner in runners)
                {
                    Character? boss = Invoke(ability, "Owner", runner.gameObject) as Character;
                    Player? target = boss == null ? null : Invoke(ability, "Target", boss, runner.transform.position) as Player;
                    MonsterAI ai = runner.GetComponent<MonsterAI>();
                    ZDO record = runner.m_nview.GetZDO();
                    args.Context.AddString($"summonMetadata id={record.m_uid} bossId={record.GetZDOID("VE_UberBossOwner")} expiry={record.GetLong("VE_AbilityExpires")} slsManaged={record.GetBool("SLS_EXTMGD")} encounter={record.GetString("VE_Encounter")}");
                    args.Context.AddString($"runner={runner.name} id={runner.m_nview.GetZDO().m_uid} owner={runner.IsOwner()} health={runner.GetHealth():F1}/{runner.GetMaxHealth():F1} boss={(boss == null ? "NONE" : boss.name)} eligibleTarget={(target == null ? "NONE" : target.GetPlayerName())} alerted={ai.IsAlerted()} position={runner.transform.position:F2} velocity={runner.m_body.linearVelocity:F2} targetDistance={(target == null ? -1 : Vector3.Distance(runner.transform.position, target.transform.position)):F2}");
                }
                args.Context.AddString("Runners within 100m: " + runners.Length);
                return;
            }
            if (args.Length < 2 || args.Length > 4) throw new ArgumentException("Usage: cli_spawn_boss_summon neck [distance=10] [lifetime=35]");
            string prefab = args[1].ToLowerInvariant() switch
            {
                "neck" => "VE_Eikthyr_LightningNeck",
                _ => throw new ArgumentException("Choose neck.")
            };
            float distance = args.Length > 2 ? Parse(args[2], "distance", 3, 25) : 10;
            float lifetime = args.Length > 3 ? Parse(args[3], "lifetime", 1, 300) : 35;
            if (player.IsDead() || player.InGhostMode() || player.IsTeleporting()) throw new InvalidOperationException("Use a living player with ghost mode off, after teleporting finishes.");
            Type registry = ModType("UberBossRegistry");
            Character? owner = Character.GetAllCharacters().Where(character => character != null &&
                Utils.GetPrefabName(character.gameObject) == "VE_UberEikthyr" && !character.IsDead() && character.IsOwner() &&
                (bool)Invoke(registry, "Eligible", character, player)!).OrderBy(character => Vector3.Distance(character.transform.position, player.transform.position)).FirstOrDefault();
            if (owner == null) throw new InvalidOperationException("No eligible locally owned Uber Eikthyr within 40m. Spawn one nearby; inside an encounter you must be a participant in its active room.");
            if (!(bool)Invoke(registry, "EncounterActive", owner.m_nview.GetZDO())!) throw new InvalidOperationException("The boss encounter is not active.");
            if (ZNetScene.instance.GetPrefab(prefab) == null) throw new InvalidOperationException("Missing prefab: " + prefab);
            Vector3 forward = player.transform.forward; forward.y = 0;
            Vector3 position = player.transform.position + forward.normalized * distance;
            object?[] floorArguments = { owner, position, Vector3.zero };
            if (!(bool)Invoke(ability, "Floor", floorArguments)!) throw new InvalidOperationException("No valid floor ahead inside the boss room. Face open floor or reduce distance.");
            GameObject spawned = (GameObject)Invoke(ability, "Spawn", prefab, owner, (Vector3)floorArguments[2]! + Vector3.up * 0.3f, lifetime)!;
            ZNetView view = spawned.GetComponent<ZNetView>();
            args.Context.AddString($"Spawned {prefab} id={view.GetZDO().m_uid} linkedBoss={owner.name} bossId={owner.m_nview.GetZDO().m_uid} position={spawned.transform.position:F2} lifetime={lifetime}s. Run cli_boss_summon_state to inspect the chase.");
        }
        catch (Exception error)
        {
            while (error.InnerException != null) error = error.InnerException;
            args.Context.AddString("ERROR: " + error.Message);
        }
    }

    private static float Parse(string text, string name, float minimum, float maximum)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
            throw new ArgumentException(name + " must be from " + minimum + " to " + maximum + ".");
        return value;
    }
}
