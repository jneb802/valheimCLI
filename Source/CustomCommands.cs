using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace valheimCLI
{
    public static class CustomCommands
    {
        public static void Register()
        {
            new Terminal.ConsoleCommand("cli_goto_location", "Teleport to a location by prefab name", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_goto_location <location_prefab_name>");
                    return;
                }

                string locationName = args[1];
                Player player = Player.m_localPlayer;
                if (player == null)
                {
                    args.Context.AddString("ERROR: No local player found");
                    return;
                }

                ZoneSystem zoneSystem = ZoneSystem.instance;
                if (zoneSystem == null)
                {
                    args.Context.AddString("ERROR: ZoneSystem not available");
                    return;
                }

                Dictionary<Vector2i, ZoneSystem.LocationInstance> locationInstances = zoneSystem.m_locationInstances;
                if (locationInstances == null)
                {
                    args.Context.AddString("ERROR: Location instances not available");
                    return;
                }

                foreach (KeyValuePair<Vector2i, ZoneSystem.LocationInstance> kvp in locationInstances)
                {
                    ZoneSystem.LocationInstance locationInstance = kvp.Value;
                    if (locationInstance.m_placed && locationInstance.m_location != null &&
                        locationInstance.m_location.m_prefabName == locationName)
                    {
                        Vector3 position = locationInstance.m_position;
                        player.TeleportTo(position, player.transform.rotation, distantTeleport: true);
                        args.Context.AddString($"OK: Teleported to {locationName} at {position.x:F0}, {position.y:F0}, {position.z:F0}");
                        return;
                    }
                }

                args.Context.AddString($"ERROR: Location '{locationName}' not found");
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_interact_nearest", "Interact with the nearest interactable object", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                float radius = 30f;
                if (args.Length >= 2)
                {
                    float.TryParse(args[1], out radius);
                }

                Player player = Player.m_localPlayer;
                if (player == null)
                {
                    args.Context.AddString("ERROR: No local player found");
                    return;
                }

                Vector3 playerPos = player.transform.position;
                Collider[] colliders = Physics.OverlapSphere(playerPos, radius);

                float closestDistance = float.MaxValue;
                Interactable? closestInteractable = null;
                string closestName = "";

                foreach (Collider collider in colliders)
                {
                    Interactable interactable = collider.GetComponentInParent<Interactable>();
                    if (interactable == null)
                        continue;

                    // Skip the player itself
                    if (interactable is Player)
                        continue;

                    float distance = Vector3.Distance(playerPos, collider.transform.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestInteractable = interactable;
                        closestName = collider.gameObject.name;
                    }
                }

                if (closestInteractable != null)
                {
                    bool result = closestInteractable.Interact(player, false, false);
                    args.Context.AddString($"OK: Interacted with {closestName} (distance: {closestDistance:F1}m, result: {result})");
                }
                else
                {
                    args.Context.AddString($"ERROR: No interactable found within {radius}m");
                }
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_close_store", "Close the store/trader UI", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (StoreGui.IsVisible())
                {
                    StoreGui.instance.Hide();
                    args.Context.AddString("OK: Store closed");
                }
                else
                {
                    args.Context.AddString("OK: Store was not open");
                }
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_connect", "Queue a server join: cli_connect <host:port> [password]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_connect <host:port> [password]");
                    return;
                }

                if (FejdStartup.instance == null)
                {
                    args.Context.AddString("ERROR: Main menu is not available");
                    return;
                }

                if (ZSteamMatchmaking.instance == null)
                {
                    args.Context.AddString("ERROR: Steam matchmaking is not available");
                    return;
                }

                string address = args[1];
                if (args.Length >= 3)
                {
                    SetServerPassword(args[2]);
                }

                valheimCLIPlugin.RequestAutoStartQueuedJoin();
                ZSteamMatchmaking.instance.QueueServerJoin(address);
                args.Context.AddString($"OK: Queued server join for {address}");
            });

            valheimCLIPlugin.Log.LogInfo("Custom CLI commands registered");
        }

        private static void SetServerPassword(string password)
        {
            PropertyInfo? property = typeof(FejdStartup).GetProperty("ServerPassword", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo? setter = property?.GetSetMethod(true);
            if (setter == null)
            {
                valheimCLIPlugin.Log.LogWarning("Could not set FejdStartup.ServerPassword; server password was not applied.");
                return;
            }

            setter.Invoke(null, new object[] { password });
        }
    }
}
