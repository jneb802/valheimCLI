using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace valheimCLI
{
    public static class ComponentCommands
    {
        public static void Register()
        {
            new Terminal.ConsoleCommand("cli_store_buy", "Buy an available item through the open store: cli_store_buy <prefab>", args =>
            {
                if (args.Length != 2 || Player.m_localPlayer == null || StoreGui.instance == null || !StoreGui.IsVisible()) { args.Context.AddString("ERROR: Open a store and specify an item prefab"); return; }
                StoreGui store = StoreGui.instance;
                Trader trader = store.m_trader;
                Trader.TradeItem item = trader.GetAvailableItems().FirstOrDefault(candidate => candidate.m_prefab != null && candidate.m_prefab.name == args[1]);
                if (item == null) { args.Context.AddString("ERROR: Item is not available at this store"); return; }
                Inventory inventory = Player.m_localPlayer.GetInventory();
                int before = inventory.CountItems(store.m_coinPrefab.m_itemData.m_shared.m_name);
                store.m_selectedItem = item;
                store.OnBuyItem();
                int after = inventory.CountItems(store.m_coinPrefab.m_itemData.m_shared.m_name);
                args.Context.AddString($"purchase prefab={args[1]} price={item.m_price} coinsBefore={before} coinsAfter={after}");
            }, isCheat: true);
            new Terminal.ConsoleCommand("cli_component_state", "Inspect nearest component fields/properties: cli_component_state <full-type-name> [radius]", args => Run(args, false, false), isCheat: true);
            new Terminal.ConsoleCommand("cli_component_interact", "Use normal interaction on nearest component: cli_component_interact <full-type-name> [radius]", args => Run(args, true, false), isCheat: true);
            new Terminal.ConsoleCommand("cli_component_use_item", "Use inventory item on nearest component: cli_component_use_item <full-type-name> <item-prefab> [radius]", args => Run(args, true, true), isCheat: true);
            new Terminal.ConsoleCommand("cli_inventory_details", "Print inventory prefab, quality, tooltip and custom data: cli_inventory_details [prefab]", args =>
            {
                Player player = Player.m_localPlayer;
                if (player == null) { args.Context.AddString("ERROR: No local player"); return; }
                foreach (ItemDrop.ItemData item in player.GetInventory().GetAllItems())
                {
                    string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
                    if (args.Length > 1 && prefab != args[1]) continue;
                    args.Context.AddString($"item={prefab} count={item.m_stack} quality={item.m_quality} grid={item.m_gridPos}");
                    foreach (System.Collections.Generic.KeyValuePair<string, string> entry in item.m_customData.OrderBy(entry => entry.Key)) args.Context.AddString($"  {entry.Key}={entry.Value}");
                    args.Context.AddString("tooltip=" + item.GetTooltip().Replace("\n", " | "));
                }
            }, isCheat: true);
        }

        private static void Run(Terminal.ConsoleEventArgs args, bool interact, bool useItem)
        {
            Player player = Player.m_localPlayer;
            if (player == null) { args.Context.AddString("ERROR: No local player"); return; }
            if (args.Length < (useItem ? 3 : 2)) { args.Context.AddString("ERROR: Full component type and optional item prefab required"); return; }
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(args[1], false)).FirstOrDefault(candidate => candidate != null);
            if (type == null || !typeof(Component).IsAssignableFrom(type)) { args.Context.AddString("ERROR: Component type not loaded"); return; }
            int radiusArgument = useItem ? 3 : 2;
            float radius = 30f;
            if (args.Length > radiusArgument && (!float.TryParse(args[radiusArgument], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) || float.IsNaN(radius) || radius <= 0 || radius > 10000)) { args.Context.AddString("ERROR: Radius must be greater than 0 and at most 10000"); return; }
            Component target = UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.None).Cast<Component>()
                .Where(component => component.gameObject.activeInHierarchy && Vector3.Distance(component.transform.position, player.transform.position) <= radius)
                .OrderBy(component => Vector3.Distance(component.transform.position, player.transform.position)).FirstOrDefault();
            if (target == null) { args.Context.AddString("ERROR: No matching active component in radius"); return; }
            args.Context.AddString($"target={Path(target.transform)} type={type.FullName} position={target.transform.position.ToString("F3")} distance={Vector3.Distance(target.transform.position, player.transform.position):F3}");
            if (interact)
            {
                if (!(target is Interactable action)) { args.Context.AddString("ERROR: Component does not implement Interactable"); return; }
                if (useItem)
                {
                    ItemDrop.ItemData item = player.GetInventory().GetAllItems().FirstOrDefault(candidate => candidate.m_dropPrefab != null && candidate.m_dropPrefab.name == args[2]);
                    if (item == null) { args.Context.AddString("ERROR: Inventory item not found"); return; }
                    args.Context.AddString("UseItem result=" + action.UseItem(player, item));
                }
                else args.Context.AddString("Interact result=" + action.Interact(player, false, false));
                return;
            }
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            string? member = args.Length > 3 ? args[3] : null;
            if (member != null && member.Contains('.'))
            {
                object value = target;
                foreach (string part in member.Split('.'))
                {
                    if (value == null) break;
                    Type current = value.GetType();
                    const BindingFlags nestedFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                    FieldInfo field = current.GetField(part, nestedFlags);
                    PropertyInfo property = current.GetProperty(part, nestedFlags);
                    if (field == null && (property == null || property.GetIndexParameters().Length != 0))
                    { args.Context.AddString("ERROR: Member not found: " + part); return; }
                    value = field != null ? field.GetValue(value) : property.GetValue(value);
                }
                args.Context.AddString(member + "=" + Format(value));
                return;
            }
            bool found = false;
            foreach (FieldInfo field in type.GetFields(flags).Where(field => member == null || field.Name == member).OrderBy(field => field.Name))
            { found = true; args.Context.AddString(field.Name + "=" + Format(field.GetValue(target))); }
            foreach (PropertyInfo property in type.GetProperties(flags).Where(property => (member == null || property.Name == member) && property.GetIndexParameters().Length == 0 && property.GetGetMethod(true) != null).OrderBy(property => property.Name))
            {
                found = true;
                try { args.Context.AddString(property.Name + "=" + Format(property.GetValue(target))); }
                catch (Exception exception) { args.Context.AddString(property.Name + "=<unavailable:" + exception.GetType().Name + ">"); }
            }
            if (member != null && !found) args.Context.AddString("ERROR: Member not found: " + member);
            if (member == null && target is Hoverable hoverable) args.Context.AddString("hover=" + hoverable.GetHoverText().Replace("\n", " | "));
        }

        private static string Format(object value)
        {
            if (value == null) return "null";
            if (value is UnityEngine.Object unityObject && unityObject == null) return "<destroyed>";
            if (value is Transform transform) return Path(transform) + " position=" + transform.position.ToString("F3");
            if (value is Component component) return Path(component.transform) + " type=" + component.GetType().FullName;
            if (value is Array array) return "[" + string.Join(", ", array.Cast<object>().Select(Format)) + "]";
            if (value is System.Collections.IEnumerable items && !(value is string))
                return "[" + string.Join(", ", items.Cast<object>().Take(1000).Select(Format)) + "]";
            return value.ToString();
        }

        private static string Path(Transform transform) => transform.parent == null ? transform.name : Path(transform.parent) + "/" + transform.name;
    }
}
