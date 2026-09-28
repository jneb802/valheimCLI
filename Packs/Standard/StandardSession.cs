using System;
using System.Reflection;
namespace valheimCLI
{
    internal static class StandardSession
    {
        private static bool _autoStartQueuedJoinAttempted;
        private static string? _pendingConnectAddress;
        private static string? _pendingConnectPassword;
        private static bool _autoStartQueuedJoinRequested;
        private static readonly FieldInfo? QueuedJoinServerField = typeof(FejdStartup).GetField("m_queuedJoinServer", BindingFlags.Instance | BindingFlags.NonPublic);
        internal static void Initialize() { if (HasStartupJoinArgument()) RequestAutoStartQueuedJoin(); }
        internal static void Tick() { TryQueuePendingServerConnect(); TryAutoStartQueuedJoin(); }
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
