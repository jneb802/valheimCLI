using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace valheimCLI
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class valheimCLIPlugin : BaseUnityPlugin
    {
        private const string ModName = "valheimCLI";
        private const string ModVersion = "1.0.0";
        private const string Author = "valheimCLI";
        private const string ModGUID = Author + "." + ModName;
        private static string ConfigFileName = ModGUID + ".cfg";
        private static string ConfigFileFullPath = BepInEx.Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;

        private readonly Harmony HarmonyInstance = new(ModGUID);
        public static readonly ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource(ModName);

        private CommandServer? _commandServer;
        private GameStateTracker? _stateTracker;
        private ConfigEntry<int>? _portConfig;
        private ConfigEntry<bool>? _enabledConfig;
        private ConfigEntry<bool>? _autoStartQueuedJoinConfig;

        private readonly List<string> _capturedOutput = new();
        private bool _capturingOutput;
        private bool _autoStartQueuedJoinAttempted;
        private string? _pendingConnectAddress;
        private string? _pendingConnectPassword;
        private static bool _autoStartQueuedJoinRequested;
        private static readonly FieldInfo? QueuedJoinServerField = typeof(FejdStartup).GetField("m_queuedJoinServer", BindingFlags.Instance | BindingFlags.NonPublic);

        public void Awake()
        {
            Instance = this;

            _enabledConfig = Config.Bind("Server", "Enabled", true, "Enable the command server");
            _portConfig = Config.Bind("Server", "Port", 5555, "Port for the command server (localhost only)");
            _autoStartQueuedJoinConfig = Config.Bind("ClientLaunch", "AutoStartQueuedJoin", true, "Automatically start the selected character when Valheim has a queued startup/server join.");
            if (HasStartupJoinArgument())
            {
                RequestAutoStartQueuedJoin();
            }

            Assembly assembly = Assembly.GetExecutingAssembly();
            HarmonyInstance.PatchAll(assembly);

            CustomCommands.Register();

            // Initialize state tracker
            _stateTracker = new GameStateTracker(Log);

            if (_enabledConfig.Value)
            {
                _commandServer = new CommandServer(Log, _portConfig.Value);
                _commandServer.SetStateTracker(_stateTracker);
                _commandServer.Start();
            }

            SetupWatcher();
            Log.LogInfo($"{ModName} loaded. CLI server on port {_portConfig.Value}");
        }

        private void Update()
        {
            _stateTracker?.Update();
            ProcessPendingCommands();
            TryQueuePendingServerConnect();
            TryAutoStartQueuedJoin();
        }

        private void ProcessPendingCommands()
        {
            if (_commandServer == null) return;

            while (_commandServer.TryGetPendingCommand(out var command) && command != null)
            {
                if (string.IsNullOrEmpty(command)) continue;

                try
                {
                    Log.LogInfo($"Executing CLI command: {command}");
                    if (!TryExecuteBuiltInCommand(command))
                    {
                        ExecuteCommand(command);
                    }
                }
                catch (Exception ex)
                {
                    _commandServer.SendOutput($"Error: {ex.Message}");
                    Log.LogError($"Command execution error: {ex}");
                }
            }
        }

        private bool TryExecuteBuiltInCommand(string command)
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
                    _commandServer?.SendOutput("Usage: cli_create_character <name> [--replace] [--local]");
                    return true;
                }

                bool replace = false;
                bool forceLocal = false;
                for (int i = 2; i < parts.Length; i++)
                {
                    replace |= parts[i].Equals("--replace", StringComparison.OrdinalIgnoreCase);
                    forceLocal |= parts[i].Equals("--local", StringComparison.OrdinalIgnoreCase);
                }

                forceLocal |= replace;
                CustomCommands.CreateCharacter(parts[1], replace, forceLocal, line => _commandServer?.SendOutput(line));
                return true;
            }

            if (parts[0].Equals("cli_select_character", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    _commandServer?.SendOutput("Usage: cli_select_character <name-or-filename>");
                    return true;
                }

                CustomCommands.SelectCharacter(parts[1], line => _commandServer?.SendOutput(line));
                return true;
            }

            if (parts[0].Equals("cli_connect_direct", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    _commandServer?.SendOutput("Usage: cli_connect_direct <host[:port]> [password]");
                    return true;
                }

                if (!CustomCommands.TryParseHostPort(parts[1], out string host, out int port))
                {
                    _commandServer?.SendOutput($"ERROR: Invalid server address '{parts[1]}'");
                    return true;
                }

                if (FejdStartup.instance == null)
                {
                    QueueServerConnect(parts[1], parts.Length >= 3 ? parts[2] : null);
                    _commandServer?.SendOutput($"OK: Queued dedicated server join for {parts[1]}");
                    return true;
                }

                if (parts.Length >= 3)
                {
                    SetServerPassword(parts[2]);
                }

                CustomCommands.StartDedicatedServerJoin(host, port, line => _commandServer?.SendOutput(line));
                return true;
            }

            if (parts[0].Equals("cli_connection_status", StringComparison.OrdinalIgnoreCase))
            {
                _commandServer?.SendOutput($"OK: connectionStatus={ZNet.GetConnectionStatus()}, server={ZNet.GetServerString()}");
                return true;
            }

            if (parts[0].Equals("cli_goto_location", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    _commandServer?.SendOutput("Usage: cli_goto_location <location_prefab_name_or_group>");
                    return true;
                }

                CustomCommands.GotoLocation(parts[1], line => _commandServer?.SendOutput(line));
                return true;
            }

            if (parts[0].Equals("cli_find_locations", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    _commandServer?.SendOutput("Usage: cli_find_locations <text> [limit]");
                    return true;
                }

                int limit = 20;
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[2], out limit);
                }

                CustomCommands.FindLocations(parts[1], Math.Max(1, limit), line => _commandServer?.SendOutput(line));
                return true;
            }

            if (parts[0].Equals("cli_logout_save", StringComparison.OrdinalIgnoreCase))
            {
                CustomCommands.LogoutSave(line => _commandServer?.SendOutput(line));
                return true;
            }

            if (parts[0].Equals("cli_spawn_near", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    _commandServer?.SendOutput("Usage: cli_spawn_near <prefab> [count] [level] [radius]");
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

                CustomCommands.SpawnNear(parts[1], count, level, radius, line => _commandServer?.SendOutput(line));
                return true;
            }

            if (parts[0].Equals("cli_spawn_at", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 5)
                {
                    _commandServer?.SendOutput("Usage: cli_spawn_at <prefab> <x> <y> <z> [count] [level] [radius]");
                    return true;
                }

                if (!float.TryParse(parts[2], out float x) || !float.TryParse(parts[3], out float y) || !float.TryParse(parts[4], out float z))
                {
                    _commandServer?.SendOutput("ERROR: Invalid coordinates");
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

                CustomCommands.SpawnAt(parts[1], x, y, z, count, level, radius, line => _commandServer?.SendOutput(line));
                return true;
            }

            if (parts[0].Equals("cli_mwl_port_status", StringComparison.OrdinalIgnoreCase))
            {
                float radius = 80f;
                if (parts.Length >= 2)
                {
                    float.TryParse(parts[1], out radius);
                }

                CustomCommands.PrintMwlPortStatus(radius, line => _commandServer?.SendOutput(line));
                return true;
            }

            if (parts[0].Equals("cli_mwl_goto_port", StringComparison.OrdinalIgnoreCase))
            {
                int index = 0;
                if (parts.Length >= 2)
                {
                    int.TryParse(parts[1], out index);
                }

                CustomCommands.GotoMwlPort(Math.Max(0, index), line => _commandServer?.SendOutput(line));
                return true;
            }

            if (parts[0].Equals("cli_mwl_clear_shipments", StringComparison.OrdinalIgnoreCase))
            {
                CustomCommands.ClearMwlShipments(line => _commandServer?.SendOutput(line));
                return true;
            }

            if (parts[0].Equals("cli_mwl_port_payment_regression", StringComparison.OrdinalIgnoreCase))
            {
                string itemPrefab = parts.Length >= 2 ? parts[1] : "Wood";
                int itemCount = 10;
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[2], out itemCount);
                }

                CustomCommands.RunMwlPortPaymentRegression(itemPrefab, Math.Max(1, itemCount), line => _commandServer?.SendOutput(line));
                return true;
            }

            if (parts[0].Equals("cli_mwl_port_delivery_regression", StringComparison.OrdinalIgnoreCase))
            {
                string itemPrefab = parts.Length >= 2 ? parts[1] : "Wood";
                int itemCount = 10;
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[2], out itemCount);
                }

                CustomCommands.RunMwlPortDeliveryRegression(itemPrefab, Math.Max(1, itemCount), line => _commandServer?.SendOutput(line));
                return true;
            }

            if (parts[0].Equals("cli_mwl_port_ownership_seed", StringComparison.OrdinalIgnoreCase))
            {
                string itemPrefab = parts.Length >= 2 ? parts[1] : "Wood";
                int itemCount = 10;
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[2], out itemCount);
                }

                CustomCommands.SeedMwlPortOwnershipShipment(itemPrefab, Math.Max(1, itemCount), line => _commandServer?.SendOutput(line));
                return true;
            }

            if (parts[0].Equals("cli_mwl_port_ownership_check", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 2)
                {
                    _commandServer?.SendOutput("Usage: cli_mwl_port_ownership_check <shipmentId> [blocked|allowed]");
                    return true;
                }

                string expectation = parts.Length >= 3 ? parts[2] : "blocked";
                CustomCommands.CheckMwlPortOwnershipShipment(parts[1], expectation, line => _commandServer?.SendOutput(line));
                return true;
            }

            if (!parts[0].Equals("cli_connect", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (parts.Length < 2)
            {
                _commandServer?.SendOutput("Usage: cli_connect <host:port> [password]");
                return true;
            }

            QueueServerConnect(parts[1], parts.Length >= 3 ? parts[2] : null);
            _commandServer?.SendOutput($"OK: Queued server join for {parts[1]}");
            return true;
        }

        private void ExecuteCommand(string command)
        {
            if (Console.instance == null)
            {
                _commandServer?.SendOutput("Error: Console not available (game not fully loaded)");
                return;
            }

            _capturedOutput.Clear();
            _capturingOutput = true;

            try
            {
                // Execute the command
                Console.instance.TryRunCommand(command, silentFail: false, skipAllowedCheck: false);

                // Give a moment for output to be generated
                System.Threading.Thread.Sleep(100);

                // Send captured output or confirmation
                if (_capturedOutput.Count > 0)
                {
                    foreach (var line in _capturedOutput)
                    {
                        _commandServer?.SendOutput(line);
                    }
                }
                else
                {
                    _commandServer?.SendOutput($"Executed: {command}");
                }
            }
            finally
            {
                _capturingOutput = false;
            }
        }

        public static void QueueServerConnect(string address, string? password)
        {
            if (Instance == null)
            {
                _autoStartQueuedJoinRequested = true;
                return;
            }

            Instance._pendingConnectAddress = address;
            Instance._pendingConnectPassword = password;
            RequestAutoStartQueuedJoin();
            Instance.TryQueuePendingServerConnect();
        }

        private void TryQueuePendingServerConnect()
        {
            if (string.IsNullOrWhiteSpace(_pendingConnectAddress))
            {
                return;
            }

            if (FejdStartup.instance == null)
            {
                return;
            }

            string address = _pendingConnectAddress!;
            string? password = _pendingConnectPassword;

            if (!CustomCommands.TryParseHostPort(address, out string host, out int port))
            {
                _pendingConnectAddress = null;
                _pendingConnectPassword = null;
                Log.LogError($"Invalid queued dedicated server address '{address}'");
                return;
            }

            if (!string.IsNullOrWhiteSpace(password))
            {
                SetServerPassword(password!);
            }

            _pendingConnectAddress = null;
            _pendingConnectPassword = null;
            CustomCommands.StartDedicatedServerJoin(host, port, line => Log.LogInfo(line));
        }

        private static void SetServerPassword(string password)
        {
            PropertyInfo? property = typeof(FejdStartup).GetProperty("ServerPassword", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo? setter = property?.GetSetMethod(true);
            if (setter == null)
            {
                Log.LogWarning("Could not set FejdStartup.ServerPassword; server password was not applied.");
                return;
            }

            setter.Invoke(null, new object[] { password });
        }

        public void CaptureOutput(string text)
        {
            if (_capturingOutput)
            {
                _capturedOutput.Add(text);
            }
        }

        public static valheimCLIPlugin? Instance { get; private set; }

        public static void RequestAutoStartQueuedJoin()
        {
            _autoStartQueuedJoinRequested = true;
            if (Instance != null)
            {
                Instance._autoStartQueuedJoinAttempted = false;
            }
        }

        private static bool HasStartupJoinArgument()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "+connect" || args[i] == "+connect_lobby")
                {
                    return true;
                }
            }

            return false;
        }

        private void TryAutoStartQueuedJoin()
        {
            if (_autoStartQueuedJoinConfig?.Value != true || !_autoStartQueuedJoinRequested || _autoStartQueuedJoinAttempted)
            {
                return;
            }

            FejdStartup fejd = FejdStartup.instance;
            if (fejd == null || fejd.m_characterSelectScreen == null || !fejd.m_characterSelectScreen.activeInHierarchy)
            {
                return;
            }

            if (!HasQueuedJoin(fejd))
            {
                return;
            }

            _autoStartQueuedJoinAttempted = true;
            Log.LogInfo("Queued server join detected; starting selected character.");
            fejd.OnCharacterStart();
        }

        private static bool HasQueuedJoin(FejdStartup fejd)
        {
            if (QueuedJoinServerField == null)
            {
                Log.LogWarning("Could not inspect FejdStartup.m_queuedJoinServer; auto-start skipped.");
                return false;
            }

            object? value = QueuedJoinServerField.GetValue(fejd);
            return value is ServerJoinData queuedJoin && queuedJoin.IsValid;
        }

        private void OnEnable()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            _commandServer?.Dispose();
            Config.Save();
        }

        private DateTime _lastReloadTime;
        private const long RELOAD_DELAY = 10000000; // One second

        private void SetupWatcher()
        {
            _lastReloadTime = DateTime.Now;
            FileSystemWatcher watcher = new(BepInEx.Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            var now = DateTime.Now;
            var time = now.Ticks - _lastReloadTime.Ticks;
            if (!File.Exists(ConfigFileFullPath) || time < RELOAD_DELAY) return;

            try
            {
                Log.LogInfo("Attempting to reload configuration...");
                Config.Reload();
                Log.LogInfo("Configuration reloaded successfully!");
            }
            catch
            {
                Log.LogError($"There was an issue loading {ConfigFileName}");
                return;
            }

            _lastReloadTime = now;
        }
    }

    // Harmony patch to capture console output
    [HarmonyPatch(typeof(Terminal), nameof(Terminal.AddString), new Type[] { typeof(string) })]
    public static class Terminal_AddString_Patch
    {
        public static void Postfix(string text)
        {
            valheimCLIPlugin.Instance?.CaptureOutput(text);
        }
    }
}
