using System;
using System.Linq;
using UnityEngine;

namespace valheimCLI
{
    internal static class ItemInteractionCommands
    {
        public static void Register()
        {
            new Terminal.ConsoleCommand("cli_count_item", "Count inventory items by prefab: cli_count_item <itemPrefab>", args =>
            {
                Player player = Player.m_localPlayer;
                if (args.Length != 2 || player == null)
                {
                    args.Context.AddString("ERROR: Requires a local player and item prefab.");
                    return;
                }
                int count = player.GetInventory().GetAllItems()
                    .Where(item => item.m_dropPrefab != null && item.m_dropPrefab.name == args[1])
                    .Sum(item => item.m_stack);
                args.Context.AddString($"Inventory item={args[1]} count={count}");
            });
            new Terminal.ConsoleCommand("cli_use_item_nearest", "Use an inventory item on the nearest matching interactable: cli_use_item_nearest <itemPrefab> <object-or-component-name> [radius]", args =>
            {
                Player player = Player.m_localPlayer;
                float radius = 5f;
                if (args.Length < 3 || player == null ||
                    (args.Length > 3 && (!float.TryParse(args[3], out radius) || radius <= 0 || float.IsNaN(radius) || float.IsInfinity(radius))))
                {
                    args.Context.AddString("ERROR: Requires a local player, item prefab, target name, and positive radius.");
                    return;
                }
                ItemDrop.ItemData item = player.GetInventory().GetAllItems()
                    .FirstOrDefault(candidate => candidate.m_dropPrefab != null && candidate.m_dropPrefab.name == args[1]);
                if (item == null)
                {
                    args.Context.AddString("ERROR: Inventory does not contain " + args[1]);
                    return;
                }
                MonoBehaviour target = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                    .Where(candidate => candidate is Interactable && !(candidate is Player) &&
                        (candidate.name.IndexOf(args[2], StringComparison.OrdinalIgnoreCase) >= 0 ||
                         candidate.GetType().Name.IndexOf(args[2], StringComparison.OrdinalIgnoreCase) >= 0) &&
                        Vector3.Distance(candidate.transform.position, player.transform.position) <= radius)
                    .OrderBy(candidate => Vector3.Distance(candidate.transform.position, player.transform.position))
                    .FirstOrDefault();
                if (target == null)
                {
                    args.Context.AddString("ERROR: No matching interactable in range.");
                    return;
                }
                bool accepted = ((Interactable)target).UseItem(player, item);
                args.Context.AddString($"UseItem target={target.name} component={target.GetType().Name} item={args[1]} accepted={accepted}. Check inventory and event state after asynchronous actions complete.");
            }, isCheat: true);
        }
    }
}
