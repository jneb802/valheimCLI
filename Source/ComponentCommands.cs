using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace valheimCLI;

internal static class ComponentCommands
{
    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_hover_state", "Report the player's actual crosshair interaction target", args =>
        {
            GameObject? target = Player.m_localPlayer?.GetHoverObject();
            if (target == null) { args.Context.AddString("hover=none"); return; }
            Hoverable? hover = target.GetComponentInParent<Hoverable>();
            args.Context.AddString($"hover={target.name} text={hover?.GetHoverText()}");
        });
        new Terminal.ConsoleCommand("cli_container_items", "Read a nearby container inventory: [radius]", args =>
        {
            if (Player.m_localPlayer == null) { args.Context.AddString("ERROR: No player"); return; }
            float radius = 5;
            if (args.Length > 1 && (!float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) ||
                float.IsNaN(radius) || radius <= 0 || radius > 100))
            { args.Context.AddString("ERROR: Radius must be 0–100 metres"); return; }
            Container? container = UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                .Where(c => Vector3.Distance(c.transform.position, Player.m_localPlayer.transform.position) <= radius)
                .OrderBy(c => Vector3.Distance(c.transform.position, Player.m_localPlayer.transform.position)).FirstOrDefault();
            if (container == null) { args.Context.AddString("ERROR: No container in range"); return; }
            args.Context.AddString($"container={container.name} position={container.transform.position:F2}");
            foreach (ItemDrop.ItemData item in container.GetInventory().GetAllItems())
                args.Context.AddString($"prefab={item.m_dropPrefab?.name} count={item.m_stack} quality={item.m_quality}");
        });
        new Terminal.ConsoleCommand("cli_take_all", "Use Take All in the currently open container", args =>
        {
            if (Player.m_localPlayer == null || InventoryGui.instance == null || !InventoryGui.IsVisible())
            { args.Context.AddString("ERROR: Open a container first"); return; }
            InventoryGui.instance.OnTakeAll();
            args.Context.AddString("Take All requested");
        });
        new Terminal.ConsoleCommand("cli_component_state", "Read nearby component public scalar properties: <type-name> [radius]", args =>
        {
            MonoBehaviour? target = Find(args, 1, 2, 10000);
            if (target == null) return;
            args.Context.AddString($"component={target.GetType().FullName} object={target.name} position={target.transform.position:F2}");
            foreach (PropertyInfo property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0 ||
                    !(property.PropertyType.IsPrimitive || property.PropertyType.IsEnum || property.PropertyType == typeof(string))) continue;
                try { args.Context.AddString(property.Name + "=" + property.GetValue(target)); }
                catch (Exception exception) { args.Context.AddString(property.Name + "=ERROR " + exception.GetBaseException().Message); }
            }
        });
        new Terminal.ConsoleCommand("cli_interact_component", "Interact with nearby component: <type-name> [radius]", args =>
        {
            MonoBehaviour? target = Find(args, 1, 2);
            if (target is not Interactable interactable) { args.Context.AddString("ERROR: Target is not interactable"); return; }
            bool result = interactable.Interact(Player.m_localPlayer, false, false);
            args.Context.AddString($"Interact {target.name}: {result}");
        });
        new Terminal.ConsoleCommand("cli_use_item_on", "Use one inventory item on a component: <item-prefab> <type-name> [radius]", args =>
        {
            MonoBehaviour? target = Find(args, 2, 3);
            if (target is not Interactable interactable) { args.Context.AddString("ERROR: Target is not interactable"); return; }
            ItemDrop.ItemData? item = Player.m_localPlayer.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == args[1]);
            if (item == null) { args.Context.AddString("ERROR: Item absent from inventory"); return; }
            bool result = interactable.UseItem(Player.m_localPlayer, item);
            args.Context.AddString($"UseItem {item.m_dropPrefab.name} on {target.name}: {result}");
        });
        new Terminal.ConsoleCommand("cli_buy_store_item", "Buy an item through the open store: <item-prefab>", args =>
        {
            if (args.Length != 2 || Player.m_localPlayer == null || StoreGui.instance == null || !StoreGui.IsVisible())
            { args.Context.AddString("ERROR: Open a trader and specify an item prefab"); return; }
            Trader trader = (Trader)AccessTools.Field(typeof(StoreGui), "m_trader").GetValue(StoreGui.instance);
            Trader.TradeItem? item = trader.GetAvailableItems().FirstOrDefault(i => i.m_prefab != null && i.m_prefab.name == args[1]);
            if (item == null) { args.Context.AddString("ERROR: Trader does not sell this item"); return; }
            Inventory inventory = Player.m_localPlayer.GetInventory();
            int before = inventory.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == args[1]).Sum(i => i.m_stack);
            AccessTools.Field(typeof(StoreGui), "m_selectedItem").SetValue(StoreGui.instance, item);
            StoreGui.instance.OnBuyItem();
            int after = inventory.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == args[1]).Sum(i => i.m_stack);
            args.Context.AddString($"Store purchase prefab={args[1]} before={before} after={after} received={after - before}");
        });
        new Terminal.ConsoleCommand("cli_inventory_items", "List inventory prefab, count, quality, and custom-data keys", args =>
        {
            if (Player.m_localPlayer == null) { args.Context.AddString("ERROR: No player"); return; }
            foreach (ItemDrop.ItemData item in Player.m_localPlayer.GetInventory().GetAllItems())
                args.Context.AddString($"prefab={item.m_dropPrefab?.name} count={item.m_stack} quality={item.m_quality} customKeys={string.Join(",", item.m_customData.Keys)}");
        });
    }

    private static MonoBehaviour? Find(Terminal.ConsoleEventArgs args, int typeIndex, int radiusIndex, float maxRadius = 100)
    {
        if (Player.m_localPlayer == null || args.Length <= typeIndex)
        { args.Context.AddString("ERROR: Player and component type required"); return null; }
        float radius = 5;
        if (args.Length > radiusIndex && !float.TryParse(args[radiusIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out radius))
        { args.Context.AddString("ERROR: Invalid radius"); return null; }
        if (float.IsNaN(radius) || radius <= 0 || radius > maxRadius) { args.Context.AddString($"ERROR: Radius must be 0–{maxRadius} metres"); return null; }
        MonoBehaviour? target = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .Where(c => c.GetType().Name == args[typeIndex] || c.GetType().FullName == args[typeIndex])
            .Where(c => Vector3.Distance(c.transform.position, Player.m_localPlayer.transform.position) <= radius)
            .OrderBy(c => Vector3.Distance(c.transform.position, Player.m_localPlayer.transform.position)).FirstOrDefault();
        if (target == null) args.Context.AddString("ERROR: No matching component in range");
        return target;
    }
}
