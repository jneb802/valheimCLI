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

            valheimCLIPlugin.Log.LogInfo("Custom CLI commands registered");
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
    }
}
