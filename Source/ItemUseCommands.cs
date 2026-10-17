using System;
using System.Linq;
using UnityEngine;

namespace valheimCLI
{
    internal static class ItemUseCommands
    {
        internal static void Register()
        {
            new Terminal.ConsoleCommand("cli_use_item_nearest", "Use an inventory item on a nearby interactable: cli_use_item_nearest <item-prefab> <target-name> [radius=5]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                float radius = 5;
                if (args.Length < 3 || (args.Length > 3 && (!float.TryParse(args[3], out radius) || radius <= 0)))
                {
                    args.Context.AddString("Usage: cli_use_item_nearest <item-prefab> <target-name> [radius=5]");
                    return;
                }
                Player player = Player.m_localPlayer;
                if (player == null) { args.Context.AddString("ERROR: No local player found"); return; }
                ItemDrop.ItemData? item = player.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name.Equals(args[1], StringComparison.OrdinalIgnoreCase));
                if (item == null) { args.Context.AddString("ERROR: Item is not in the local inventory"); return; }
                MonoBehaviour? target = Physics.OverlapSphere(player.transform.position, radius)
                    .SelectMany(c => c.GetComponentsInParent<MonoBehaviour>())
                    .Where(c => c is Interactable && !(c is Player) && c.name.IndexOf(args[2], StringComparison.OrdinalIgnoreCase) >= 0)
                    .Distinct().OrderBy(c => Vector3.Distance(player.transform.position, c.transform.position)).FirstOrDefault();
                if (target == null) { args.Context.AddString("ERROR: No matching interactable found"); return; }
                bool result = ((Interactable)target).UseItem(player, item);
                args.Context.AddString($"OK: Used {args[1]} on {target.name} result={result}");
            }, isCheat: true);
        }
    }
}
