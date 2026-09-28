using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using valheimCLI.Extensions;

namespace valheimCLI
{
    internal static class SessionCapabilities
    {
        internal static ExtensionRegistration Register(ExtensionRegistry registry) => registry.Register("valheim.session", "0.1.0", 1,
            new ExtensionCommand("state", "Read session facts; mod readiness must be checked separately", State, readOnly: true),
            new ExtensionCommand("join", "Join from menu: <host:port> <character> [password-environment-variable]", Join),
            new ExtensionCommand("leave", "Save the local character and return to the menu", Leave, role: ExtensionRole.Client, needsWorld: true),
            new ExtensionCommand("save", "Confirm a server world save: [timeout-seconds, 1..600]", Save, role: ExtensionRole.Server, needsWorld: true));

        private static bool WorldPresent => Game.instance != null && ZNet.World != null && ZoneSystem.instance != null && ZDOMan.instance != null;
        private static bool PlayerReady => Player.m_localPlayer != null && !Player.m_localPlayer.IsTeleporting() && !Player.m_localPlayer.IsDead();
        private static bool ShuttingDown => Game.instance != null && Game.instance.IsShuttingDown();
        private static IEnumerator State(ExtensionContext context)
        {
            if (context.Arguments.Count != 0) { context.Fail("usage", "state takes no arguments"); yield break; }
            var net = ZNet.instance;
            bool server = net != null && net.IsServer();
            bool present = WorldPresent;
            string connection = ZNet.GetConnectionStatus().ToString();
            bool ready = SessionReadiness.WorldReady(present, server, ZNet.m_loadError, ShuttingDown, connection, PlayerReady);
            string phase = ShuttingDown ? "leaving" : ZNet.m_loadError ? "failed" : ready ? "world-present" : present ? "loading" : FejdStartup.instance != null ? "menu" : "loading";
            context.Succeed(new Dictionary<string, object?> {
                ["source"] = "session-state", ["complete"] = true, ["phase"] = phase,
                ["worldUid"] = present ? ZNet.World.m_uid.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                ["worldPresent"] = present, ["worldReady"] = ready, ["server"] = server,
                ["dedicated"] = net != null && net.IsDedicated(), ["localPlayer"] = Player.m_localPlayer != null,
                ["playerReady"] = PlayerReady, ["saving"] = net != null && net.IsSaving(),
                ["loadError"] = ZNet.m_loadError, ["connectionStatus"] = connection
            });
            yield break;
        }

        private static IEnumerator Join(ExtensionContext context)
        {
            var args = context.Arguments;
            if ((args.Count != 2 && args.Count != 3) || !CustomCommands.TryParseHostPort(args[0], out string host, out int port))
            { context.Fail("usage", "join <host:port> <character> [password-environment-variable]"); yield break; }
            if (FejdStartup.instance == null || Game.instance != null || (ZNet.instance != null && ZNet.instance.IsDedicated()))
            { context.Fail("not_menu", "Join requires an idle client main menu."); yield break; }
            string password = args.Count == 3 ? Environment.GetEnvironmentVariable(args[2]) ?? "" : "";
            if (args.Count == 3 && password.Length == 0) { context.Fail("password_unavailable", "The named password environment variable is empty or missing on the game host."); yield break; }
            MethodInfo? setter = typeof(FejdStartup).GetProperty("ServerPassword", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetSetMethod(true);
            if (setter == null) { context.Fail("unsupported", "This game build has no password setter."); yield break; }
            string? rejection = null;
            CustomCommands.SelectCharacter(args[1], line => { if (line.StartsWith("ERROR:", StringComparison.Ordinal)) rejection = "character_unavailable"; });
            if (rejection != null) { context.Fail(rejection, "The requested existing character could not be selected."); yield break; }
            var originalNet = ZNet.instance;
            var clock = Stopwatch.StartNew();
            IEnumerator run = SessionTransition.Run(context, "join", () => {
                setter.Invoke(null, new object[] { password }); // Omission clears a stale password; never echo credentials.
                CustomCommands.StartDedicatedServerJoin(host, port, line => { if (line.StartsWith("ERROR:", StringComparison.Ordinal)) rejection = "join_refused"; });
            }, () => SessionReadiness.JoinResult(rejection, ZNet.instance != null && !ReferenceEquals(originalNet, ZNet.instance),
                WorldPresent, PlayerReady, ZNet.m_loadError, ZNet.GetConnectionStatus().ToString()), () => clock.Elapsed.TotalSeconds, 120);
            while (run.MoveNext()) yield return run.Current;
        }

        private static IEnumerator Leave(ExtensionContext context)
        {
            if (context.Arguments.Count != 0) { context.Fail("usage", "leave takes no arguments"); yield break; }
            var game = Game.instance;
            if (game == null || ShuttingDown) { context.Fail("not_ready", "No active client session is available to leave."); yield break; }
            var clock = Stopwatch.StartNew();
            var run = SessionTransition.Run(context, "leave", () => game.Logout(save: true, changeToStartScene: true),
                () => Game.instance == null && FejdStartup.instance != null ? "" : null, () => clock.Elapsed.TotalSeconds, 120);
            while (run.MoveNext()) yield return run.Current;
        }

        private static IEnumerator Save(ExtensionContext context)
        {
            float timeout = 120;
            if (context.Arguments.Count > 1 || context.Arguments.Count == 1 && (!CommandArguments.TryFiniteFloat(context.Arguments[0], out timeout) || timeout < 1 || timeout > 600))
            { context.Fail("usage", "save [timeout-seconds, 1..600]"); yield break; }
            var net = ZNet.instance; var world = ZNet.World; var game = Game.instance;
            if (net == null || world == null || game == null || !net.IsServer()) { context.Fail("not_server", "A loaded server world is required."); yield break; }
            var clock = Stopwatch.StartNew();
            var outcome = new SaveOutcome { TimeoutSeconds = timeout, World = world.m_name };
            var run = SessionSave.Run(new NativeSessionSave(net, world, game), outcome, () => clock.Elapsed.TotalSeconds,
                () => context.Cancelled, context.WaitForQuiescence, result => {
                    // The reply is held until the write ends, so Finished is known here; a write that
                    // outlived the timeout reports its real outcome with pastTimeout=true.
                    if (!result.Saved) { context.Fail(result.Skipped.Length > 0 || !result.Started ? "save_skipped" : !result.Finished ? "save_timeout" : "save_failed", result.Reply()); return; }
                    context.Succeed(new Dictionary<string, object?> { ["source"] = "session-save", ["complete"] = true,
                        ["worldUid"] = world.m_uid.ToString(System.Globalization.CultureInfo.InvariantCulture), ["saved"] = true,
                        ["before"] = result.SaveNumberBefore, ["after"] = result.SaveNumberAfter, ["milliseconds"] = result.Milliseconds,
                        ["pastTimeout"] = result.PastTimeout });
                }, context.Fail);
            while (run.MoveNext()) yield return run.Current;
        }
    }
}
