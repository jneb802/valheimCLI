using System;
using System.Collections.Generic;
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
        private static readonly MethodInfo? SetSelectedProfileMethod = typeof(FejdStartup).GetMethod("SetSelectedProfile", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? GameFirstSpawnField = typeof(Game).GetField("m_firstSpawn", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? GameInIntroField = typeof(Game).GetField("m_inIntro", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo? GameRequestRespawnField = typeof(Game).GetField("m_requestRespawn", BindingFlags.Instance | BindingFlags.NonPublic);

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

                PlayerProfile.RemoveProfile(filename, FileHelpers.FileSource.Local);
                PlayerProfile.RemoveProfile(filename, FileHelpers.FileSource.Cloud);
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
