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
        private ConfigEntry<int>? _portConfig;
        private ConfigEntry<bool>? _enabledConfig;

        private readonly List<string> _capturedOutput = new();
        private bool _capturingOutput;

        public void Awake()
        {
            _enabledConfig = Config.Bind("Server", "Enabled", true, "Enable the command server");
            _portConfig = Config.Bind("Server", "Port", 5555, "Port for the command server (localhost only)");

            Assembly assembly = Assembly.GetExecutingAssembly();
            HarmonyInstance.PatchAll(assembly);

            if (_enabledConfig.Value)
            {
                _commandServer = new CommandServer(Log, _portConfig.Value);
                _commandServer.Start();
            }

            SetupWatcher();
            Log.LogInfo($"{ModName} loaded. CLI server on port {_portConfig.Value}");
        }

        public void Update()
        {
            ProcessPendingCommands();
        }

        private void ProcessPendingCommands()
        {
            if (_commandServer == null) return;

            while (_commandServer.TryGetPendingCommand(out var command) && command != null)
            {
                if (string.IsNullOrEmpty(command)) continue;

                try
                {
                    ExecuteCommand(command);
                }
                catch (Exception ex)
                {
                    _commandServer.SendOutput($"Error: {ex.Message}");
                    Log.LogError($"Command execution error: {ex}");
                }
            }
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

        public void CaptureOutput(string text)
        {
            if (_capturingOutput)
            {
                _capturedOutput.Add(text);
            }
        }

        public static valheimCLIPlugin? Instance { get; private set; }

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
