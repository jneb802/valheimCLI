using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace valheimCLI
{
    public static class CustomCommands
    {
        private static readonly FieldInfo? PlayerInstanceField = typeof(FejdStartup).GetField("m_playerInstance", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? ProfilesField = typeof(FejdStartup).GetField("m_profiles", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? ProfileIndexField = typeof(FejdStartup).GetField("m_profileIndex", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? WorldField = typeof(FejdStartup).GetField("m_world", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo? SetSelectedProfileMethod = typeof(FejdStartup).GetMethod("SetSelectedProfile", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? GameFirstSpawnField = typeof(Game).GetField("m_firstSpawn", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? GameInIntroField = typeof(Game).GetField("m_inIntro", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? GameRequestRespawnField = typeof(Game).GetField("m_requestRespawn", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? StoreGuiTraderField = typeof(StoreGui).GetField("m_trader", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Register()
        {
            new Terminal.ConsoleCommand("cli_create_character", "Create and select a local character: cli_create_character <name> [--replace] [--local]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_create_character <name> [--replace] [--local]");
                    return;
                }

                string characterName = args[1].Trim();
                bool replace = false;
                bool forceLocal = false;
                for (int i = 2; i < args.Length; i++)
                {
                    replace |= args[i].Equals("--replace", StringComparison.OrdinalIgnoreCase);
                    forceLocal |= args[i].Equals("--local", StringComparison.OrdinalIgnoreCase);
                }

                forceLocal |= replace;
                CreateCharacter(characterName, replace, forceLocal, args.Context.AddString);
            });

            new Terminal.ConsoleCommand("cli_select_character", "Select an existing character: cli_select_character <name-or-filename>", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_select_character <name-or-filename>");
                    return;
                }

                SelectCharacter(args[1].Trim(), args.Context.AddString);
            });

            new Terminal.ConsoleCommand("cli_goto_location", "Teleport to a location by prefab name", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_goto_location <location_prefab_name>");
                    return;
                }

                GotoLocation(args[1], args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_teleport", "Teleport the player to exact coordinates: cli_teleport <x> <y> <z>", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 4 ||
                    !float.TryParse(args[1], out float teleportX) ||
                    !float.TryParse(args[2], out float teleportY) ||
                    !float.TryParse(args[3], out float teleportZ))
                {
                    args.Context.AddString("Usage: cli_teleport <x> <y> <z>");
                    return;
                }

                TeleportPlayer(new Vector3(teleportX, teleportY, teleportZ), args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_find_locations", "Find placed locations by prefab or group text: cli_find_locations <text> [limit]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_find_locations <text> [limit]");
                    return;
                }

                int limit = 20;
                if (args.Length >= 3)
                {
                    int.TryParse(args[2], out limit);
                }

                FindLocations(args[1], Math.Max(1, limit), args.Context.AddString);
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

            new Terminal.ConsoleCommand("cli_pick_nearest", "Pick the nearest pickable item by prefab/name: cli_pick_nearest <name> [radius]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_pick_nearest <name> [radius]");
                    return;
                }

                float radius = 80f;
                if (args.Length >= 3)
                {
                    float.TryParse(args[2], out radius);
                }

                PickNearest(args[1], radius, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_spawn_near", "Spawn a prefab near the local player: cli_spawn_near <prefab> [count] [level] [radius]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_spawn_near <prefab> [count] [level] [radius]");
                    return;
                }

                int count = 1;
                if (args.Length >= 3)
                {
                    int.TryParse(args[2], out count);
                }

                int level = 1;
                if (args.Length >= 4)
                {
                    int.TryParse(args[3], out level);
                }

                float radius = 3f;
                if (args.Length >= 5)
                {
                    float.TryParse(args[4], out radius);
                }

                SpawnNear(args[1], count, level, radius, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_spawn_at", "Spawn a prefab at world coordinates: cli_spawn_at <prefab> <x> <y> <z> [count] [level] [radius]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 5)
                {
                    args.Context.AddString("Usage: cli_spawn_at <prefab> <x> <y> <z> [count] [level] [radius]");
                    return;
                }

                if (!float.TryParse(args[2], out float x) || !float.TryParse(args[3], out float y) || !float.TryParse(args[4], out float z))
                {
                    args.Context.AddString("ERROR: Invalid coordinates");
                    return;
                }

                int count = 1;
                if (args.Length >= 6)
                {
                    int.TryParse(args[5], out count);
                }

                int level = 1;
                if (args.Length >= 7)
                {
                    int.TryParse(args[6], out level);
                }

                float radius = 3f;
                if (args.Length >= 8)
                {
                    float.TryParse(args[7], out radius);
                }

                SpawnAt(args[1], x, y, z, count, level, radius, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_spawn_frozen", "Spawn frozen damageable creatures in front of the local player: cli_spawn_frozen <prefab> [count] [level] [distance] [spacing]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_spawn_frozen <prefab> [count] [level] [distance] [spacing]");
                    return;
                }

                int count = 1;
                if (args.Length >= 3)
                {
                    int.TryParse(args[2], out count);
                }

                int level = 1;
                if (args.Length >= 4)
                {
                    int.TryParse(args[3], out level);
                }

                float distance = 12f;
                if (args.Length >= 5)
                {
                    float.TryParse(args[4], out distance);
                }

                float spacing = 3f;
                if (args.Length >= 6)
                {
                    float.TryParse(args[5], out spacing);
                }

                SpawnFrozenNear(args[1], count, level, distance, spacing, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_freeze_nearest_character", "Freeze the nearest damageable non-player character: cli_freeze_nearest_character <name> [radius]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_freeze_nearest_character <name> [radius]");
                    return;
                }

                float radius = 30f;
                if (args.Length >= 3)
                {
                    float.TryParse(args[2], out radius);
                }

                FreezeNearestCharacter(args[1], radius, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_aim_at", "Aim the local player at world coordinates: cli_aim_at <x> <y> <z>", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 4 ||
                    !float.TryParse(args[1], out float x) ||
                    !float.TryParse(args[2], out float y) ||
                    !float.TryParse(args[3], out float z))
                {
                    args.Context.AddString("Usage: cli_aim_at <x> <y> <z>");
                    return;
                }

                AimAtPoint(new Vector3(x, y, z), args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_aim_at_nearest_character", "Aim the local player at the nearest non-player character: cli_aim_at_nearest_character <name> [radius] [heightOffset]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_aim_at_nearest_character <name> [radius] [heightOffset]");
                    return;
                }

                float radius = 50f;
                if (args.Length >= 3)
                {
                    float.TryParse(args[2], out radius);
                }

                float heightOffset = 0.8f;
                if (args.Length >= 4)
                {
                    float.TryParse(args[3], out heightOffset);
                }

                AimAtNearestCharacter(args[1], radius, heightOffset, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_fire_current_weapon", "Fire the equipped weapon through normal player controls: cli_fire_current_weapon [holdSeconds] [waitLoadedSeconds]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                float holdSeconds = 0.15f;
                if (args.Length >= 2)
                {
                    float.TryParse(args[1], out holdSeconds);
                }

                float waitLoadedSeconds = 4f;
                if (args.Length >= 3)
                {
                    float.TryParse(args[2], out waitLoadedSeconds);
                }

                FireCurrentWeapon(holdSeconds, waitLoadedSeconds, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_zdo_resend_destroyed", "Destroy a local ZDO, then resend it through ZDOData: cli_zdo_resend_destroyed <zdoId> [delaySeconds]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_zdo_resend_destroyed <zdoId> [delaySeconds]");
                    return;
                }

                float delaySeconds = 3f;
                if (args.Length >= 3)
                {
                    float.TryParse(args[2], out delaySeconds);
                }

                ResendDestroyedZdo(args[1], Math.Max(0.5f, delaySeconds), args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_send_chat_rpc", "Send a routed chat RPC to the server: cli_send_chat_rpc <message>", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_send_chat_rpc <message>");
                    return;
                }

                SendChatRpc(string.Join(" ", Enumerable.Range(1, args.Length - 1).Select(i => args[i])), args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_damage_nearest_character", "Damage the nearest non-player character by prefab/name: cli_damage_nearest_character <name> [damage] [radius]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_damage_nearest_character <name> [damage] [radius]");
                    return;
                }

                float damage = 15f;
                if (args.Length >= 3)
                {
                    float.TryParse(args[2], out damage);
                }

                float radius = 80f;
                if (args.Length >= 4)
                {
                    float.TryParse(args[3], out radius);
                }

                DamageNearestCharacter(args[1], damage, radius, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_rpc_damage_nearest_character", "Send RPC_Damage to the nearest non-player character: cli_rpc_damage_nearest_character <name> [damage] [radius]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_rpc_damage_nearest_character <name> [damage] [radius]");
                    return;
                }

                float damage = 15f;
                if (args.Length >= 3)
                {
                    float.TryParse(args[2], out damage);
                }

                float radius = 80f;
                if (args.Length >= 4)
                {
                    float.TryParse(args[3], out radius);
                }

                SendDamageRpcToNearestCharacter(args[1], damage, radius, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_rpc_damage_burst_nearest_character", "Send repeated RPC_Damage messages and report client send timing: cli_rpc_damage_burst_nearest_character <name> [damage] [radius] [count]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_rpc_damage_burst_nearest_character <name> [damage] [radius] [count]");
                    return;
                }

                float damage = 1f;
                if (args.Length >= 3)
                {
                    float.TryParse(args[2], out damage);
                }

                float radius = 80f;
                if (args.Length >= 4)
                {
                    float.TryParse(args[3], out radius);
                }

                int count = 20;
                if (args.Length >= 5)
                {
                    int.TryParse(args[4], out count);
                }

                SendDamageRpcBurstToNearestCharacter(args[1], damage, radius, count, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_rpc_use_nearest", "Send a routed interaction RPC to the nearest object: cli_rpc_use_nearest <door|container|pickable> <name> [radius]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 3)
                {
                    args.Context.AddString("Usage: cli_rpc_use_nearest <door|container|pickable> <name> [radius]");
                    return;
                }

                float radius = 30f;
                if (args.Length >= 4)
                {
                    float.TryParse(args[3], out radius);
                }

                SendInteractionRpcToNearest(args[1], args[2], radius, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_set_guardian_power", "Set the current guardian power: cli_set_guardian_power <power>", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_set_guardian_power <power>");
                    return;
                }

                SetGuardianPower(args[1], args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_set_deathlink_choice", "Set the Deathlink choice: cli_set_deathlink_choice <choice>", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_set_deathlink_choice <choice>");
                    return;
                }

                SetDeathlinkChoice(args[1], args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_walk", "Walk forward for a duration: cli_walk <seconds> [run]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2 || !float.TryParse(args[1], out float seconds))
                {
                    args.Context.AddString("Usage: cli_walk <seconds> [run]");
                    return;
                }

                bool run = args.Length >= 3 && args[2].Equals("run", StringComparison.OrdinalIgnoreCase);
                Walk(seconds, run, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_player_state", "Print current player state", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                PrintPlayerState(args.Context.AddString);
            });

            new Terminal.ConsoleCommand("cli_set_tod", "Set local debug time of day: cli_set_tod <0-1|-1>", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2 || !float.TryParse(args[1], out float dayFraction))
                {
                    args.Context.AddString("Usage: cli_set_tod <0-1|-1>");
                    return;
                }

                SetDebugTimeOfDay(dayFraction, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_set_env", "Set local debug environment: cli_set_env <env|reset>", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_set_env <env|reset>");
                    return;
                }

                SetDebugEnvironment(args[1], args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_sleep_state", "Print local player bed flag and world sleep timing state", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                PrintSleepState(args.Context.AddString);
            });

            new Terminal.ConsoleCommand("cli_set_in_bed", "Set local player ZDO inBed flag for validation: cli_set_in_bed <true|false>", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2 || !bool.TryParse(args[1], out bool inBed))
                {
                    args.Context.AddString("Usage: cli_set_in_bed <true|false>");
                    return;
                }

                SetInBedFlag(inBed, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_bed_say", "Attach as if in bed and say a chat message: cli_bed_say <message> [direct]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_bed_say <message> [direct]");
                    return;
                }

                bool direct = args.Length >= 3 && args[args.Length - 1].Equals("direct", StringComparison.OrdinalIgnoreCase);
                int messageArgCount = direct ? args.Length - 2 : args.Length - 1;
                string message = string.Join(" ", Enumerable.Range(1, messageArgCount).Select(i => args[i]));
                AttachBedAndSay(message, direct, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_forged_say_zdo", "Send a Say RPC against a specific ZDO: cli_forged_say_zdo <userId:id> <message>", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 3 || !TryParseZdoId(args[1], out ZDOID targetZdo))
                {
                    args.Context.AddString("Usage: cli_forged_say_zdo <userId:id> <message>");
                    return;
                }

                string message = string.Join(" ", Enumerable.Range(2, args.Length - 2).Select(i => args[i]));
                SendForgedSayToZdo(targetZdo, message, args.Context.AddString);
            });

            new Terminal.ConsoleCommand("cli_clear_inventory", "Clear the local player's inventory for validation tests", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                ClearLocalInventory(args.Context.AddString);
            });

            new Terminal.ConsoleCommand("cli_check_bird_drop", "Check for the carried/drop/death repro signal after normal landing", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                CheckBirdDropSignal(args.Context.AddString);
            });

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

            new Terminal.ConsoleCommand("cli_check_global_key", "Check a ZoneSystem global key: cli_check_global_key <key>", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_check_global_key <key>");
                    return;
                }

                CheckGlobalKey(args[1], args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_store_items", "Print the currently visible trader store items", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                PrintVisibleStoreItems(args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_trader_items_nearest", "Print available items for the nearest trader: cli_trader_items_nearest [radius] [name]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                float radius = 30f;
                if (args.Length >= 2)
                {
                    float.TryParse(args[1], out radius);
                }

                string requestedName = args.Length >= 3 ? args[2] : "";
                PrintNearestTraderItems(radius, requestedName, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_interact_nearest_trader", "Interact with the nearest trader: cli_interact_nearest_trader [radius] [name]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                float radius = 30f;
                if (args.Length >= 2)
                {
                    float.TryParse(args[1], out radius);
                }

                string requestedName = args.Length >= 3 ? args[2] : "";
                InteractNearestTrader(radius, requestedName, args.Context.AddString);
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

            new Terminal.ConsoleCommand("cli_connect_direct", "Join a dedicated server directly: cli_connect_direct <host[:port]> [password]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_connect_direct <host[:port]> [password]");
                    return;
                }

                string address = args[1];
                if (!TryParseHostPort(address, out string host, out int port))
                {
                    args.Context.AddString($"ERROR: Invalid server address '{address}'");
                    return;
                }

                if (args.Length >= 3)
                {
                    SetServerPassword(args[2]);
                }

                StartDedicatedServerJoin(host, port, args.Context.AddString);
            });

            new Terminal.ConsoleCommand("cli_connection_status", "Print the current Valheim network connection status", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                PrintConnectionStatus(args.Context.AddString);
            });

            new Terminal.ConsoleCommand("cli_start_local_world", "Create/select and start a local world: cli_start_local_world <worldName>", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_start_local_world <worldName>");
                    return;
                }

                StartLocalWorld(args[1], args.Context.AddString);
            });

            new Terminal.ConsoleCommand("cli_check_intro_complete", "Check that the first-spawn Valkyrie intro state is cleared", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                CheckIntroComplete(args.Context.AddString);
            });

            new Terminal.ConsoleCommand("cli_logout_save", "Log out through Valheim's normal save path", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                LogoutSave(args.Context.AddString);
            });

            new Terminal.ConsoleCommand("cli_mwl_port_status", "Print More World Locations port runtime state: cli_mwl_port_status [radius]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                float radius = 80f;
                if (args.Length >= 2)
                {
                    float.TryParse(args[1], out radius);
                }

                PrintMwlPortStatus(radius, args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_mwl_goto_port", "Teleport to an MWL port from ShipmentManager.GetPorts: cli_mwl_goto_port [index]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                int index = 0;
                if (args.Length >= 2)
                {
                    int.TryParse(args[1], out index);
                }

                GotoMwlPort(Math.Max(0, index), args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_mwl_clear_shipments", "Clear all currently synced MWL shipments from the server", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                ClearMwlShipments(args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_mwl_port_payment_regression", "Run the MWL port shipment coin-charge regression: cli_mwl_port_payment_regression [itemPrefab] [itemCount]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                string itemPrefab = args.Length >= 2 ? args[1] : "Wood";
                int itemCount = 10;
                if (args.Length >= 3)
                {
                    int.TryParse(args[2], out itemCount);
                }

                RunMwlPortPaymentRegression(itemPrefab, Mathf.Max(1, itemCount), args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_mwl_port_delivery_regression", "Run the MWL port partial-delivery duplicate regression: cli_mwl_port_delivery_regression [itemPrefab] [itemCount]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                string itemPrefab = args.Length >= 2 ? args[1] : "Wood";
                int itemCount = 10;
                if (args.Length >= 3)
                {
                    int.TryParse(args[2], out itemCount);
                }

                RunMwlPortDeliveryRegression(itemPrefab, Mathf.Max(1, itemCount), args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_mwl_port_ownership_seed", "Create a delivered MWL ownership test shipment at the nearest loaded port: cli_mwl_port_ownership_seed [itemPrefab] [itemCount]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                string itemPrefab = args.Length >= 2 ? args[1] : "Wood";
                int itemCount = 10;
                if (args.Length >= 3)
                {
                    int.TryParse(args[2], out itemCount);
                }

                SeedMwlPortOwnershipShipment(itemPrefab, Mathf.Max(1, itemCount), args.Context.AddString);
            }, isCheat: true);

            new Terminal.ConsoleCommand("cli_mwl_port_ownership_check", "Check whether this player can access an MWL ownership test shipment: cli_mwl_port_ownership_check <shipmentId> [blocked|allowed]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 2)
                {
                    args.Context.AddString("Usage: cli_mwl_port_ownership_check <shipmentId> [blocked|allowed]");
                    return;
                }

                string expectation = args.Length >= 3 ? args[2] : "blocked";
                CheckMwlPortOwnershipShipment(args[1], expectation, args.Context.AddString);
            }, isCheat: true);

            valheimCLIPlugin.Log.LogInfo("Custom CLI commands registered");
        }

        private const BindingFlags MwlReflectionFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private sealed class MwlPortContext
        {
            public Type PortType = null!;
            public Type PortInfoType = null!;
            public Type PortUiType = null!;
            public Type ManifestType = null!;
            public Type ShipmentType = null!;
            public Type ShipmentItemType = null!;
            public Type ShipmentStateType = null!;
            public Type ShipmentManagerType = null!;
            public Type PortIdType = null!;
            public FieldInfo PortViewField = null!;
            public FieldInfo PortIdField = null!;
            public FieldInfo PortNameField = null!;
            public FieldInfo PortContainersField = null!;
            public FieldInfo? PortHasOpenDeliveryField;
            public FieldInfo? PortSelectedDeliveryField;
            public MethodInfo SpawnContainerMethod = null!;
            public MethodInfo LoadDeliveryMethod = null!;
            public MethodInfo DestroyContainersMethod = null!;
            public FieldInfo ShipmentsField = null!;
            public MethodInfo GetPortsMethod = null!;
            public FieldInfo ManifestManifestsField = null!;
            public FieldInfo ManifestNameField = null!;
            public FieldInfo ManifestCostField = null!;
            public FieldInfo ManifestChestIdField = null!;
            public FieldInfo PortUiInstanceField = null!;
            public FieldInfo PortUiSelectedDestinationField = null!;
            public FieldInfo PortUiCurrentTabField = null!;
            public MethodInfo PortUiShowMethod = null!;
            public MethodInfo PortUiOnMainButtonMethod = null!;
            public MethodInfo? ShipmentSendToServerMethod;
            public MethodInfo? ShipmentCanAccessMethod;
            public PropertyInfo? CurrencyItemProperty;
            public ConstructorInfo PortInfoConstructor = null!;
            public ConstructorInfo ShipmentConstructor = null!;
            public ConstructorInfo ShipmentItemConstructor = null!;
        }

        public static void PrintMwlPortStatus(float radius, Action<string> addOutput)
        {
            if (!TryGetMwlPortContext(addOutput, out MwlPortContext? context) || context == null)
            {
                return;
            }

            UnityEngine.Object[] loadedPorts = UnityEngine.Object.FindObjectsByType(context.PortType, FindObjectsSortMode.None);
            List<ZDO> portZdos = GetMwlPortZdos(context);
            int serverPortCount = portZdos.Count;
            int shipmentCount = GetMwlShipmentCount(context);
            int manifestCount = GetMwlManifestCount(context);
            object? portUi = context.PortUiInstanceField.GetValue(null);
            Player player = Player.m_localPlayer;

            string nearestText = "nearest=none";
            if (player != null && TryFindNearestMwlPort(context, radius, out object? nearestPort, out string nearestName, out float nearestDistance) && nearestPort != null)
            {
                nearestText = $"nearest='{nearestName}' distance={nearestDistance:F1}m";
            }
            else if (player != null && portZdos.Count > 0)
            {
                ZDO nearestZdo = portZdos
                    .OrderBy(portZdo => Vector3.Distance(player.transform.position, portZdo.GetPosition()))
                    .First();
                Vector3 zdoPosition = nearestZdo.GetPosition();
                nearestText = $"nearestZdo=({zdoPosition.x:F0},{zdoPosition.y:F0},{zdoPosition.z:F0}) distance={Vector3.Distance(player.transform.position, zdoPosition):F1}m";
            }

            string playerText = player != null
                ? $"player=({player.transform.position.x:F0},{player.transform.position.y:F0},{player.transform.position.z:F0})"
                : "player=none";

            addOutput($"OK: MWL_PORT_STATUS loadedPorts={loadedPorts.Length} serverPorts={serverPortCount} shipments={shipmentCount} manifests={manifestCount} portUI={(portUi != null)} {playerText} {nearestText}");
        }

        public static void GotoMwlPort(int index, Action<string> addOutput)
        {
            if (!TryGetMwlPortContext(addOutput, out MwlPortContext? context) || context == null)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            List<ZDO> portZdos = GetMwlPortZdos(context);
            if (portZdos.Count == 0)
            {
                addOutput("ERROR: ShipmentManager.GetPorts returned no MWL port ZDOs");
                return;
            }

            List<ZDO> orderedPorts = portZdos
                .OrderBy(portZdo => Vector3.Distance(player.transform.position, portZdo.GetPosition()))
                .ToList();
            int clampedIndex = Mathf.Clamp(index, 0, orderedPorts.Count - 1);
            ZDO destination = orderedPorts[clampedIndex];
            Vector3 position = destination.GetPosition();
            player.TeleportTo(position + Vector3.up, player.transform.rotation, distantTeleport: true);
            addOutput($"OK: Teleported to MWL port index={clampedIndex} totalPorts={orderedPorts.Count} pos=({position.x:F0},{position.y:F0},{position.z:F0})");
        }

        public static void ClearMwlShipments(Action<string> addOutput)
        {
            if (!TryGetMwlPortContext(addOutput, out MwlPortContext? context) || context == null)
            {
                return;
            }

            IDictionary? shipments = context.ShipmentsField.GetValue(null) as IDictionary;
            if (shipments == null)
            {
                addOutput("ERROR: MWL shipment dictionary is unavailable");
                return;
            }

            MethodInfo? onCollectedMethod = context.ShipmentType.GetMethod("OnCollected", MwlReflectionFlags);
            if (onCollectedMethod == null)
            {
                addOutput("ERROR: MWL Shipment.OnCollected method was not found");
                return;
            }

            List<object> snapshot = new List<object>();
            foreach (DictionaryEntry entry in shipments)
            {
                if (entry.Value != null)
                {
                    snapshot.Add(entry.Value);
                }
            }

            foreach (object shipment in snapshot)
            {
                onCollectedMethod.Invoke(shipment, null);
            }

            addOutput($"OK: MWL_CLEAR_SHIPMENTS requested={snapshot.Count}");
        }

        public static void TeleportPlayer(Vector3 position, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            player.TeleportTo(position, player.transform.rotation, distantTeleport: true);
            addOutput($"OK: Teleported to {position.x:F1}, {position.y:F1}, {position.z:F1}");
        }

        public static void GotoLocation(string locationNameOrGroup, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            ZoneSystem zoneSystem = ZoneSystem.instance;
            if (zoneSystem == null)
            {
                addOutput("ERROR: ZoneSystem not available");
                return;
            }

            Dictionary<Vector2i, ZoneSystem.LocationInstance> locationInstances = zoneSystem.m_locationInstances;
            if (locationInstances == null)
            {
                addOutput("ERROR: Location instances not available");
                return;
            }

            ZoneSystem.LocationInstance? nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (KeyValuePair<Vector2i, ZoneSystem.LocationInstance> kvp in locationInstances)
            {
                ZoneSystem.LocationInstance locationInstance = kvp.Value;
                if (locationInstance.m_location == null)
                {
                    continue;
                }

                bool prefabMatches = locationInstance.m_location.m_prefabName.Equals(locationNameOrGroup, StringComparison.OrdinalIgnoreCase);
                bool groupMatches = locationInstance.m_location.m_group.Equals(locationNameOrGroup, StringComparison.OrdinalIgnoreCase);
                if (!prefabMatches && !groupMatches)
                {
                    continue;
                }

                float distance = Vector3.Distance(player.transform.position, locationInstance.m_position);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = locationInstance;
                }
            }

            if (nearest == null)
            {
                addOutput($"ERROR: Location or group '{locationNameOrGroup}' not found");
                return;
            }

            ZoneSystem.LocationInstance target = nearest.Value;
            Vector3 targetPosition = target.m_position;
            player.TeleportTo(targetPosition, player.transform.rotation, distantTeleport: true);
            addOutput($"OK: Teleported to {target.m_location.m_prefabName} group={target.m_location.m_group} at {targetPosition.x:F0}, {targetPosition.y:F0}, {targetPosition.z:F0} placed={target.m_placed} distance={nearestDistance:F0}");
        }

        public static void FindLocations(string query, int limit, Action<string> addOutput)
        {
            ZoneSystem zoneSystem = ZoneSystem.instance;
            if (zoneSystem == null)
            {
                addOutput("ERROR: ZoneSystem not available");
                return;
            }

            Dictionary<Vector2i, ZoneSystem.LocationInstance> locationInstances = zoneSystem.m_locationInstances;
            if (locationInstances == null)
            {
                addOutput("ERROR: Location instances not available");
                return;
            }

            string normalizedQuery = query.Trim();
            Vector3 playerPosition = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            List<ZoneSystem.LocationInstance> matches = new();
            foreach (KeyValuePair<Vector2i, ZoneSystem.LocationInstance> kvp in locationInstances)
            {
                ZoneSystem.LocationInstance locationInstance = kvp.Value;
                if (locationInstance.m_location == null)
                {
                    continue;
                }

                string prefabName = locationInstance.m_location.m_prefabName ?? "";
                string groupName = locationInstance.m_location.m_group ?? "";
                if (prefabName.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) < 0 &&
                    groupName.IndexOf(normalizedQuery, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                matches.Add(locationInstance);
            }

            matches.Sort((a, b) => Vector3.Distance(playerPosition, a.m_position).CompareTo(Vector3.Distance(playerPosition, b.m_position)));
            int emitted = 0;
            foreach (ZoneSystem.LocationInstance locationInstance in matches)
            {
                if (emitted >= limit)
                {
                    break;
                }

                Vector3 position = locationInstance.m_position;
                float distance = Vector3.Distance(playerPosition, position);
                addOutput($"LOCATION prefab={locationInstance.m_location.m_prefabName} group={locationInstance.m_location.m_group} pos=({position.x:F0},{position.y:F0},{position.z:F0}) placed={locationInstance.m_placed} distance={distance:F0}");
                emitted++;
            }

            addOutput($"OK: FIND_LOCATIONS query='{query}' matched={matches.Count} shown={emitted}");
        }

        public static void RunMwlPortPaymentRegression(string itemPrefab, int itemCount, Action<string> addOutput)
        {
            if (!TryGetMwlPortContext(addOutput, out MwlPortContext? context) || context == null)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            if (!TryFindNearestMwlPort(context, 160f, out object? port, out string portName, out float portDistance) || port == null)
            {
                addOutput("ERROR: No loaded MWL port found within 160m. Teleport to an MWL port and run this again.");
                return;
            }

            ZDO? currentPortZdo = GetMwlPortZdo(context, port);
            if (currentPortZdo == null)
            {
                addOutput("ERROR: Nearest MWL port has no valid ZDO");
                return;
            }

            ZDO? destinationZdo = FindMwlDestinationZdo(context, currentPortZdo);
            if (destinationZdo == null)
            {
                addOutput("ERROR: Need at least two MWL ports available from ShipmentManager.GetPorts()");
                return;
            }

            if (!TryGetCheapestMwlManifest(context, out object? manifest, out string manifestName, out int manifestCost, out _) || manifest == null)
            {
                addOutput("ERROR: No MWL manifests are registered");
                return;
            }

            Container? container = null;
            try
            {
                container = context.SpawnContainerMethod.Invoke(port, new object[] { manifest }) as Container;
                if (container == null)
                {
                    addOutput("ERROR: Failed to spawn MWL manifest container at nearest port");
                    return;
                }

                ItemDrop.ItemData? testItem = container.GetInventory().AddItem(itemPrefab, itemCount, 1, 0, 0L, "");
                if (testItem == null)
                {
                    addOutput($"ERROR: Failed to add test item prefab '{itemPrefab}' to manifest container");
                    context.DestroyContainersMethod.Invoke(port, null);
                    return;
                }

                object? containers = context.PortContainersField.GetValue(port);
                MethodInfo? getCostMethod = containers?.GetType().GetMethod("GetCost", MwlReflectionFlags);
                int expectedCost = getCostMethod != null ? Convert.ToInt32(getCostMethod.Invoke(containers, null)) : manifestCost;
                if (expectedCost <= 0)
                {
                    addOutput($"ERROR: Manifest '{manifestName}' produced non-positive shipping cost {expectedCost}");
                    context.DestroyContainersMethod.Invoke(port, null);
                    return;
                }

                string currencySharedName = GetMwlCurrencySharedName(context);
                string currencyPrefabName = GetMwlCurrencyPrefabName(context);
                Inventory inventory = player.GetInventory();
                int beforeGrantCurrency = inventory.CountItems(currencySharedName);
                inventory.AddItem(currencyPrefabName, expectedCost + 100, 1, 0, 0L, "");
                int beforeCurrency = inventory.CountItems(currencySharedName);
                int beforeShipments = GetMwlShipmentCount(context);

                object? destinationInfo = context.PortInfoConstructor.Invoke(new object[] { destinationZdo });
                object? portUi = context.PortUiInstanceField.GetValue(null);
                if (portUi == null)
                {
                    addOutput("ERROR: MWL PortUI.instance is null");
                    context.DestroyContainersMethod.Invoke(port, null);
                    return;
                }

                context.PortUiShowMethod.Invoke(portUi, new object[] { port });
                context.PortUiSelectedDestinationField.SetValue(portUi, destinationInfo);
                object portsTab = Enum.Parse(context.PortUiCurrentTabField.FieldType, "Ports");
                context.PortUiCurrentTabField.SetValue(portUi, portsTab);
                context.PortUiOnMainButtonMethod.Invoke(portUi, null);

                int afterCurrency = inventory.CountItems(currencySharedName);
                int afterShipments = GetMwlShipmentCount(context);
                int spent = beforeCurrency - afterCurrency;
                string result = spent == expectedCost ? "FIXED" : "BUG_PRESENT";
                addOutput($"OK: MWL_PAYMENT_REGRESSION result={result} port='{portName}' distance={portDistance:F1}m manifest='{manifestName}' item={itemPrefab}x{itemCount} expectedCost={expectedCost} currencyBeforeGrant={beforeGrantCurrency} currencyBefore={beforeCurrency} currencyAfter={afterCurrency} paymentSpent={spent} shipmentsBefore={beforeShipments} shipmentsAfter={afterShipments}");
            }
            catch (TargetInvocationException ex)
            {
                addOutput($"ERROR: MWL payment regression threw {ex.InnerException?.GetType().Name ?? ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
                if (container != null)
                {
                    context.DestroyContainersMethod.Invoke(port, null);
                }
            }
            catch (Exception ex)
            {
                addOutput($"ERROR: MWL payment regression failed: {ex.GetType().Name}: {ex.Message}");
                if (container != null)
                {
                    context.DestroyContainersMethod.Invoke(port, null);
                }
            }
        }

        public static void RunMwlPortDeliveryRegression(string itemPrefab, int itemCount, Action<string> addOutput)
        {
            if (!TryGetMwlPortContext(addOutput, out MwlPortContext? context) || context == null)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            if (!TryFindNearestMwlPort(context, 160f, out object? port, out string portName, out float portDistance) || port == null)
            {
                addOutput("ERROR: No loaded MWL port found within 160m. Teleport to an MWL port and run this again.");
                return;
            }

            if (!TryGetCheapestMwlManifest(context, out object? manifest, out string manifestName, out _, out int chestId) || manifest == null)
            {
                addOutput("ERROR: No MWL manifests are registered");
                return;
            }

            object? shipmentsObject = context.ShipmentsField.GetValue(null);
            IDictionary? shipments = shipmentsObject as IDictionary;
            if (shipments == null)
            {
                addOutput("ERROR: MWL shipment dictionary is unavailable");
                return;
            }

            string shipmentId = "cli-mwl-delivery-test-" + Guid.NewGuid().ToString("N");
            try
            {
                object originPortId = Activator.CreateInstance(context.PortIdType);
                context.PortIdType.GetField("Name")?.SetValue(originPortId, "CLI Origin");
                context.PortIdType.GetField("GUID")?.SetValue(originPortId, "cli-origin-" + Guid.NewGuid().ToString("N"));
                object destinationPortId = context.PortIdField.GetValue(port);

                object shipment = context.ShipmentConstructor.Invoke(new object[] { originPortId, destinationPortId, 1f });
                context.ShipmentType.GetField("ShipmentID")?.SetValue(shipment, shipmentId);
                context.ShipmentType.GetField("State")?.SetValue(shipment, Enum.Parse(context.ShipmentStateType, "Delivered"));
                context.ShipmentType.GetField("ArrivalTime")?.SetValue(shipment, 0d);
                context.ShipmentType.GetField("ExpirationTime")?.SetValue(shipment, ZNet.instance.GetTimeSeconds() + 3600d);

                ItemDrop.ItemData? itemData = CreateDetachedItemData(itemPrefab, itemCount);
                if (itemData == null)
                {
                    addOutput($"ERROR: Failed to create detached test item prefab '{itemPrefab}'");
                    return;
                }

                object shipmentItem = context.ShipmentItemConstructor.Invoke(new object[] { chestId, itemData });
                object? shipmentItemsObject = context.ShipmentType.GetField("Items")?.GetValue(shipment);
                IList? shipmentItems = shipmentItemsObject as IList;
                if (shipmentItems == null)
                {
                    addOutput("ERROR: MWL shipment Items list is unavailable");
                    return;
                }

                shipmentItems.Add(shipmentItem);
                shipments[shipmentId] = shipment;

                bool loaded = Convert.ToBoolean(context.LoadDeliveryMethod.Invoke(port, new object[] { shipment }));
                bool selectedDeliveryStillSet = context.PortSelectedDeliveryField?.GetValue(port) != null;
                bool? hasOpenDelivery = context.PortHasOpenDeliveryField != null
                    ? Convert.ToBoolean(context.PortHasOpenDeliveryField.GetValue(port))
                    : null;
                bool fixedBehavior = loaded && context.PortHasOpenDeliveryField != null && hasOpenDelivery == true && !selectedDeliveryStillSet;
                string result = fixedBehavior ? "FIXED" : "BUG_PRESENT";

                addOutput($"OK: MWL_DELIVERY_REGRESSION result={result} port='{portName}' distance={portDistance:F1}m manifest='{manifestName}' item={itemPrefab}x{itemCount} loaded={loaded} hasOpenDelivery={(hasOpenDelivery.HasValue ? hasOpenDelivery.Value.ToString() : "missing")} portSelectedDeliveryStillSet={selectedDeliveryStillSet} shipmentDictionaryContainsTest={shipments.Contains(shipmentId)}");
            }
            catch (TargetInvocationException ex)
            {
                addOutput($"ERROR: MWL delivery regression threw {ex.InnerException?.GetType().Name ?? ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                addOutput($"ERROR: MWL delivery regression failed: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (shipments.Contains(shipmentId))
                {
                    shipments.Remove(shipmentId);
                }

                context.DestroyContainersMethod.Invoke(port, null);
            }
        }

        public static void SeedMwlPortOwnershipShipment(string itemPrefab, int itemCount, Action<string> addOutput)
        {
            if (!TryGetMwlPortContext(addOutput, out MwlPortContext? context) || context == null)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            if (!TryFindNearestMwlPort(context, 160f, out object? port, out string portName, out float portDistance) || port == null)
            {
                addOutput("ERROR: No loaded MWL port found within 160m. Teleport to an MWL port and run this again.");
                return;
            }

            if (!TryGetCheapestMwlManifest(context, out object? manifest, out string manifestName, out _, out int chestId) || manifest == null)
            {
                addOutput("ERROR: No MWL manifests are registered");
                return;
            }

            object? shipmentsObject = context.ShipmentsField.GetValue(null);
            IDictionary? shipments = shipmentsObject as IDictionary;
            if (shipments == null)
            {
                addOutput("ERROR: MWL shipment dictionary is unavailable");
                return;
            }

            string shipmentId = "cli-mwl-ownership-test-" + Guid.NewGuid().ToString("N");
            try
            {
                object originPortId = Activator.CreateInstance(context.PortIdType);
                context.PortIdType.GetField("Name")?.SetValue(originPortId, "CLI Owner Origin");
                context.PortIdType.GetField("GUID")?.SetValue(originPortId, "cli-owner-origin-" + Guid.NewGuid().ToString("N"));
                object destinationPortId = context.PortIdField.GetValue(port);

                object shipment = context.ShipmentConstructor.Invoke(new object[] { originPortId, destinationPortId, 1f });
                context.ShipmentType.GetField("ShipmentID")?.SetValue(shipment, shipmentId);
                context.ShipmentType.GetField("State")?.SetValue(shipment, Enum.Parse(context.ShipmentStateType, "Delivered"));
                context.ShipmentType.GetField("ArrivalTime")?.SetValue(shipment, 0d);
                context.ShipmentType.GetField("ExpirationTime")?.SetValue(shipment, ZNet.instance.GetTimeSeconds() + 3600d);

                ItemDrop.ItemData? itemData = CreateDetachedItemData(itemPrefab, itemCount);
                if (itemData == null)
                {
                    addOutput($"ERROR: Failed to create detached test item prefab '{itemPrefab}'");
                    return;
                }

                object shipmentItem = context.ShipmentItemConstructor.Invoke(new object[] { chestId, itemData });
                object? shipmentItemsObject = context.ShipmentType.GetField("Items")?.GetValue(shipment);
                IList? shipmentItems = shipmentItemsObject as IList;
                if (shipmentItems == null)
                {
                    addOutput("ERROR: MWL shipment Items list is unavailable");
                    return;
                }

                int beforeShipments = shipments.Count;
                shipmentItems.Add(shipmentItem);
                if (context.ShipmentSendToServerMethod != null)
                {
                    context.ShipmentSendToServerMethod.Invoke(shipment, null);
                }
                else
                {
                    shipments[shipmentId] = shipment;
                }

                string destinationPortGuid = GetMwlPortIdGuid(context, destinationPortId);
                addOutput($"OK: MWL_OWNERSHIP_SEED shipmentId={shipmentId} owner='{player.GetPlayerName()}' playerId={player.GetPlayerID()} port='{portName}' portGuid={destinationPortGuid} distance={portDistance:F1}m manifest='{manifestName}' item={itemPrefab}x{itemCount} shipmentsBefore={beforeShipments} shipmentsLocalAfter={shipments.Count}");
            }
            catch (TargetInvocationException ex)
            {
                addOutput($"ERROR: MWL ownership seed threw {ex.InnerException?.GetType().Name ?? ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
            }
            catch (Exception ex)
            {
                addOutput($"ERROR: MWL ownership seed failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public static void CheckMwlPortOwnershipShipment(string shipmentId, string expectation, Action<string> addOutput)
        {
            if (!TryGetMwlPortContext(addOutput, out MwlPortContext? context) || context == null)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            object? shipmentsObject = context.ShipmentsField.GetValue(null);
            IDictionary? shipments = shipmentsObject as IDictionary;
            if (shipments == null)
            {
                addOutput("ERROR: MWL shipment dictionary is unavailable");
                return;
            }

            if (!shipments.Contains(shipmentId))
            {
                addOutput($"ERROR: MWL_OWNERSHIP_CHECK shipmentId={shipmentId} found=false shipments={shipments.Count}");
                return;
            }

            object? shipment = shipments[shipmentId];
            if (shipment == null)
            {
                addOutput($"ERROR: MWL_OWNERSHIP_CHECK shipmentId={shipmentId} found=true shipment=null shipments={shipments.Count}");
                return;
            }

            string normalizedExpectation = expectation.Equals("allowed", StringComparison.OrdinalIgnoreCase) ? "allowed" : "blocked";
            bool hasOwnershipGate = context.ShipmentCanAccessMethod != null;
            bool canAccess = true;
            if (context.ShipmentCanAccessMethod != null)
            {
                canAccess = Convert.ToBoolean(context.ShipmentCanAccessMethod.Invoke(shipment, new object[] { player }));
            }

            string destinationPortId = Convert.ToString(context.ShipmentType.GetField("DestinationPortID")?.GetValue(shipment)) ?? "";
            bool loaded = false;
            bool destinationLoaded = false;
            string portName = "";
            float portDistance = 0f;
            object? port = FindLoadedMwlPortByGuid(context, destinationPortId, out portName, out portDistance);
            if (port != null)
            {
                destinationLoaded = true;
                try
                {
                    loaded = Convert.ToBoolean(context.LoadDeliveryMethod.Invoke(port, new object[] { shipment }));
                }
                catch (TargetInvocationException ex)
                {
                    addOutput($"ERROR: MWL ownership check load threw {ex.InnerException?.GetType().Name ?? ex.GetType().Name}: {ex.InnerException?.Message ?? ex.Message}");
                    return;
                }
                finally
                {
                    context.DestroyContainersMethod.Invoke(port, null);
                    context.PortSelectedDeliveryField?.SetValue(port, null);
                }
            }
            else if (TryGetMwlPortZdoByGuid(context, destinationPortId, out ZDO? destinationZdo) && destinationZdo != null)
            {
                Vector3 position = destinationZdo.GetPosition();
                player.TeleportTo(position + Vector3.up, player.transform.rotation, distantTeleport: true);
                addOutput($"OK: MWL_OWNERSHIP_CHECK shipmentId={shipmentId} found=true destinationLoaded=false teleported=true destinationPortGuid={destinationPortId} retry=true");
                return;
            }

            string result;
            if (normalizedExpectation == "allowed")
            {
                result = canAccess && loaded ? "OWNER_ACCESS_OK" : "OWNER_ACCESS_FAILED";
            }
            else
            {
                result = !canAccess && !loaded ? "FIXED" : "BUG_PRESENT";
            }

            addOutput($"OK: MWL_OWNERSHIP_CHECK result={result} expectation={normalizedExpectation} shipmentId={shipmentId} player='{player.GetPlayerName()}' playerId={player.GetPlayerID()} hasOwnershipGate={hasOwnershipGate} canAccess={canAccess} loaded={loaded} destinationLoaded={destinationLoaded} destinationPortGuid={destinationPortId} port='{portName}' distance={portDistance:F1}m shipments={shipments.Count}");
        }

        private static bool TryGetMwlPortContext(Action<string> addOutput, out MwlPortContext? context)
        {
            context = null;
            Type? portType = FindTypeByFullName("More_World_Locations_AIO.Port");
            Type? portUiType = FindTypeByFullName("More_World_Locations_AIO.PortUI");
            Type? manifestType = FindTypeByFullName("More_World_Locations_AIO.Manifest");
            Type? shipmentType = FindTypeByFullName("More_World_Locations_AIO.Shipment");
            Type? shipmentItemType = FindTypeByFullName("More_World_Locations_AIO.ShipmentItem");
            Type? shipmentStateType = FindTypeByFullName("More_World_Locations_AIO.ShipmentState");
            Type? shipmentManagerType = FindTypeByFullName("More_World_Locations_AIO.ShipmentManager");
            if (portType == null || portUiType == null || manifestType == null || shipmentType == null || shipmentItemType == null || shipmentStateType == null || shipmentManagerType == null)
            {
                addOutput("ERROR: More_World_Locations_AIO port runtime types are not loaded");
                return false;
            }

            Type? portInfoType = portType.GetNestedType("PortInfo", MwlReflectionFlags);
            Type? portIdType = shipmentManagerType.GetNestedType("PortID", MwlReflectionFlags);
            if (portInfoType == null || portIdType == null)
            {
                addOutput("ERROR: MWL PortInfo or ShipmentManager.PortID type was not found");
                return false;
            }

            MwlPortContext resolved = new MwlPortContext
            {
                PortType = portType,
                PortInfoType = portInfoType,
                PortUiType = portUiType,
                ManifestType = manifestType,
                ShipmentType = shipmentType,
                ShipmentItemType = shipmentItemType,
                ShipmentStateType = shipmentStateType,
                ShipmentManagerType = shipmentManagerType,
                PortIdType = portIdType,
                PortViewField = RequireField(portType, "m_view"),
                PortIdField = RequireField(portType, "m_portID"),
                PortNameField = RequireField(portType, "m_name"),
                PortContainersField = RequireField(portType, "m_containers"),
                PortHasOpenDeliveryField = portType.GetField("m_hasOpenDelivery", MwlReflectionFlags),
                PortSelectedDeliveryField = portType.GetField("m_selectedDelivery", MwlReflectionFlags),
                SpawnContainerMethod = RequireMethod(portType, "SpawnContainer"),
                LoadDeliveryMethod = RequireMethod(portType, "LoadDelivery"),
                DestroyContainersMethod = RequireMethod(portType, "DestroyContainers"),
                ShipmentsField = RequireField(shipmentManagerType, "Shipments"),
                GetPortsMethod = RequireMethod(shipmentManagerType, "GetPorts"),
                ManifestManifestsField = RequireField(manifestType, "Manifests"),
                ManifestNameField = RequireField(manifestType, "Name"),
                ManifestCostField = RequireField(manifestType, "CostToShip"),
                ManifestChestIdField = RequireField(manifestType, "ChestStableHashCode"),
                PortUiInstanceField = RequireField(portUiType, "instance"),
                PortUiSelectedDestinationField = RequireField(portUiType, "m_selectedDestination"),
                PortUiCurrentTabField = RequireField(portUiType, "m_currentTab"),
                PortUiShowMethod = RequireMethod(portUiType, "Show"),
                PortUiOnMainButtonMethod = RequireMethod(portUiType, "OnMainButton"),
                ShipmentSendToServerMethod = shipmentType.GetMethod("SendToServer", MwlReflectionFlags),
                ShipmentCanAccessMethod = shipmentType.GetMethod("CanAccess", MwlReflectionFlags),
                CurrencyItemProperty = shipmentManagerType.GetProperty("CurrencyItem", MwlReflectionFlags),
                PortInfoConstructor = RequireConstructor(portInfoType, new Type[] { typeof(ZDO) }),
                ShipmentConstructor = RequireConstructor(shipmentType, new Type[] { portIdType, portIdType, typeof(float) }),
                ShipmentItemConstructor = RequireConstructor(shipmentItemType, new Type[] { typeof(int), typeof(ItemDrop.ItemData) })
            };

            context = resolved;
            return true;
        }

        private static Type? FindTypeByFullName(string fullName)
        {
            Type? type = Type.GetType(fullName + ", More_World_Locations_AIO");
            if (type != null)
            {
                return type;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in assemblies)
            {
                type = assembly.GetType(fullName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static FieldInfo RequireField(Type type, string fieldName)
        {
            FieldInfo? field = type.GetField(fieldName, MwlReflectionFlags);
            if (field == null)
            {
                throw new MissingFieldException(type.FullName, fieldName);
            }

            return field;
        }

        private static MethodInfo RequireMethod(Type type, string methodName)
        {
            MethodInfo? method = type.GetMethod(methodName, MwlReflectionFlags);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, methodName);
            }

            return method;
        }

        private static ConstructorInfo RequireConstructor(Type type, Type[] parameterTypes)
        {
            ConstructorInfo? constructor = type.GetConstructor(MwlReflectionFlags, null, parameterTypes, null);
            if (constructor == null)
            {
                throw new MissingMethodException(type.FullName, ".ctor");
            }

            return constructor;
        }

        private static bool TryFindNearestMwlPort(MwlPortContext context, float radius, out object? nearestPort, out string nearestName, out float nearestDistance)
        {
            nearestPort = null;
            nearestName = "";
            nearestDistance = float.MaxValue;
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return false;
            }

            UnityEngine.Object[] ports = UnityEngine.Object.FindObjectsByType(context.PortType, FindObjectsSortMode.None);
            foreach (UnityEngine.Object candidate in ports)
            {
                Component? component = candidate as Component;
                if (component == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(player.transform.position, component.transform.position);
                if (distance > radius || distance >= nearestDistance)
                {
                    continue;
                }

                nearestPort = candidate;
                nearestName = Convert.ToString(context.PortNameField.GetValue(candidate)) ?? candidate.name;
                nearestDistance = distance;
            }

            return nearestPort != null;
        }

        private static ZDO? GetMwlPortZdo(MwlPortContext context, object port)
        {
            ZNetView? view = context.PortViewField.GetValue(port) as ZNetView;
            if (view == null || !view.IsValid())
            {
                return null;
            }

            return view.GetZDO();
        }

        private static ZDO? FindMwlDestinationZdo(MwlPortContext context, ZDO currentPortZdo)
        {
            List<ZDO> ports = GetMwlPortZdos(context);
            foreach (ZDO zdo in ports)
            {
                if (!zdo.m_uid.Equals(currentPortZdo.m_uid))
                {
                    return zdo;
                }
            }

            return null;
        }

        private static object? FindLoadedMwlPortByGuid(MwlPortContext context, string portGuid, out string portName, out float portDistance)
        {
            portName = "";
            portDistance = 0f;
            if (string.IsNullOrEmpty(portGuid))
            {
                return null;
            }

            Player player = Player.m_localPlayer;
            UnityEngine.Object[] ports = UnityEngine.Object.FindObjectsByType(context.PortType, FindObjectsSortMode.None);
            foreach (UnityEngine.Object candidate in ports)
            {
                object? portId = context.PortIdField.GetValue(candidate);
                if (!string.Equals(GetMwlPortIdGuid(context, portId), portGuid, StringComparison.Ordinal))
                {
                    continue;
                }

                Component? component = candidate as Component;
                portName = Convert.ToString(context.PortNameField.GetValue(candidate)) ?? candidate.name;
                portDistance = player != null && component != null
                    ? Vector3.Distance(player.transform.position, component.transform.position)
                    : 0f;
                return candidate;
            }

            return null;
        }

        private static bool TryGetMwlPortZdoByGuid(MwlPortContext context, string portGuid, out ZDO? portZdo)
        {
            portZdo = null;
            foreach (ZDO zdo in GetMwlPortZdos(context))
            {
                object portId = Activator.CreateInstance(context.PortIdType);
                context.PortIdType.GetField("GUID")?.SetValue(portId, zdo.GetString("PortGUID".GetStableHashCode()));
                if (!string.Equals(GetMwlPortIdGuid(context, portId), portGuid, StringComparison.Ordinal))
                {
                    continue;
                }

                portZdo = zdo;
                return true;
            }

            return false;
        }

        private static string GetMwlPortIdGuid(MwlPortContext context, object? portId)
        {
            if (portId == null)
            {
                return "";
            }

            return Convert.ToString(context.PortIdType.GetField("GUID")?.GetValue(portId)) ?? "";
        }

        private static List<ZDO> GetMwlPortZdos(MwlPortContext context)
        {
            List<ZDO> portZdos = new List<ZDO>();
            object? portsObject = context.GetPortsMethod.Invoke(null, null);
            IEnumerable? ports = portsObject as IEnumerable;
            if (ports == null)
            {
                return portZdos;
            }

            foreach (object zdoObject in ports)
            {
                ZDO? zdo = zdoObject as ZDO;
                if (zdo != null)
                {
                    portZdos.Add(zdo);
                }
            }

            return portZdos;
        }

        private static bool TryGetCheapestMwlManifest(MwlPortContext context, out object? manifest, out string manifestName, out int manifestCost, out int chestId)
        {
            manifest = null;
            manifestName = "";
            manifestCost = int.MaxValue;
            chestId = 0;
            IDictionary? manifests = context.ManifestManifestsField.GetValue(null) as IDictionary;
            if (manifests == null || manifests.Count == 0)
            {
                return false;
            }

            foreach (DictionaryEntry entry in manifests)
            {
                object? candidate = entry.Value;
                if (candidate == null)
                {
                    continue;
                }

                int candidateCost = Convert.ToInt32(context.ManifestCostField.GetValue(candidate));
                if (candidateCost >= manifestCost)
                {
                    continue;
                }

                manifest = candidate;
                manifestName = Convert.ToString(context.ManifestNameField.GetValue(candidate)) ?? "Manifest";
                manifestCost = candidateCost;
                chestId = Convert.ToInt32(context.ManifestChestIdField.GetValue(candidate));
            }

            return manifest != null;
        }

        private static int GetMwlShipmentCount(MwlPortContext context)
        {
            ICollection? shipments = context.ShipmentsField.GetValue(null) as ICollection;
            return shipments?.Count ?? -1;
        }

        private static int GetMwlManifestCount(MwlPortContext context)
        {
            ICollection? manifests = context.ManifestManifestsField.GetValue(null) as ICollection;
            return manifests?.Count ?? -1;
        }

        private static string GetMwlCurrencySharedName(MwlPortContext context)
        {
            ItemDrop.ItemData? itemData = context.CurrencyItemProperty?.GetValue(null) as ItemDrop.ItemData;
            return itemData?.m_shared?.m_name ?? "$item_coins";
        }

        private static string GetMwlCurrencyPrefabName(MwlPortContext context)
        {
            ItemDrop.ItemData? itemData = context.CurrencyItemProperty?.GetValue(null) as ItemDrop.ItemData;
            return itemData?.m_dropPrefab != null ? itemData.m_dropPrefab.name : "Coins";
        }

        private static ItemDrop.ItemData? CreateDetachedItemData(string itemPrefab, int itemCount)
        {
            if (ObjectDB.instance == null)
            {
                return null;
            }

            GameObject prefab = ObjectDB.instance.GetItemPrefab(itemPrefab);
            if (prefab == null)
            {
                return null;
            }

            ItemDrop itemDrop = prefab.GetComponent<ItemDrop>();
            if (itemDrop == null)
            {
                return null;
            }

            ItemDrop.ItemData itemData = itemDrop.m_itemData.Clone();
            itemData.m_stack = itemCount;
            return itemData;
        }

        public static void CreateCharacter(string characterName, bool replace, bool forceLocal, Action<string> addOutput)
        {
            if (characterName.Length < 3)
            {
                addOutput("ERROR: Character name must be at least 3 characters");
                return;
            }

            if (characterName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                addOutput("ERROR: Character name contains invalid filename characters");
                return;
            }

            FejdStartup fejd = FejdStartup.instance;
            if (fejd == null)
            {
                addOutput("ERROR: Main menu is not available");
                return;
            }

            GameObject? playerInstance = PlayerInstanceField?.GetValue(fejd) as GameObject;
            Player? previewPlayer = playerInstance != null ? playerInstance.GetComponent<Player>() : null;
            if (previewPlayer == null)
            {
                addOutput("ERROR: Character preview player is not available");
                return;
            }

            string filename = characterName.ToLowerInvariant();
            FileHelpers.FileSource fileSource = forceLocal ? FileHelpers.FileSource.Local : FileHelpers.FileSource.Auto;
            if (PlayerProfile.HaveProfile(filename))
            {
                if (!replace)
                {
                    addOutput($"ERROR: Character '{characterName}' already exists. Use --replace to recreate it.");
                    return;
                }

                PlayerProfile.RemoveProfile(filename);
                SaveSystem.InvalidateCache();
            }

            PlayerProfile profile = new PlayerProfile(filename, fileSource);
            if (forceLocal)
            {
                profile.m_fileSource = FileHelpers.FileSource.Local;
            }

            previewPlayer.GiveDefaultItems();
            profile.SetName(characterName);
            profile.SavePlayerData(previewPlayer);
            if (!profile.Save())
            {
                addOutput($"ERROR: Failed to save character '{characterName}'");
                return;
            }

            SaveSystem.InvalidateCache();
            ProfilesField?.SetValue(fejd, null);
            SetSelectedProfileMethod?.Invoke(fejd, new object[] { filename });
            PlatformPrefs.SetString("profile", filename);
            Game.SetProfile(filename, profile.m_fileSource);

            addOutput($"OK: Created and selected character '{characterName}' ({profile.m_fileSource})");
        }

        public static void SelectCharacter(string characterNameOrFilename, Action<string> addOutput)
        {
            if (characterNameOrFilename.Length < 3)
            {
                addOutput("ERROR: Character name must be at least 3 characters");
                return;
            }

            FejdStartup fejd = FejdStartup.instance;
            if (fejd == null)
            {
                addOutput("ERROR: Main menu is not available");
                return;
            }

            SaveSystem.InvalidateCache();
            List<PlayerProfile> profiles = SaveSystem.GetAllPlayerProfiles();
            if (profiles.Count == 0)
            {
                addOutput("ERROR: No local character profile is available");
                return;
            }

            string requested = characterNameOrFilename.Trim();
            string requestedFilename = requested.ToLowerInvariant();
            PlayerProfile? profile = profiles.FirstOrDefault(candidate =>
                candidate.GetFilename().Equals(requestedFilename, StringComparison.OrdinalIgnoreCase) ||
                candidate.GetName().Equals(requested, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
            {
                addOutput($"ERROR: Character '{characterNameOrFilename}' was not found");
                return;
            }

            ProfilesField?.SetValue(fejd, profiles);
            SetSelectedProfileMethod?.Invoke(fejd, new object[] { profile.GetFilename() });
            PlatformPrefs.SetString("profile", profile.GetFilename());
            Game.SetProfile(profile.GetFilename(), profile.m_fileSource);

            addOutput($"OK: Selected character '{profile.GetName()}' ({profile.GetFilename()}, {profile.m_fileSource})");
        }

        public static void SpawnNear(string prefabName, int count, int level, float radius, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            ZNetScene znetScene = ZNetScene.instance;
            if (znetScene == null)
            {
                addOutput("ERROR: ZNetScene not available");
                return;
            }

            string resolvedName = ResolvePrefabName(prefabName, znetScene);
            GameObject prefab = znetScene.GetPrefab(resolvedName);
            if (prefab == null)
            {
                addOutput($"ERROR: Prefab '{prefabName}' not found");
                return;
            }

            count = Mathf.Clamp(count, 1, 20);
            level = Mathf.Clamp(level, 1, 10);
            radius = Mathf.Clamp(radius, 0.5f, 50f);

            Vector3 basePosition = player.transform.position + player.transform.forward * radius + Vector3.up;
            SpawnPrefabAt(prefab, resolvedName, basePosition, count, level, radius, "near player", addOutput);
        }

        public static void SpawnAt(string prefabName, float x, float y, float z, int count, int level, float radius, Action<string> addOutput)
        {
            ZNetScene znetScene = ZNetScene.instance;
            if (znetScene == null)
            {
                addOutput("ERROR: ZNetScene not available");
                return;
            }

            string resolvedName = ResolvePrefabName(prefabName, znetScene);
            GameObject prefab = znetScene.GetPrefab(resolvedName);
            if (prefab == null)
            {
                addOutput($"ERROR: Prefab '{prefabName}' not found");
                return;
            }

            count = Mathf.Clamp(count, 1, 20);
            level = Mathf.Clamp(level, 1, 10);
            radius = Mathf.Clamp(radius, 0.5f, 50f);

            SpawnPrefabAt(prefab, resolvedName, new Vector3(x, y, z), count, level, radius, "at", addOutput);
        }

        public static void SpawnFrozenNear(string prefabName, int count, int level, float distance, float spacing, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            ZNetScene znetScene = ZNetScene.instance;
            if (znetScene == null)
            {
                addOutput("ERROR: ZNetScene not available");
                return;
            }

            string resolvedName = ResolvePrefabName(prefabName, znetScene);
            GameObject prefab = znetScene.GetPrefab(resolvedName);
            if (prefab == null)
            {
                addOutput($"ERROR: Prefab '{prefabName}' not found");
                return;
            }

            if (prefab.GetComponent<Character>() == null)
            {
                addOutput($"ERROR: Prefab '{resolvedName}' is not a character");
                return;
            }

            count = Mathf.Clamp(count, 1, 20);
            level = Mathf.Clamp(level, 1, 10);
            distance = Mathf.Clamp(distance, 1f, 80f);
            spacing = Mathf.Clamp(spacing, 0.5f, 20f);

            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 center = player.transform.position + forward * distance;
            Quaternion rotation = Quaternion.LookRotation(-forward, Vector3.up);

            List<string> zdoIds = new();
            for (int i = 0; i < count; i++)
            {
                float offset = (i - (count - 1) * 0.5f) * spacing;
                Vector3 position = center + right * offset;
                GameObject spawned = UnityEngine.Object.Instantiate(prefab, position, rotation);
                Character character = spawned.GetComponent<Character>();
                if (character == null)
                {
                    UnityEngine.Object.Destroy(spawned);
                    continue;
                }

                if (level > 1)
                {
                    character.SetLevel(level);
                }

                FreezeCharacter(spawned, position, rotation);
                ZNetView nview = spawned.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid())
                {
                    zdoIds.Add(nview.GetZDO().m_uid.ToString());
                }
            }

            string zdoText = zdoIds.Count > 0 ? string.Join(",", zdoIds) : "none";
            addOutput($"OK: Spawned frozen {count}x {resolvedName} distance={distance:F1} spacing={spacing:F1}; zdo={zdoText}");
        }

        public static void FreezeNearestCharacter(string requestedName, float radius, Action<string> addOutput)
        {
            Character? character = FindNearestCharacter(requestedName, radius, out string bestName, out float bestDistance);
            if (character == null)
            {
                addOutput($"ERROR: No non-player character matching '{requestedName}' found within {radius:F1}m");
                return;
            }

            FreezeCharacter(character.gameObject, character.transform.position, character.transform.rotation);
            addOutput($"OK: Frozen {bestName} at distance {bestDistance:F1}m");
        }

        public static void AimAtPoint(Vector3 point, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            Vector3 origin = player.GetEyePoint();
            Vector3 direction = point - origin;
            if (direction.sqrMagnitude < 0.001f)
            {
                addOutput("ERROR: Aim target is too close to player eye position");
                return;
            }

            player.AttackTowardsPlayerLookDir = true;
            player.SetLookDir(direction.normalized);
            player.FaceLookDirection();
            addOutput($"OK: Aimed at {point.x:F2},{point.y:F2},{point.z:F2}");
        }

        public static void AimAtNearestCharacter(string requestedName, float radius, float heightOffset, Action<string> addOutput)
        {
            Character? character = FindNearestCharacter(requestedName, radius, out string bestName, out float bestDistance);
            if (character == null)
            {
                addOutput($"ERROR: No non-player character matching '{requestedName}' found within {radius:F1}m");
                return;
            }

            Vector3 point = character.GetCenterPoint() + Vector3.up * heightOffset;
            AimAtPoint(point, addOutput);
            addOutput($"OK: Aim target={bestName} distance={bestDistance:F1}m point={point.x:F2},{point.y:F2},{point.z:F2}");
        }

        public static void FireCurrentWeapon(float holdSeconds, float waitLoadedSeconds, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon == null)
            {
                addOutput("ERROR: No current weapon equipped");
                return;
            }

            if (valheimCLIPlugin.Instance == null)
            {
                addOutput("ERROR: valheimCLI plugin instance is not available");
                return;
            }

            holdSeconds = Mathf.Clamp(holdSeconds, 0.05f, 10f);
            waitLoadedSeconds = Mathf.Clamp(waitLoadedSeconds, 0f, 30f);
            valheimCLIPlugin.Instance.StartCoroutine(FireCurrentWeaponRoutine(holdSeconds, waitLoadedSeconds));
            addOutput($"OK: Queued fire currentWeapon='{weapon.m_shared.m_name}' holdSeconds={holdSeconds:F2} waitLoadedSeconds={waitLoadedSeconds:F1}");
        }

        private static void SpawnPrefabAt(GameObject prefab, string resolvedName, Vector3 basePosition, int count, int level, float radius, string positionLabel, Action<string> addOutput)
        {
            List<string> zdoIds = new();
            for (int i = 0; i < count; i++)
            {
                Vector3 offset = count == 1 ? Vector3.zero : UnityEngine.Random.insideUnitSphere * Math.Min(radius, 10f);
                offset.y = 0f;
                GameObject spawned = UnityEngine.Object.Instantiate(prefab, basePosition + offset, Quaternion.identity);

                if (level > 1)
                {
                    ItemDrop itemDrop = spawned.GetComponent<ItemDrop>();
                    if (itemDrop != null)
                    {
                        itemDrop.SetQuality(Mathf.Min(level, 4));
                    }
                    spawned.GetComponent<Character>()?.SetLevel(level);
                }

                ZNetView nview = spawned.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid())
                {
                    zdoIds.Add(nview.GetZDO().m_uid.ToString());
                }
            }

            string zdoText = zdoIds.Count > 0 ? string.Join(",", zdoIds) : "none";
            addOutput($"OK: Spawned {count}x {resolvedName} {positionLabel} {basePosition.x:F1},{basePosition.y:F1},{basePosition.z:F1}; zdo={zdoText}");
        }

        public static void PickNearest(string requestedName, float radius, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            string requested = requestedName.Trim();
            Vector3 playerPos = player.transform.position;
            Collider[] colliders = Physics.OverlapSphere(playerPos, radius);
            Pickable? bestPickable = null;
            PickableItem? bestPickableItem = null;
            string bestName = "";
            float bestDistance = float.MaxValue;

            foreach (Collider collider in colliders)
            {
                Pickable pickable = collider.GetComponentInParent<Pickable>();
                if (pickable != null && pickable.CanBePicked() && PickableMatches(pickable, requested))
                {
                    float distance = Vector3.Distance(playerPos, pickable.transform.position);
                    if (distance < bestDistance)
                    {
                        bestPickable = pickable;
                        bestPickableItem = null;
                        bestName = GetPickableName(pickable);
                        bestDistance = distance;
                    }
                }

                PickableItem pickableItem = collider.GetComponentInParent<PickableItem>();
                if (pickableItem != null && PickableItemMatches(pickableItem, requested))
                {
                    float distance = Vector3.Distance(playerPos, pickableItem.transform.position);
                    if (distance < bestDistance)
                    {
                        bestPickable = null;
                        bestPickableItem = pickableItem;
                        bestName = GetPickableItemName(pickableItem);
                        bestDistance = distance;
                    }
                }
            }

            if (bestPickable != null)
            {
                bool result = bestPickable.Interact(player, false, false);
                addOutput($"OK: Picked {bestName} at distance {bestDistance:F1}m (result={result})");
                return;
            }

            if (bestPickableItem != null)
            {
                bool result = bestPickableItem.Interact(player, false, false);
                addOutput($"OK: Picked {bestName} at distance {bestDistance:F1}m (result={result})");
                return;
            }

            addOutput($"ERROR: No pickable matching '{requestedName}' found within {radius:F1}m");
        }

        public static void DamageNearestCharacter(string requestedName, float damage, float radius, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            if (damage <= 0f)
            {
                addOutput("ERROR: Damage must be greater than zero");
                return;
            }

            string requested = requestedName.Trim();
            Vector3 playerPos = player.transform.position;
            Collider[] colliders = Physics.OverlapSphere(playerPos, radius);
            Character? bestCharacter = null;
            string bestName = "";
            float bestDistance = float.MaxValue;

            foreach (Collider collider in colliders)
            {
                Character character = collider.GetComponentInParent<Character>();
                if (character == null || character is Player || character.IsDead())
                    continue;
                if (!CharacterMatches(character, requested))
                    continue;

                float distance = Vector3.Distance(playerPos, character.transform.position);
                if (distance < bestDistance)
                {
                    bestCharacter = character;
                    bestName = GetCharacterName(character);
                    bestDistance = distance;
                }
            }

            if (bestCharacter == null)
            {
                addOutput($"ERROR: No non-player character matching '{requestedName}' found within {radius:F1}m");
                return;
            }

            Vector3 direction = bestCharacter.transform.position - player.transform.position;
            HitData hit = new()
            {
                m_hitType = HitData.HitType.PlayerHit,
                m_skill = Skills.SkillType.Clubs,
                m_point = bestCharacter.transform.position + Vector3.up,
                m_dir = direction.sqrMagnitude > 0.01f ? direction.normalized : player.transform.forward,
                m_pushForce = 2f,
                m_backstabBonus = 1f,
                m_staggerMultiplier = 1f,
                m_dodgeable = true,
                m_blockable = true,
            };
            hit.m_damage.m_blunt = damage;
            hit.SetAttacker(player);
            bestCharacter.Damage(hit);
            addOutput($"OK: Damaged {bestName} for {damage:F1} blunt damage at distance {bestDistance:F1}m");
        }

        public static void SendChatRpc(string text, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            if (ZRoutedRpc.instance == null)
            {
                addOutput("ERROR: ZRoutedRpc is not available");
                return;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.Everybody,
                "ChatMessage",
                player.GetHeadPoint(),
                (int)Talker.Type.Shout,
                UserInfo.GetLocalUser(),
                text);
            addOutput("OK: Sent ChatMessage routed RPC");
        }

        public static void SendDamageRpcToNearestCharacter(string requestedName, float damage, float radius, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            if (damage <= 0f)
            {
                addOutput("ERROR: Damage must be greater than zero");
                return;
            }

            Character? character = FindNearestCharacter(requestedName, radius, out string bestName, out float bestDistance);
            if (character == null)
            {
                addOutput($"ERROR: No non-player character matching '{requestedName}' found within {radius:F1}m");
                return;
            }

            ZNetView nview = character.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                addOutput($"ERROR: Character '{bestName}' has no valid ZNetView");
                return;
            }

            if (ZRoutedRpc.instance == null)
            {
                addOutput("ERROR: ZRoutedRpc is not available");
                return;
            }

            HitData hit = BuildPlayerHit(player, character.transform.position, damage);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, nview.GetZDO().m_uid, "RPC_Damage", hit);
            addOutput($"OK: Sent RPC_Damage for {bestName} at distance {bestDistance:F1}m");
        }

        public static void SendDamageRpcBurstToNearestCharacter(string requestedName, float damage, float radius, int count, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            if (ZRoutedRpc.instance == null)
            {
                addOutput("ERROR: ZRoutedRpc is not available");
                return;
            }

            if (damage <= 0f)
            {
                addOutput("ERROR: Damage must be greater than zero");
                return;
            }

            if (count <= 0)
            {
                addOutput("ERROR: Count must be greater than zero");
                return;
            }

            count = Math.Min(count, 500);
            Character? character = FindNearestCharacter(requestedName, radius, out string bestName, out float bestDistance);
            if (character == null)
            {
                addOutput($"ERROR: No non-player character matching '{requestedName}' found within {radius:F1}m");
                return;
            }

            ZNetView nview = character.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                addOutput($"ERROR: Character '{bestName}' has no valid ZNetView");
                return;
            }

            ZDOID targetZdo = nview.GetZDO().m_uid;
            long totalTicks = 0L;
            long maxTicks = 0L;
            Stopwatch totalStopwatch = Stopwatch.StartNew();

            for (int i = 0; i < count; i++)
            {
                HitData hit = BuildPlayerHit(player, character.transform.position, damage);
                long started = Stopwatch.GetTimestamp();
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, targetZdo, "RPC_Damage", hit);
                long elapsed = Stopwatch.GetTimestamp() - started;
                totalTicks += elapsed;
                if (elapsed > maxTicks)
                    maxTicks = elapsed;
            }

            totalStopwatch.Stop();
            double averageMilliseconds = totalTicks * 1000.0 / Stopwatch.Frequency / count;
            double maxMilliseconds = maxTicks * 1000.0 / Stopwatch.Frequency;
            addOutput($"OK: Sent RPC_Damage burst count={count} target={bestName} targetZdo={targetZdo} distance={bestDistance:F1}m avgMs={averageMilliseconds:F4} maxMs={maxMilliseconds:F4} totalMs={totalStopwatch.Elapsed.TotalMilliseconds:F4}");
        }

        public static void SendInteractionRpcToNearest(string kind, string requestedName, float radius, Action<string> addOutput)
        {
            string normalizedKind = kind.Trim().ToLowerInvariant();
            switch (normalizedKind)
            {
                case "door":
                    SendObjectRpc<Door>(requestedName, radius, "UseDoor", addOutput, true);
                    return;
                case "container":
                    long playerId = Game.instance != null ? Game.instance.GetPlayerProfile().GetPlayerID() : 0L;
                    if (playerId == 0L)
                    {
                        addOutput("ERROR: No player profile ID found");
                        return;
                    }
                    SendObjectRpc<Container>(requestedName, radius, "RequestOpen", addOutput, playerId);
                    return;
                case "pickable":
                    SendObjectRpc<Pickable>(requestedName, radius, "RPC_Pick", addOutput, 0);
                    return;
                default:
                    addOutput("ERROR: Type must be door, container, or pickable");
                    return;
            }
        }

        public static void CheckGlobalKey(string key, Action<string> addOutput)
        {
            if (ZoneSystem.instance == null)
            {
                addOutput("ERROR: ZoneSystem not available");
                return;
            }

            string normalizedKey = key.Trim();
            if (normalizedKey.Length == 0)
            {
                addOutput("ERROR: Global key is empty");
                return;
            }

            bool isSet = ZoneSystem.instance.GetGlobalKey(normalizedKey);
            addOutput($"OK: globalKey={normalizedKey} set={isSet}");
        }

        public static void PrintVisibleStoreItems(Action<string> addOutput)
        {
            if (StoreGui.instance == null || !StoreGui.IsVisible())
            {
                addOutput("ERROR: Store is not visible");
                return;
            }

            Trader? trader = StoreGuiTraderField?.GetValue(StoreGui.instance) as Trader;
            if (trader == null)
            {
                addOutput("ERROR: Store trader is not available");
                return;
            }

            PrintTraderItems(trader, "visible-store", 0f, addOutput);
        }

        public static void PrintNearestTraderItems(float radius, string requestedName, Action<string> addOutput)
        {
            radius = Mathf.Clamp(radius, 0.5f, 80f);
            Trader? trader = FindNearestComponent<Trader>(requestedName, radius, out _, out float distance);
            if (trader == null)
            {
                string target = string.IsNullOrWhiteSpace(requestedName) ? "trader" : $"trader matching '{requestedName}'";
                addOutput($"ERROR: No {target} found within {radius:F1}m");
                return;
            }

            PrintTraderItems(trader, "nearest-trader", distance, addOutput);
        }

        public static void InteractNearestTrader(float radius, string requestedName, Action<string> addOutput)
        {
            radius = Mathf.Clamp(radius, 0.5f, 80f);
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            Trader? trader = FindNearestComponent<Trader>(requestedName, radius, out string bestName, out float distance);
            if (trader == null)
            {
                string target = string.IsNullOrWhiteSpace(requestedName) ? "trader" : $"trader matching '{requestedName}'";
                addOutput($"ERROR: No {target} found within {radius:F1}m");
                return;
            }

            bool result = trader.Interact(player, false, false);
            bool storeVisible = StoreGui.instance != null && StoreGui.IsVisible();
            addOutput($"OK: Interacted with trader={bestName} distance={distance:F1}m result={result} storeVisible={storeVisible}");
        }

        private static void PrintTraderItems(Trader trader, string source, float distance, Action<string> addOutput)
        {
            List<Trader.TradeItem> items = trader.GetAvailableItems();
            string traderName = Utils.GetPrefabName(trader.gameObject);
            string distanceText = distance > 0f ? $" distance={distance:F1}m" : "";
            addOutput($"OK: {source} trader={traderName}{distanceText} itemCount={items.Count}");

            foreach (Trader.TradeItem item in items)
            {
                string prefabName = item.m_prefab != null ? item.m_prefab.name : "<null>";
                string displayName = item.m_prefab?.m_itemData?.m_shared?.m_name ?? "";
                string requiredKey = item.m_requiredGlobalKey ?? "";
                addOutput($"ITEM: prefab={prefabName} name={displayName} price={item.m_price} stack={item.m_stack} requiredGlobalKey={requiredKey}");
            }
        }

        private static void SendObjectRpc<T>(string requestedName, float radius, string methodName, Action<string> addOutput, params object[] parameters)
            where T : Component
        {
            T? component = FindNearestComponent<T>(requestedName, radius, out string bestName, out float bestDistance);
            if (component == null)
            {
                addOutput($"ERROR: No {typeof(T).Name} matching '{requestedName}' found within {radius:F1}m");
                return;
            }

            ZNetView nview = component.GetComponentInParent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                addOutput($"ERROR: Object '{bestName}' has no valid ZNetView");
                return;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, nview.GetZDO().m_uid, methodName, parameters);
            addOutput($"OK: Sent {methodName} for {bestName} at distance {bestDistance:F1}m");
        }

        private static Character? FindNearestCharacter(string requestedName, float radius, out string bestName, out float bestDistance)
        {
            Player player = Player.m_localPlayer;
            bestName = "";
            bestDistance = float.MaxValue;
            Character? bestCharacter = null;
            if (player == null)
                return null;

            Vector3 playerPos = player.transform.position;
            foreach (Collider collider in Physics.OverlapSphere(playerPos, radius))
            {
                Character character = collider.GetComponentInParent<Character>();
                if (character == null || character is Player || character.IsDead())
                    continue;
                if (!CharacterMatches(character, requestedName))
                    continue;

                float distance = Vector3.Distance(playerPos, character.transform.position);
                if (distance < bestDistance)
                {
                    bestCharacter = character;
                    bestName = GetCharacterName(character);
                    bestDistance = distance;
                }
            }

            return bestCharacter;
        }

        private static T? FindNearestComponent<T>(string requestedName, float radius, out string bestName, out float bestDistance)
            where T : Component
        {
            Player player = Player.m_localPlayer;
            bestName = "";
            bestDistance = float.MaxValue;
            T? bestComponent = null;
            if (player == null)
                return null;

            Vector3 playerPos = player.transform.position;
            foreach (Collider collider in Physics.OverlapSphere(playerPos, radius))
            {
                T component = collider.GetComponentInParent<T>();
                if (component == null)
                    continue;
                if (!ObjectMatches(component.gameObject, requestedName))
                    continue;

                float distance = Vector3.Distance(playerPos, component.transform.position);
                if (distance < bestDistance)
                {
                    bestComponent = component;
                    bestName = Utils.GetPrefabName(component.gameObject);
                    bestDistance = distance;
                }
            }

            return bestComponent;
        }

        private static bool ObjectMatches(GameObject gameObject, string requestedName)
        {
            string requested = requestedName.Trim();
            if (requested.Length == 0)
                return true;

            string prefabName = Utils.GetPrefabName(gameObject);
            string objectName = gameObject.name.Replace("(Clone)", "").Trim();
            return prefabName.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                   objectName.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
                   prefabName.IndexOf(requested, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   objectName.IndexOf(requested, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static HitData BuildPlayerHit(Player player, Vector3 targetPosition, float damage)
        {
            Vector3 direction = targetPosition - player.transform.position;
            HitData hit = new()
            {
                m_hitType = HitData.HitType.PlayerHit,
                m_skill = Skills.SkillType.Clubs,
                m_point = targetPosition + Vector3.up,
                m_dir = direction.sqrMagnitude > 0.01f ? direction.normalized : player.transform.forward,
                m_pushForce = 2f,
                m_backstabBonus = 1f,
                m_staggerMultiplier = 1f,
                m_dodgeable = true,
                m_blockable = true,
            };
            hit.m_damage.m_blunt = damage;
            hit.SetAttacker(player);
            return hit;
        }

        public static void SetGuardianPower(string powerName, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            string power = powerName.Trim();
            if (string.IsNullOrWhiteSpace(power))
            {
                addOutput("ERROR: Guardian power name is empty");
                return;
            }

            int hash = power.GetStableHashCode();
            StatusEffect? statusEffect = ObjectDB.instance != null ? ObjectDB.instance.GetStatusEffect(hash) : null;
            if (statusEffect == null)
            {
                addOutput($"ERROR: Guardian power '{power}' was not found");
                return;
            }

            player.SetGuardianPower(power);
            player.m_guardianPowerCooldown = 0f;
            addOutput($"OK: Guardian power set to {power}");
        }

        public static void SetDeathlinkChoice(string choiceName, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            Assembly? deathlinkAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name.Equals("Deathlink", StringComparison.OrdinalIgnoreCase));
            if (deathlinkAssembly == null)
            {
                addOutput("ERROR: Deathlink assembly is not loaded");
                return;
            }

            Type? dataObjectsType = deathlinkAssembly.GetType("Deathlink.Common.DataObjects");
            Type? configDataType = deathlinkAssembly.GetType("Deathlink.Common.DeathConfigurationData");
            if (dataObjectsType == null || configDataType == null)
            {
                addOutput("ERROR: Deathlink configuration types were not found");
                return;
            }

            FieldInfo? levelsField = configDataType.GetField("DeathLevels", BindingFlags.Public | BindingFlags.Static);
            object? levelsValue = levelsField?.GetValue(null);
            string choice = choiceName.Trim();
            string? actualChoice = FindDictionaryKey(levelsValue, choice);
            if (actualChoice == null)
            {
                addOutput($"ERROR: Deathlink choice '{choiceName}' was not found");
                return;
            }

            string key = dataObjectsType.GetField("DeathChoiceKey", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string ?? "DL_DeathChoice";
            player.RemoveUniqueKeyValue(key);
            player.AddUniqueKeyValue(key, actualChoice);
            configDataType.GetMethod("CheckAndSetPlayerDeathConfig", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { player });

            addOutput($"OK: Deathlink choice set to {actualChoice}");
        }

        public static void Walk(float seconds, bool run, Action<string> addOutput)
        {
            if (seconds <= 0f)
            {
                addOutput("ERROR: Walk duration must be greater than zero");
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            if (valheimCLIPlugin.Instance == null)
            {
                addOutput("ERROR: valheimCLI plugin instance is not available");
                return;
            }

            valheimCLIPlugin.Instance.StartCoroutine(WalkRoutine(seconds, run));
            addOutput($"OK: Walking forward for {seconds:F1}s (run={run})");
        }

        public static void PrintPlayerState(Action<string> addOutput)
        {
            if (!TryBuildPlayerState(out string details, out _))
            {
                addOutput(details);
                return;
            }

            addOutput($"OK: {details}");
        }

        public static void PrintSleepState(Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            ZNetView nview = player.m_nview;
            if (nview == null || !nview.IsValid())
            {
                addOutput("ERROR: Local player has no valid ZNetView");
                return;
            }

            ZDO zdo = nview.GetZDO();
            bool inBed = zdo.GetBool(ZDOVars.s_inBed);
            string timeDetails = BuildTimeDetails();
            addOutput($"OK: inBed={inBed}, playerInBed={player.InBed()}, playerSleeping={player.IsSleeping()}, attached={player.IsAttached()}, zdo={zdo.m_uid}, owner={zdo.GetOwner()}, {timeDetails}");
        }

        public static void SetInBedFlag(bool inBed, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            ZNetView nview = player.m_nview;
            if (nview == null || !nview.IsValid())
            {
                addOutput("ERROR: Local player has no valid ZNetView");
                return;
            }

            ZDO zdo = nview.GetZDO();
            bool before = zdo.GetBool(ZDOVars.s_inBed);
            zdo.Set(ZDOVars.s_inBed, inBed);
            addOutput($"OK: inBed before={before} after={zdo.GetBool(ZDOVars.s_inBed)}, playerInBed={player.InBed()}, playerSleeping={player.IsSleeping()}, zdo={zdo.m_uid}, owner={zdo.GetOwner()}, {BuildTimeDetails()}");
        }

        public static void ResendDestroyedZdo(string zdoIdText, float delaySeconds, Action<string> addOutput)
        {
            if (!TryParseZdoId(zdoIdText, out ZDOID zdoId))
            {
                addOutput("ERROR: Invalid ZDO id. Expected user:id.");
                return;
            }

            if (ZDOMan.instance == null || ZNet.instance == null || ZRoutedRpc.instance == null)
            {
                addOutput("ERROR: ZDO/ZNet systems are not available");
                return;
            }

            ZNetPeer serverPeer = ZNet.instance.GetServerPeer();
            if (serverPeer == null || serverPeer.m_rpc == null)
            {
                addOutput("ERROR: Server peer RPC is not available");
                return;
            }

            ZDO zdo = ZDOMan.instance.GetZDO(zdoId);
            if (zdo == null)
            {
                addOutput($"ERROR: ZDO {zdoId} is not loaded locally");
                return;
            }

            if (!zdo.IsOwner())
            {
                addOutput($"ERROR: Local client is not owner of ZDO {zdoId}; owner={zdo.GetOwner()}");
                return;
            }

            ushort ownerRevision = (ushort)(zdo.OwnerRevision + 1);
            uint dataRevision = zdo.DataRevision + 1U;
            long ownerPeerId = ZDOMan.GetSessionID();
            Vector3 position = zdo.GetPosition();
            int prefabHash = zdo.GetPrefab();
            ZPackage itemPayload = new();
            itemPayload.Write((ushort)0);
            itemPayload.Write(prefabHash);
            byte[] payloadBytes = itemPayload.GetArray();

            ZPackage destroyPackage = new();
            destroyPackage.Write(1);
            destroyPackage.Write(zdoId);
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "DestroyZDO", destroyPackage);
            ZDOMan.instance.DestroyZDO(zdo);

            if (valheimCLIPlugin.Instance == null)
            {
                addOutput("ERROR: valheimCLI plugin instance is not available");
                return;
            }

            valheimCLIPlugin.Instance.StartCoroutine(ResendDestroyedZdoRoutine(
                serverPeer.m_rpc,
                zdoId,
                ownerRevision,
                dataRevision,
                ownerPeerId,
                position,
                payloadBytes,
                delaySeconds,
                addOutput));

            addOutput($"OK: queued destroyed ZDO resend zdo={zdoId}, prefabHash={prefabHash}, dataRevision={dataRevision}, delay={delaySeconds:F1}s");
        }

        private static IEnumerator ResendDestroyedZdoRoutine(
            ZRpc serverRpc,
            ZDOID zdoId,
            ushort ownerRevision,
            uint dataRevision,
            long ownerPeerId,
            Vector3 position,
            byte[] payloadBytes,
            float delaySeconds,
            Action<string> addOutput)
        {
            yield return new WaitForSeconds(delaySeconds);

            ZPackage zdoData = new();
            zdoData.Write(0);
            zdoData.Write(zdoId);
            zdoData.Write(ownerRevision);
            zdoData.Write(dataRevision);
            zdoData.Write(ownerPeerId);
            zdoData.Write(position);
            zdoData.Write(new ZPackage(payloadBytes));
            zdoData.Write(ZDOID.None);
            serverRpc.Invoke("ZDOData", zdoData);
            addOutput($"OK: resent destroyed ZDOData zdo={zdoId}, dataRevision={dataRevision}, ownerRevision={ownerRevision}");
        }

        public static void AttachBedAndSay(string message, bool direct, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            ZNetView nview = player.m_nview;
            if (nview == null || !nview.IsValid())
            {
                addOutput("ERROR: Local player has no valid ZNetView");
                return;
            }

            GameObject attachObject = new("valheimCLI_bed_attach_point");
            attachObject.transform.position = player.transform.position;
            attachObject.transform.rotation = player.transform.rotation;
            UnityEngine.Object.DontDestroyOnLoad(attachObject);

            player.AttachStart(
                attachObject.transform,
                null,
                hideWeapons: true,
                isBed: true,
                onShip: false,
                attachAnimation: "attach_bed",
                detachOffset: new Vector3(0f, 0.5f, 0f));

            if (direct)
            {
                nview.InvokeRPC(ZNetView.Everybody, "Say", (int)Talker.Type.Normal, UserInfo.GetLocalUser(), message);
            }
            else
            {
                Talker talker = player.GetComponent<Talker>();
                if (talker == null)
                {
                    addOutput("ERROR: Local player has no Talker component");
                    return;
                }

                talker.Say(Talker.Type.Normal, message);
            }

            ZDO zdo = nview.GetZDO();
            addOutput($"OK: bedSay message={message} direct={direct}, inBed={zdo.GetBool(ZDOVars.s_inBed)}, playerInBed={player.InBed()}, playerSleeping={player.IsSleeping()}, attached={player.IsAttached()}, zdo={zdo.m_uid}, owner={zdo.GetOwner()}, {BuildTimeDetails()}");
        }

        public static void SendForgedSayToZdo(ZDOID targetZdo, string message, Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            ZNetView nview = player.m_nview;
            if (nview == null || !nview.IsValid())
            {
                addOutput("ERROR: Local player has no valid ZNetView");
                return;
            }

            if (targetZdo.IsNone())
            {
                addOutput("ERROR: Target ZDO cannot be none");
                return;
            }

            if (ZRoutedRpc.instance == null)
            {
                addOutput("ERROR: ZRoutedRpc is not available");
                return;
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(
                ZRoutedRpc.Everybody,
                targetZdo,
                "Say",
                (int)Talker.Type.Normal,
                UserInfo.GetLocalUser(),
                message);

            ZDO senderZdo = nview.GetZDO();
            addOutput($"OK: forgedSay message={message}, targetZdo={targetZdo}, senderZdo={senderZdo.m_uid}, senderOwner={senderZdo.GetOwner()}");
        }

        private static bool TryParseZdoId(string value, out ZDOID zdoId)
        {
            zdoId = ZDOID.None;
            string[] parts = value.Split(':');
            if (parts.Length != 2)
            {
                return false;
            }

            if (!long.TryParse(parts[0], out long userId) || !uint.TryParse(parts[1], out uint id))
            {
                return false;
            }

            zdoId = new ZDOID(userId, id);
            return true;
        }

        private static string BuildTimeDetails()
        {
            if (ZNet.instance == null || EnvMan.instance == null)
            {
                return "time=unavailable";
            }

            double timeSeconds = ZNet.instance.GetTimeSeconds();
            return $"timeSeconds={timeSeconds:F2}, day={EnvMan.instance.GetDay(timeSeconds)}, dayFraction={EnvMan.instance.GetDayFraction():F3}, canSleep={EnvMan.CanSleep()}, isAfternoon={EnvMan.IsAfternoon()}, isNight={EnvMan.IsNight()}, isTimeSkipping={EnvMan.instance.IsTimeSkipping()}";
        }

        public static void ClearLocalInventory(Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            Inventory inventory = player.GetInventory();
            if (inventory == null)
            {
                addOutput("ERROR: Local player inventory is not available");
                return;
            }

            int before = inventory.NrOfItems();
            int extraBefore = CountExtraSlotsItems();

            player.UnequipAllItems();
            inventory.RemoveAll();
            player.ClearFood();

            int after = inventory.NrOfItems();
            int extraAfter = CountExtraSlotsItems();
            addOutput($"OK: cleared inventory before={before} after={after} extraSlotsBefore={extraBefore} extraSlotsAfter={extraAfter}");
        }

        public static void CheckBirdDropSignal(Action<string> addOutput)
        {
            if (!TryBuildPlayerState(out string details, out PlayerStateSnapshot snapshot))
            {
                addOutput(details);
                return;
            }

            bool signal = snapshot.ValkyrieActive || snapshot.PlayerInIntro || snapshot.PlayerAttached || snapshot.PlayerDead || snapshot.HeightAboveGround > 50f;
            addOutput(signal
                ? $"OK: Bird drop signal detected ({details})"
                : $"ERROR: Bird drop signal not detected ({details})");
        }

        private static int CountExtraSlotsItems()
        {
            Type? apiType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("ExtraSlots.API", throwOnError: false))
                .FirstOrDefault(type => type != null);
            MethodInfo? method = apiType?.GetMethod("GetAllExtraSlotsItems", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                return -1;
            }

            try
            {
                object? result = method.Invoke(null, Array.Empty<object>());
                if (result is not IEnumerable enumerable)
                {
                    return -1;
                }

                int count = 0;
                foreach (object value in enumerable)
                {
                    if (value is ItemDrop.ItemData)
                    {
                        count++;
                    }
                }

                return count;
            }
            catch
            {
                return -1;
            }
        }

        private static void CheckIntroComplete(Action<string> addOutput)
        {
            Game game = Game.instance;
            Player player = Player.m_localPlayer;
            if (game == null)
            {
                addOutput("ERROR: Game instance is not available");
                return;
            }

            PlayerProfile profile = game.GetPlayerProfile();
            if (profile == null)
            {
                addOutput("ERROR: Player profile is not available");
                return;
            }

            if (player == null)
            {
                addOutput("ERROR: Local player is not available");
                return;
            }

            bool profileFirstSpawn = profile.m_firstSpawn;
            bool gameFirstSpawn = GetBoolField(GameFirstSpawnField, game, fallback: false);
            bool gameInIntro = GetBoolField(GameInIntroField, game, fallback: false);
            bool requestRespawn = GetBoolField(GameRequestRespawnField, game, fallback: game.WaitingForRespawn());
            bool playerInIntro = player.InIntro();
            bool playerDead = player.IsDead();
            bool playerAttached = player.IsAttached();
            float health = player.GetHealth();
            Vector3 position = player.transform.position;

            string details = $"profileFirstSpawn={profileFirstSpawn}, gameFirstSpawn={gameFirstSpawn}, gameInIntro={gameInIntro}, requestRespawn={requestRespawn}, playerInIntro={playerInIntro}, playerDead={playerDead}, playerAttached={playerAttached}, health={health:F1}, position={position.x:F1},{position.y:F1},{position.z:F1}";
            bool introCleared = !profileFirstSpawn && !gameFirstSpawn && !gameInIntro && !requestRespawn && !playerInIntro && !playerDead && !playerAttached && health > 0f;

            addOutput(introCleared
                ? $"OK: Intro complete ({details})"
                : $"ERROR: Intro state not cleared ({details})");
        }

        public static void StartDedicatedServerJoin(string host, int port, Action<string> addOutput)
        {
            FejdStartup fejd = FejdStartup.instance;
            if (fejd == null)
            {
                addOutput("ERROR: Main menu is not available");
                return;
            }

            if (!TrySelectCurrentProfile(fejd, out string profileDescription, out string profileError))
            {
                addOutput(profileError);
                return;
            }

            ServerJoinDataDedicated dedicated = new ServerJoinDataDedicated(host, (ushort)port);
            ServerJoinData joinData = new ServerJoinData(dedicated);

            fejd.SetServerToJoin(joinData);
            fejd.JoinServer();
            addOutput($"OK: Dedicated server join started for {dedicated} using {profileDescription}");
        }

        public static void StartLocalWorld(string worldName, Action<string> addOutput)
        {
            if (worldName.Length < 3)
            {
                addOutput("ERROR: World name must be at least 3 characters");
                return;
            }

            if (worldName.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                addOutput("ERROR: World name contains invalid filename characters");
                return;
            }

            FejdStartup fejd = FejdStartup.instance;
            if (fejd == null)
            {
                addOutput("ERROR: Main menu is not available");
                return;
            }

            if (!TrySelectCurrentProfile(fejd, out string profileDescription, out string profileError))
            {
                addOutput(profileError);
                return;
            }

            World world = World.GetCreateWorld(worldName, FileHelpers.FileSource.Local);
            world.m_fileSource = FileHelpers.FileSource.Local;
            WorldField?.SetValue(fejd, world);
            SaveSystem.InvalidateCache();

            addOutput($"OK: Starting local world '{world.m_name}' using {profileDescription}");
            fejd.OnWorldStart();
        }

        public static void LogoutSave(Action<string> addOutput)
        {
            Game game = Game.instance;
            if (game == null)
            {
                addOutput("ERROR: Game instance is not available");
                return;
            }

            if (game.IsShuttingDown())
            {
                addOutput("OK: Logout is already in progress");
                return;
            }

            addOutput("OK: Logging out with save");
            game.Logout(save: true, changeToStartScene: true);
        }

        private static bool TrySelectCurrentProfile(FejdStartup fejd, out string profileDescription, out string error)
        {
            profileDescription = "";
            error = "";

            List<PlayerProfile>? profiles = ProfilesField?.GetValue(fejd) as List<PlayerProfile>;
            if (profiles == null || profiles.Count == 0)
            {
                profiles = SaveSystem.GetAllPlayerProfiles();
                ProfilesField?.SetValue(fejd, profiles);
            }

            if (profiles == null || profiles.Count == 0)
            {
                error = "ERROR: No local character profile is available";
                return false;
            }

            int profileIndex = 0;
            object? rawIndex = ProfileIndexField?.GetValue(fejd);
            if (rawIndex is int index && index >= 0 && index < profiles.Count)
            {
                profileIndex = index;
            }
            else
            {
                string selectedFilename = PlatformPrefs.GetString("profile", "");
                if (!string.IsNullOrWhiteSpace(selectedFilename))
                {
                    int selectedIndex = profiles.FindIndex(profile => profile.GetFilename() == selectedFilename);
                    if (selectedIndex >= 0)
                    {
                        profileIndex = selectedIndex;
                    }
                }
            }

            PlayerProfile profile = profiles[profileIndex];
            PlatformPrefs.SetString("profile", profile.GetFilename());
            Game.SetProfile(profile.GetFilename(), profile.m_fileSource);
            profileDescription = $"character '{profile.GetName()}' ({profile.GetFilename()}, {profile.m_fileSource})";
            return true;
        }

        private static void PrintConnectionStatus(Action<string> addOutput)
        {
            ZNet.ConnectionStatus status = ZNet.GetConnectionStatus();
            string server = ZNet.GetServerString();
            GameState state = DetectCurrentState();
            addOutput($"OK: connectionStatus={status}, gameState={GameStateTracker.StateToString(state)}, server={server}");
        }

        private static GameState DetectCurrentState()
        {
            if (FejdStartup.instance != null && Game.instance == null)
            {
                return GameState.MainMenu;
            }

            if (Game.instance != null)
            {
                if (ZNet.instance != null && ZNet.instance.InConnectingScreen())
                {
                    return GameState.Loading;
                }

                return Player.m_localPlayer != null ? GameState.InWorld : GameState.InWorldNoPlayer;
            }

            if (ZNet.instance != null && ZNet.instance.InConnectingScreen())
            {
                return GameState.Loading;
            }

            return GameState.Unknown;
        }

        public static bool TryParseHostPort(string address, out string host, out int port)
        {
            const int defaultPort = 2456;
            host = address.Trim();
            port = defaultPort;

            int separator = host.LastIndexOf(':');
            if (separator >= 0)
            {
                string portPart = host.Substring(separator + 1);
                if (!int.TryParse(portPart, out port) || port <= 0 || port > 65535)
                {
                    host = "";
                    return false;
                }

                host = host.Substring(0, separator);
            }

            return !string.IsNullOrWhiteSpace(host);
        }

        private static bool GetBoolField(FieldInfo? field, object instance, bool fallback)
        {
            if (field == null)
            {
                return fallback;
            }

            object? value = field.GetValue(instance);
            return value is bool boolValue ? boolValue : fallback;
        }

        private static bool PickableMatches(Pickable pickable, string requested)
        {
            return NameMatches(requested, pickable.name, GetPickableName(pickable), pickable.m_itemPrefab != null ? pickable.m_itemPrefab.name : "");
        }

        private static bool PickableItemMatches(PickableItem pickableItem, string requested)
        {
            return NameMatches(requested, pickableItem.name, GetPickableItemName(pickableItem));
        }

        private static bool CharacterMatches(Character character, string requested)
        {
            return NameMatches(requested, character.name, GetCharacterName(character));
        }

        private static string GetCharacterName(Character character)
        {
            try
            {
                return character.GetHoverName();
            }
            catch
            {
                return character.name;
            }
        }

        private static string GetPickableName(Pickable pickable)
        {
            try
            {
                return pickable.GetHoverName();
            }
            catch
            {
                return pickable.m_itemPrefab != null ? pickable.m_itemPrefab.name : pickable.name;
            }
        }

        private static string GetPickableItemName(PickableItem pickableItem)
        {
            try
            {
                return pickableItem.GetHoverName();
            }
            catch
            {
                return pickableItem.name;
            }
        }

        private static bool NameMatches(string requested, params string[] candidates)
        {
            string needle = NormalizeName(requested);
            return candidates.Any(candidate =>
            {
                string normalized = NormalizeName(candidate);
                return normalized.Contains(needle) || needle.Contains(normalized);
            });
        }

        private static string NormalizeName(string value)
        {
            return value
                .ToLowerInvariant()
                .Replace("(clone)", "")
                .Replace("$item_", "")
                .Replace("pickable_", "")
                .Replace("_", "")
                .Replace(" ", "")
                .Trim()
                ;
        }

        private static string? FindDictionaryKey(object? dictionary, string requested)
        {
            if (dictionary is not IDictionary entries)
            {
                return null;
            }

            foreach (object key in entries.Keys)
            {
                string? text = key?.ToString();
                if (text != null && text.Equals(requested, StringComparison.OrdinalIgnoreCase))
                {
                    return text;
                }
            }

            return null;
        }

        private static string ResolvePrefabName(string requestedName, ZNetScene znetScene)
        {
            GameObject exact = znetScene.GetPrefab(requestedName);
            if (exact != null)
            {
                return requestedName;
            }

            foreach (string prefabName in znetScene.GetPrefabNames())
            {
                if (prefabName.Equals(requestedName, StringComparison.OrdinalIgnoreCase))
                {
                    return prefabName;
                }
            }

            return requestedName;
        }

        private static void FreezeCharacter(GameObject gameObject, Vector3 position, Quaternion rotation)
        {
            Character character = gameObject.GetComponent<Character>();
            if (character != null)
            {
                character.SetMoveDir(Vector3.zero);
            }

            BaseAI baseAI = gameObject.GetComponent<BaseAI>();
            if (baseAI != null)
            {
                baseAI.SetHuntPlayer(false);
                baseAI.StopMoving();
                baseAI.enabled = false;
            }

            ZNetView nview = gameObject.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                ZDO zdo = nview.GetZDO();
                zdo.Set(ZDOVars.s_huntPlayer, false);
                zdo.Set(ZDOVars.s_alert, false);
            }

            FrozenCharacterAnchor anchor = gameObject.GetComponent<FrozenCharacterAnchor>();
            if (anchor == null)
            {
                anchor = gameObject.AddComponent<FrozenCharacterAnchor>();
            }

            anchor.Initialize(position, rotation);
        }

        private static IEnumerator FireCurrentWeaponRoutine(float holdSeconds, float waitLoadedSeconds)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                yield break;
            }

            ItemDrop.ItemData weapon = player.GetCurrentWeapon();
            if (weapon == null)
            {
                yield break;
            }

            float loadTimeout = Time.time + waitLoadedSeconds;
            while (weapon.m_shared.m_attack.m_requiresReload && !player.IsWeaponLoaded() && Time.time < loadTimeout)
            {
                player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
                yield return new WaitForFixedUpdate();

                if (player.GetCurrentWeapon() != weapon)
                {
                    yield break;
                }
            }

            if (weapon.m_shared.m_attack.m_requiresReload && !player.IsWeaponLoaded())
            {
                yield break;
            }

            player.AttackTowardsPlayerLookDir = true;
            float releaseTime = Time.time + holdSeconds;
            bool firstFrame = true;
            while (Time.time < releaseTime)
            {
                player.SetControls(Vector3.zero, firstFrame, true, false, false, false, false, false, false, false, false);
                firstFrame = false;
                yield return new WaitForFixedUpdate();
            }

            player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
        }

        private static IEnumerator WalkRoutine(float seconds, bool run)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                Player player = Player.m_localPlayer;
                if (player == null)
                {
                    yield break;
                }

                player.SetControls(Vector3.forward, false, false, false, false, false, false, false, false, run, false);
                yield return null;
            }

            Player finalPlayer = Player.m_localPlayer;
            if (finalPlayer != null)
            {
                finalPlayer.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
            }
        }

        private static bool TryBuildPlayerState(out string details, out PlayerStateSnapshot snapshot)
        {
            snapshot = default;
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                details = "ERROR: No local player found";
                return false;
            }

            Vector3 position = player.transform.position;
            float groundHeight = 0f;
            bool hasGround = ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(position, out groundHeight);
            float heightAboveGround = hasGround ? position.y - groundHeight : 0f;
            string deathlinkChoice = "";
            player.TryGetUniqueKeyValue("DL_DeathChoice", out deathlinkChoice);

            snapshot = new PlayerStateSnapshot
            {
                PlayerInIntro = player.InIntro(),
                PlayerAttached = player.IsAttached(),
                PlayerDead = player.IsDead(),
                ValkyrieActive = Valkyrie.m_instance != null,
                HeightAboveGround = heightAboveGround
            };

            details = $"position={position.x:F1},{position.y:F1},{position.z:F1}, heightAboveGround={heightAboveGround:F1}, health={player.GetHealth():F1}, playerInIntro={snapshot.PlayerInIntro}, playerAttached={snapshot.PlayerAttached}, playerDead={snapshot.PlayerDead}, valkyrieActive={snapshot.ValkyrieActive}, guardianPower={player.GetGuardianPowerName()}, deathlinkChoice={deathlinkChoice}";
            return true;
        }

        internal static void SetDebugTimeOfDay(float dayFraction, Action<string> addOutput)
        {
            if (EnvMan.instance == null)
            {
                addOutput("ERROR: EnvMan is not ready");
                return;
            }

            if (dayFraction < 0f)
            {
                EnvMan.instance.m_debugTimeOfDay = false;
                addOutput("OK: local debug time of day reset");
                return;
            }

            EnvMan.instance.m_debugTimeOfDay = true;
            EnvMan.instance.m_debugTime = Mathf.Clamp01(dayFraction);
            addOutput($"OK: local debug time of day set to {EnvMan.instance.m_debugTime:F3}");
        }

        internal static void SetDebugEnvironment(string environmentName, Action<string> addOutput)
        {
            if (EnvMan.instance == null)
            {
                addOutput("ERROR: EnvMan is not ready");
                return;
            }

            if (environmentName.Equals("reset", StringComparison.OrdinalIgnoreCase) ||
                environmentName.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                environmentName.Equals("clearenv", StringComparison.OrdinalIgnoreCase))
            {
                EnvMan.instance.m_debugEnv = "";
                addOutput("OK: local debug environment reset");
                return;
            }

            EnvSetup environment = EnvMan.instance.GetEnv(environmentName);
            if (environment == null)
            {
                addOutput($"ERROR: Environment not found: {environmentName}");
                return;
            }

            EnvMan.instance.m_debugEnv = environment.m_name;
            addOutput($"OK: local debug environment set to {environment.m_name}");
        }

        private struct PlayerStateSnapshot
        {
            public bool PlayerInIntro;
            public bool PlayerAttached;
            public bool PlayerDead;
            public bool ValkyrieActive;
            public float HeightAboveGround;
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

        private sealed class FrozenCharacterAnchor : MonoBehaviour
        {
            private Vector3 _position;
            private Quaternion _rotation;
            private Character? _character;
            private BaseAI? _baseAI;
            private Rigidbody? _body;
            private RigidbodyConstraints _originalConstraints;
            private bool _initialized;

            public void Initialize(Vector3 position, Quaternion rotation)
            {
                _position = position;
                _rotation = rotation;
                _character = GetComponent<Character>();
                _baseAI = GetComponent<BaseAI>();
                _body = GetComponent<Rigidbody>();
                if (_body != null)
                {
                    _originalConstraints = _body.constraints;
                    _body.linearVelocity = Vector3.zero;
                    _body.angularVelocity = Vector3.zero;
                    _body.constraints = RigidbodyConstraints.FreezeAll;
                }

                _initialized = true;
                ApplyFrozenState();
            }

            private void LateUpdate()
            {
                if (!_initialized)
                {
                    return;
                }

                if (_character == null || _character.IsDead())
                {
                    RestoreBody();
                    Destroy(this);
                    return;
                }

                ApplyFrozenState();
            }

            private void ApplyFrozenState()
            {
                if (_baseAI != null)
                {
                    _baseAI.SetHuntPlayer(false);
                    _baseAI.StopMoving();
                    _baseAI.enabled = false;
                }

                if (_character != null)
                {
                    _character.SetMoveDir(Vector3.zero);
                }

                if (_body != null)
                {
                    _body.linearVelocity = Vector3.zero;
                    _body.angularVelocity = Vector3.zero;
                }

                transform.SetPositionAndRotation(_position, _rotation);
            }

            private void RestoreBody()
            {
                if (_body == null)
                {
                    return;
                }

                _body.constraints = _originalConstraints;
            }
        }
    }
}
