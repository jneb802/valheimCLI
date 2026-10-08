using System;
namespace valheimCLI
{
    internal static class StandardDispatch
    {
        internal static bool Execute(string command, Action<string> output)
        {
            string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                return false;
            }

            if (parts[0].Equals("cli_create_character", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    output("Usage: cli_create_character <name> [--replace] [--local] [--skip-intro]");
                    return true;
                }

                bool replace = false;
                bool forceLocal = false;
                bool skipIntro = false;
                for (int i = 2; i < parts.Length; i++)
                {
                    replace |= parts[i].Equals("--replace", StringComparison.OrdinalIgnoreCase);
                    forceLocal |= parts[i].Equals("--local", StringComparison.OrdinalIgnoreCase);
                    skipIntro |= parts[i].Equals("--skip-intro", StringComparison.OrdinalIgnoreCase);
                }

                forceLocal |= replace;
                CustomCommands.CreateCharacter(parts[1], replace, forceLocal, skipIntro, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_select_character", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    output("Usage: cli_select_character <name-or-filename>");
                    return true;
                }

                CustomCommands.SelectCharacter(parts[1], line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_connect_direct", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    output("Usage: cli_connect_direct <host[:port]> [password]");
                    return true;
                }

                if (!CustomCommands.TryParseHostPort(parts[1], out string host, out int port))
                {
                    output($"ERROR: Invalid server address '{parts[1]}'");
                    return true;
                }

                if (FejdStartup.instance == null)
                {
                    StandardSession.QueueServerConnect(parts[1], parts.Length >= 3 ? parts[2] : null);
                    output($"OK: Queued dedicated server join for {parts[1]}");
                    return true;
                }

                if (parts.Length >= 3)
                {
                    StandardSession.SetServerPassword(parts[2]);
                }

                CustomCommands.StartDedicatedServerJoin(host, port, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_connect_steam_user", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2 || !ulong.TryParse(parts[1], out ulong steamId))
                {
                    output("Usage: cli_connect_steam_user <steamId> [password]");
                    return true;
                }

                CustomCommands.StartSteamUserJoin(steamId, parts.Length >= 3 ? parts[2] : null, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_connect_playfab_user", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                {
                    output("Usage: cli_connect_playfab_user <remotePlayerId> [password]");
                    return true;
                }

                CustomCommands.StartPlayFabUserJoin(parts[1], parts.Length >= 3 ? parts[2] : null, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_connection_status", StringComparison.OrdinalIgnoreCase))
            {
                output($"OK: connectionStatus={ZNet.GetConnectionStatus()}, server={ZNet.GetServerString()}");
                return true;
            }

            if (parts[0].Equals("cli_multiplayer_identity", StringComparison.OrdinalIgnoreCase))
            {
                CustomCommands.PrintMultiplayerIdentity(line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_start_host_world", StringComparison.OrdinalIgnoreCase))
            {
                if (!CustomCommands.TryParseHostedWorldOptions(parts, 1, out string worldName, out bool publicServer, out bool crossplay, out string? password, out string error))
                {
                    output(error);
                    return true;
                }

                CustomCommands.StartHostedWorld(worldName, publicServer, crossplay, password, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_set_tod", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2 || !float.TryParse(parts[1], out float dayFraction))
                {
                    output("Usage: cli_set_tod <0-1|-1>");
                    return true;
                }

                CustomCommands.SetDebugTimeOfDay(dayFraction, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_set_env", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    output("Usage: cli_set_env <env|reset>");
                    return true;
                }

                CustomCommands.SetDebugEnvironment(parts[1], line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_goto_location", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    output("Usage: cli_goto_location <location_prefab_name_or_group>");
                    return true;
                }

                CustomCommands.GotoLocation(parts[1], line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_find_locations", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    output("Usage: cli_find_locations <text> [limit]");
                    return true;
                }

                int limit = 20;
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[2], out limit);
                }

                CustomCommands.FindLocations(parts[1], Math.Max(1, limit), line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_logout_save", StringComparison.OrdinalIgnoreCase))
            {
                CustomCommands.LogoutSave(line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_spawn_near", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    output("Usage: cli_spawn_near <prefab> [count] [level] [radius]");
                    return true;
                }

                int count = 1;
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[2], out count);
                }

                int level = 1;
                if (parts.Length >= 4)
                {
                    int.TryParse(parts[3], out level);
                }

                float radius = 3f;
                if (parts.Length >= 5)
                {
                    float.TryParse(parts[4], out radius);
                }

                CustomCommands.SpawnNear(parts[1], count, level, radius, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_spawn_at", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 5)
                {
                    output("Usage: cli_spawn_at <prefab> <x> <y> <z> [count] [level] [radius]");
                    return true;
                }

                if (!float.TryParse(parts[2], out float x) || !float.TryParse(parts[3], out float y) || !float.TryParse(parts[4], out float z))
                {
                    output("ERROR: Invalid coordinates");
                    return true;
                }

                int count = 1;
                if (parts.Length >= 6)
                {
                    int.TryParse(parts[5], out count);
                }

                int level = 1;
                if (parts.Length >= 7)
                {
                    int.TryParse(parts[6], out level);
                }

                float radius = 3f;
                if (parts.Length >= 8)
                {
                    float.TryParse(parts[7], out radius);
                }

                CustomCommands.SpawnAt(parts[1], x, y, z, count, level, radius, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_spawn_frozen", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    output("Usage: cli_spawn_frozen <prefab> [count] [level] [distance] [spacing]");
                    return true;
                }

                int count = 1;
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[2], out count);
                }

                int level = 1;
                if (parts.Length >= 4)
                {
                    int.TryParse(parts[3], out level);
                }

                float distance = 12f;
                if (parts.Length >= 5)
                {
                    float.TryParse(parts[4], out distance);
                }

                float spacing = 3f;
                if (parts.Length >= 6)
                {
                    float.TryParse(parts[5], out spacing);
                }

                CustomCommands.SpawnFrozenNear(parts[1], count, level, distance, spacing, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_destroy_nearby_characters", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    output("Usage: cli_destroy_nearby_characters <name|*> [radius]");
                    return true;
                }

                float radius = 40f;
                if (parts.Length >= 3)
                {
                    float.TryParse(parts[2], out radius);
                }

                CustomCommands.DestroyNearbyCharacters(parts[1], radius, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_set_nearby_character_health", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 3 || !float.TryParse(parts[2], out float health))
                {
                    output("Usage: cli_set_nearby_character_health <name|*> <health> [radius]");
                    return true;
                }

                float radius = 40f;
                if (parts.Length >= 4)
                {
                    float.TryParse(parts[3], out radius);
                }

                CustomCommands.SetNearbyCharacterHealth(parts[1], health, radius, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_freeze_nearest_character", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    output("Usage: cli_freeze_nearest_character <name> [radius]");
                    return true;
                }

                float radius = 30f;
                if (parts.Length >= 3)
                {
                    float.TryParse(parts[2], out radius);
                }

                CustomCommands.FreezeNearestCharacter(parts[1], radius, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_aim_at", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 4 ||
                    !float.TryParse(parts[1], out float aimX) ||
                    !float.TryParse(parts[2], out float aimY) ||
                    !float.TryParse(parts[3], out float aimZ))
                {
                    output("Usage: cli_aim_at <x> <y> <z>");
                    return true;
                }

                CustomCommands.AimAtPoint(new UnityEngine.Vector3(aimX, aimY, aimZ), line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_aim_at_nearest_character", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    output("Usage: cli_aim_at_nearest_character <name> [radius] [heightOffset]");
                    return true;
                }

                float radius = 50f;
                if (parts.Length >= 3)
                {
                    float.TryParse(parts[2], out radius);
                }

                float heightOffset = 0.8f;
                if (parts.Length >= 4)
                {
                    float.TryParse(parts[3], out heightOffset);
                }

                CustomCommands.AimAtNearestCharacter(parts[1], radius, heightOffset, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_fire_current_weapon", StringComparison.OrdinalIgnoreCase))
            {
                float holdSeconds = 0.15f;
                if (parts.Length >= 2)
                {
                    float.TryParse(parts[1], out holdSeconds);
                }

                float waitLoadedSeconds = 4f;
                if (parts.Length >= 3)
                {
                    float.TryParse(parts[2], out waitLoadedSeconds);
                }

                CustomCommands.FireCurrentWeapon(holdSeconds, waitLoadedSeconds, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_weapon_state", StringComparison.OrdinalIgnoreCase))
            {
                CustomCommands.PrintWeaponState(line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_set_player_safety", StringComparison.OrdinalIgnoreCase))
            {
                if (!PlayerModes.TryParseSafety(parts, out bool enabled, out bool targetable, out string error))
                {
                    output(error);
                    return true;
                }

                CustomCommands.SetPlayerSafety(enabled, line => output(line), targetable);
                return true;
            }

            if (parts[0].Equals("cli_give_item", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    output("Usage: cli_give_item <prefab> [count] [quality]");
                    return true;
                }

                int count = 1;
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[2], out count);
                }

                int quality = 1;
                if (parts.Length >= 4)
                {
                    int.TryParse(parts[3], out quality);
                }

                CustomCommands.GiveItem(parts[1], Math.Max(1, count), Math.Max(1, Math.Min(4, quality)), line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_equip_item", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    output("Usage: cli_equip_item <prefab-or-display-name>");
                    return true;
                }

                CustomCommands.EquipInventoryItem(parts[1], line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_apply_magic_effect", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 3)
                {
                    output("Usage: cli_apply_magic_effect <item> <effect> [rarity] [value]");
                    return true;
                }

                string rarity = parts.Length >= 4 ? parts[3] : "Magic";
                float value = 1f;
                if (parts.Length >= 5)
                {
                    float.TryParse(parts[4], out value);
                }

                CustomCommands.ApplyMagicEffect(parts[1], parts[2], rarity, value, line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_setup_reload_on_kill_clip", StringComparison.OrdinalIgnoreCase))
            {
                string crossbow = parts.Length >= 2 ? parts[1] : "CrossbowArbalest";
                string bolt = parts.Length >= 3 ? parts[2] : "BoltCarapace";
                int boltCount = 100;
                if (parts.Length >= 4)
                {
                    int.TryParse(parts[3], out boltCount);
                }

                CustomCommands.SetupReloadOnKillClip(crossbow, bolt, Math.Max(1, boltCount), line => output(line));
                return true;
            }

            if (parts[0].Equals("cli_zdo_resend_destroyed", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    output("Usage: cli_zdo_resend_destroyed <zdoId> [delaySeconds]");
                    return true;
                }

                float delaySeconds = 3f;
                if (parts.Length >= 3)
                {
                    float.TryParse(parts[2], out delaySeconds);
                }

                CustomCommands.ResendDestroyedZdo(parts[1], Math.Max(0.5f, delaySeconds), line => output(line));
                return true;
            }

            if (!parts[0].Equals("cli_connect", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (parts.Length < 2)
            {
                output("Usage: cli_connect <host:port> [password]");
                return true;
            }

            StandardSession.QueueServerConnect(parts[1], parts.Length >= 3 ? parts[2] : null);
            output($"OK: Queued server join for {parts[1]}");
            return true;
        }

    }
}
