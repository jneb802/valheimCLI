using System;
using System.Linq;
using UnityEngine;

namespace valheimCLI;

internal static class CreatureVisualCommands
{
    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_creature_state", "Inspect nearest creature and weapons: <prefab>", args => Creature(args, false), isCheat: true);
        new Terminal.ConsoleCommand("cli_creature_attack", "Start nearest owned creature's normal attack: <prefab> <weapon-prefab>", args => Creature(args, true), isCheat: true);
        new Terminal.ConsoleCommand("cli_scene_lights", "List or toggle lights within 300m by hierarchy text: <list|on|off> <path-text>", Lights, isCheat: true);
    }

    private static void Creature(Terminal.ConsoleEventArgs args, bool attack)
    {
        Player player = Player.m_localPlayer;
        if (player == null || args.Length != (attack ? 3 : 2))
        { args.Context.AddString("ERROR: Player, creature prefab, and (for attacks) weapon prefab required"); return; }
        Humanoid creature = Character.GetAllCharacters().OfType<Humanoid>()
            .Where(c => !c.IsPlayer() && c.name.Replace("(Clone)", "") == args[1] && Vector3.Distance(c.transform.position, player.transform.position) < 300)
            .OrderBy(c => Vector3.Distance(c.transform.position, player.transform.position)).FirstOrDefault();
        if (creature == null) { args.Context.AddString("ERROR: No matching creature within 300m"); return; }
        ItemDrop.ItemData[] weapons = creature.GetInventory().GetAllItems().Where(i => i.HavePrimaryAttack()).ToArray();
        args.Context.AddString($"creature={creature.name} position={creature.transform.position:F3} health={creature.GetHealth():F2} attacking={creature.InAttack()} weapon={creature.GetCurrentWeapon()?.m_dropPrefab?.name}");
        args.Context.AddString("weapons=" + string.Join(",", weapons.Select(i => i.m_dropPrefab?.name ?? i.m_shared.m_name)));
        if (!attack) return;
        ZNetView view = creature.GetComponent<ZNetView>();
        if (view == null || !view.IsValid() || !view.IsOwner()) { args.Context.AddString("ERROR: Creature is not locally owned"); return; }
        if (creature.IsDead() || creature.InAttack()) { args.Context.AddString("ERROR: Creature is dead or already attacking"); return; }
        ItemDrop.ItemData selected = weapons.FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == args[2]);
        if (selected == null) { args.Context.AddString("ERROR: Weapon is not in this creature's inventory"); return; }
        if (!creature.EquipItem(selected, false)) { args.Context.AddString("ERROR: Could not equip weapon"); return; }
        creature.SetLookDir(player.transform.position - creature.transform.position);
        args.Context.AddString("attackStarted=" + creature.StartAttack(player, false));
    }

    private static void Lights(Terminal.ConsoleEventArgs args)
    {
        Player player = Player.m_localPlayer;
        if (player == null || args.Length != 3 || (args[1] != "list" && args[1] != "on" && args[1] != "off"))
        { args.Context.AddString("ERROR: Expected list|on|off and hierarchy text, with a loaded player"); return; }
        int count = 0;
        foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (Vector3.Distance(light.transform.position, player.transform.position) > 300) continue;
            string path = Path(light.transform);
            if (path.IndexOf(args[2], StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (args[1] != "list") light.enabled = args[1] == "on";
            args.Context.AddString($"light={path} enabled={light.enabled} active={light.gameObject.activeInHierarchy} intensity={light.intensity} range={light.range} color={light.color} position={light.transform.position:F3} shadows={light.shadows}");
            count++;
        }
        args.Context.AddString("matchedLights=" + count);
    }

    private static string Path(Transform transform) => transform.parent == null ? transform.name : Path(transform.parent) + "/" + transform.name;
}
