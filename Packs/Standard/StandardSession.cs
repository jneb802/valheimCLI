using System;
using System.IO;
using System.Reflection;
namespace valheimCLI
{
    internal static class StandardSession
    {
        private static bool _autoStartQueuedJoinAttempted;
        private static string? _pendingConnectAddress;
        private static string? _pendingConnectPassword;
        private static bool _autoStartQueuedJoinRequested;
        private static StartupWorldSpec? _startupWorld;
        private static bool _startupWorldAttempted;
        private static readonly FieldInfo? QueuedJoinServerField = typeof(FejdStartup).GetField("m_queuedJoinServer", BindingFlags.Instance | BindingFlags.NonPublic);
        internal static void Initialize()
        {
            string? path = Environment.GetEnvironmentVariable(StartupWorldSpec.PathVariable);
            if (!string.IsNullOrEmpty(path) && Environment.GetEnvironmentVariable(StartupWorldSpec.ClaimedVariable) != "1")
            {
                try
                {
                    if (HasStartupJoinArgument()) throw new InvalidDataException("Do not combine the startup spec with +connect or +connect_lobby.");
                    if (!Path.IsPathRooted(path) || new FileInfo(path).Length > 4096)
                        throw new InvalidDataException("The startup spec must be a small file at an absolute path.");
                    _startupWorld = StartupWorldSpec.Parse(File.ReadAllText(path));
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
                { valheimCLIPlugin.Log.LogError("Client startup spec refused: " + error.Message); }
            }
            else if (HasStartupJoinArgument()) RequestAutoStartQueuedJoin();
        }
        internal static void Tick() { TryStartupWorld(); TryQueuePendingServerConnect(); TryAutoStartQueuedJoin(); TeleportTrace.Tick(); TestTeleportTiming.Tick(); }

        private static void TryStartupWorld()
        {
            StartupWorldSpec? spec = _startupWorld;
            if (spec == null || _startupWorldAttempted) return;
            FejdStartup fejd = FejdStartup.instance;
            // Wait for the menu's profile UI and platform matchmaking to exist; FejdStartup itself appears earlier.
            if (fejd == null || fejd.m_characterSelectScreen == null ||
                spec.Mode == "join" && ZSteamMatchmaking.instance == null || Game.instance != null)
                return;
            _startupWorldAttempted = true;
            Environment.SetEnvironmentVariable(StartupWorldSpec.ClaimedVariable, "1"); // A Standard-pack reload cannot issue it twice.
            try
            {
                string selection = "";
                CustomCommands.SelectCharacter(spec.Character, line => selection = line);
                if (!selection.StartsWith("OK: Selected character '", StringComparison.Ordinal) ||
                    !selection.EndsWith(" (" + spec.Character + ", Local)", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The exact disposable local character was not selected: " + selection);

                if (spec.Devcommands)
                {
                    Terminal.m_cheat = true;
                    Console.instance?.updateCommandList();
                }

                string password = spec.PasswordVariable == null ? "" : Environment.GetEnvironmentVariable(spec.PasswordVariable) ?? "";
                if (spec.PasswordVariable != null && password.Length == 0)
                    throw new InvalidOperationException("The named startup password variable is empty or missing.");
                string result = "";
                switch (spec.Mode)
                {
                    case "join":
                        if (!CustomCommands.TryParseHostPort(spec.Target, out string host, out int port))
                            throw new InvalidDataException("The startup join target is not host:port.");
                        if (typeof(FejdStartup).GetProperty("ServerPassword", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetSetMethod(true) is not MethodInfo setter)
                            throw new InvalidOperationException("This game build has no server-password setter.");
                        setter.Invoke(null, new object[] { password });
                        CustomCommands.StartDedicatedServerJoin(host, port, line => result = line);
                        break;
                    case "host":
                        CustomCommands.StartHostedWorld(spec.Target, spec.PublicServer, spec.Crossplay, password, line => result = line);
                        break;
                    default:
                        CustomCommands.StartLocalWorld(spec.Target, line => result = line);
                        break;
                }
                if (!result.StartsWith("OK:", StringComparison.Ordinal))
                    throw new InvalidOperationException("The startup action was refused: " + result);
                valheimCLIPlugin.Log.LogInfo("Client startup spec issued once: " + spec.Mode + " using local character " + spec.Character);
            }
            catch (Exception error)
            { valheimCLIPlugin.Log.LogError("Client startup spec failed without retry: " + error); }
        }
        public static void QueueServerConnect(string address, string? password)
        {
            if (valheimCLIPlugin.Instance == null)
            {
                _autoStartQueuedJoinRequested = true;
                return;
            }

            _pendingConnectAddress = address;
            _pendingConnectPassword = password;
            RequestAutoStartQueuedJoin();
            TryQueuePendingServerConnect();
        }

        private static void TryQueuePendingServerConnect()
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
                valheimCLIPlugin.Log.LogError($"Invalid queued dedicated server address '{address}'");
                return;
            }

            if (!string.IsNullOrWhiteSpace(password))
            {
                SetServerPassword(password!);
            }

            _pendingConnectAddress = null;
            _pendingConnectPassword = null;
            CustomCommands.StartDedicatedServerJoin(host, port, line => valheimCLIPlugin.Log.LogInfo(line));
        }

        internal static void SetServerPassword(string password)
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

        public static void RequestAutoStartQueuedJoin()
        {
            _autoStartQueuedJoinRequested = true;
            if (valheimCLIPlugin.Instance != null)
            {
                _autoStartQueuedJoinAttempted = false;
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

        private static void TryAutoStartQueuedJoin()
        {
            if (valheimCLIPlugin.Instance?.AutoStartQueuedJoin != true || !_autoStartQueuedJoinRequested || _autoStartQueuedJoinAttempted)
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
            valheimCLIPlugin.Log.LogInfo("Queued server join detected; starting selected character.");
            fejd.OnCharacterStart();
        }

        private static bool HasQueuedJoin(FejdStartup fejd)
        {
            if (QueuedJoinServerField == null)
            {
                valheimCLIPlugin.Log.LogWarning("Could not inspect FejdStartup.m_queuedJoinServer; auto-start skipped.");
                return false;
            }

            object? value = QueuedJoinServerField.GetValue(fejd);
            return value is ServerJoinData queuedJoin && queuedJoin.IsValid;
        }

    }
}
