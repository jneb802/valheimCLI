namespace valheimCLI
{
    /// <summary>Read-only access facts. This command neither grants permissions nor marks a profile cheated.</summary>
    internal static class AccessCommands
    {
        internal static void Register()
        {
            new Terminal.ConsoleCommand("cli_access", "Observe test command access without changing it: cli_access", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length != 1) { args.Context.AddString("ERROR: code=usage cli_access takes no arguments"); return; }
                ZNet? net = ZNet.instance;
                bool localPlayer = Player.m_localPlayer != null;
                bool profile = Game.instance != null && Game.instance.GetPlayerProfile() != null;
                args.Context.AddString("ACCESS " + AccessSnapshot.Json(Terminal.m_cheat,
                    Achievements.IsCheatedAtAll(), ClientCommandAccess.AllowOnServerClients,
                    net != null && net.IsServer(), net != null && net.IsDedicated(), net != null && !net.IsServer(),
                    localPlayer, profile));
            }, isCheat: false);
        }
    }
}
