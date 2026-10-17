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
        new Terminal.ConsoleCommand("cli_epicloot_table_level", "Set nearby table feature for test setup: <feature-name> <level>", args =>
        {
            if (args.Length != 3 || Player.m_localPlayer == null || !int.TryParse(args[2], out int level)) return;
            Component? table = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .Where(c => c.GetType().FullName == "EpicLoot_UnityLib.EnchantingTable" && Vector3.Distance(c.transform.position, Player.m_localPlayer.transform.position) < 5).FirstOrDefault();
            MethodInfo? method = table?.GetType().GetMethod("SetFeatureLevel");
            if (method == null) { args.Context.AddString("ERROR: No nearby table"); return; }
            object feature = Enum.Parse(method.GetParameters()[0].ParameterType, args[1]);
            method.Invoke(table, new object[] { feature, level });
            args.Context.AddString("Set table feature " + args[1] + "=" + level);
        });
        new Terminal.ConsoleCommand("cli_epicloot_enchant_ui", "Select a sigil/item in the open enchanting table: <prefab> <rarity> [craft]", args =>
        {
            if (args.Length < 3 || !int.TryParse(args[2], out int rarity)) return;
            Type? uiType = Type.GetType("EpicLoot_UnityLib.EnchantingTableUI, EpicLoot");
            object? ui = uiType?.GetProperty("instance")?.GetValue(null);
            if (ui == null) { args.Context.AddString("ERROR: Open the enchanting table first"); return; }
            Array panels = (Array)uiType!.GetField("Panels")!.GetValue(ui);
            Component? panel = panels.Cast<Component>().FirstOrDefault(p => p.GetType().Name == "EnchantUI");
            if (panel == null) return;
            int index = Array.IndexOf(panels, panel);
            TabHandler tabs = (TabHandler)uiType.GetField("TabHandler")!.GetValue(ui);
            tabs.SetActiveTab(index);
            Type type = panel.GetType();
            object buttons = type.GetField("RarityButtons")!.GetValue(panel);
            UnityEngine.UI.Toggle[] toggles = ((System.Collections.IEnumerable)buttons).Cast<UnityEngine.UI.Toggle>().ToArray();
            if (rarity < 0 || rarity >= toggles.Length) return;
            toggles[rarity].isOn = true;
            object list = AccessTools.Field(type, "AvailableItems").GetValue(panel);
            Transform container = (Transform)list.GetType().GetField("ListContainer")!.GetValue(list);
            bool selected = false;
            foreach (Transform row in container)
            {
                Component? element = row.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "MultiSelectItemListElement");
                if (element == null) continue;
                object data = AccessTools.Field(element.GetType(), "_item").GetValue(element);
                ItemDrop.ItemData? item = data?.GetType().GetMethod("GetItem")?.Invoke(data, null) as ItemDrop.ItemData;
                if (item?.m_dropPrefab?.name != args[1]) continue;
                element.GetType().GetMethod("SelectQuantity")!.Invoke(element, new object[] { 1, true });
                selected = true; break;
            }
            UnityEngine.UI.Button button = (UnityEngine.UI.Button)AccessTools.Field(type, "MainButton").GetValue(panel);
            args.Context.AddString($"selected={selected} canCraft={button.IsInteractable()}");
            if (selected && button.IsInteractable() && args.Length > 3 && args[3] == "craft") button.onClick.Invoke();
        });
        new Terminal.ConsoleCommand("cli_navigation_to_player", "Report nearby creatures and their full path to the player", args =>
        {
            if (Player.m_localPlayer == null || Pathfinding.instance == null) return;
            foreach (Character character in Character.GetAllCharacters().Where(c => !c.IsPlayer() && Vector3.Distance(c.transform.position, Player.m_localPlayer.transform.position) < 100))
            {
                System.Collections.Generic.List<Vector3> path = new();
                bool found = Pathfinding.instance.GetPath(character.transform.position, Player.m_localPlayer.transform.position,
                    path, character.GetComponent<BaseAI>()?.m_pathAgentType ?? Pathfinding.AgentType.Humanoid, true);
                args.Context.AddString($"creature={character.name} position={character.transform.position:F2} hp={character.GetHealth():F1} level={character.GetLevel()} fullPath={found} corners={path.Count}");
            }
        });
        new Terminal.ConsoleCommand("cli_item_custom_data", "Set first matching inventory item's custom data: <prefab> <key> <value>", args =>
        {
            if (args.Length != 4 || Player.m_localPlayer == null) return;
            ItemDrop.ItemData? item = Player.m_localPlayer.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab?.name == args[1]);
            if (item == null) { args.Context.AddString("ERROR: item absent"); return; }
            item.m_customData[args[2]] = args[3];
            args.Context.AddString("Updated " + args[2]);
        });
        new Terminal.ConsoleCommand("cli_epicloot_item", "Read Epic Loot eligibility and magic data: <prefab>", args =>
        {
            if (args.Length != 2 || Player.m_localPlayer == null) return;
            ItemDrop.ItemData? item = Player.m_localPlayer.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab?.name == args[1]);
            Type? api = Type.GetType("EpicLoot.API, EpicLoot");
            if (item == null || api == null) { args.Context.AddString("ERROR: item or Epic Loot absent"); return; }
            args.Context.AddString("eligible=" + api.GetMethod("CanBeMagicItem")?.Invoke(null, new object[] { item }));
            args.Context.AddString("magic=" + api.GetMethod("GetMagicItemJson")?.Invoke(null, new object[] { item }));
            args.Context.AddString("tooltip=" + Localization.instance.Localize(item.GetTooltip()).Replace("\n", " | "));
            args.Context.AddString("custom=" + string.Join(";", item.m_customData.Where(p => p.Key.Contains("ValheimEndgame")).Select(p => p.Key + "=" + p.Value)));
        });
        new Terminal.ConsoleCommand("cli_epicloot_roll", "Roll first matching inventory item through Epic Loot API: <prefab> <rarity ordinal>", args =>
        {
            if (args.Length != 3 || Player.m_localPlayer == null || !int.TryParse(args[2], out int rarity)) return;
            ItemDrop.ItemData? item = Player.m_localPlayer.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab?.name == args[1]);
            Type? api = Type.GetType("EpicLoot.API, EpicLoot");
            if (item == null || api == null) { args.Context.AddString("ERROR: item or Epic Loot absent"); return; }
            args.Context.AddString("rolled=" + api.GetMethod("TryMakeMagicItem")?.Invoke(null, new object[] { item, rarity, 0f, "" }));
        });
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
