namespace valheimCLI
{
    internal static class SessionReadiness
    {
        internal static bool WorldReady(bool present, bool server, bool loadError, bool leaving, string connection, bool playerReady) =>
            present && !loadError && !leaving && (server || connection == "Connected" && playerReady);

        internal static string? JoinResult(string? rejection, bool newNetwork, bool present, bool playerReady, bool loadError, string connection)
        {
            if (rejection != null) return rejection;
            if (present && playerReady && !loadError && connection == "Connected") return "";
            if (newNetwork && loadError) return "load_error";
            if (newNetwork && connection.StartsWith("Error", System.StringComparison.Ordinal)) return "join_failed";
            return null;
        }
    }
}
