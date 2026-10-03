namespace valheimCLI
{
    /// <summary>The schema is kept independent of Unity so consumers can test the permission combinations.</summary>
    internal static class AccessSnapshot
    {
        internal static string Json(bool devcommands, bool cheatsAcknowledged, bool allowOnServerClients,
            bool server, bool dedicated, bool joinedClient, bool localPlayer, bool profileAvailable)
        {
            string B(bool value) => value ? "true" : "false";
            return "{\"schemaVersion\":1,\"complete\":true,\"devcommands\":" + B(devcommands) +
                ",\"cheatsAcknowledged\":" + B(cheatsAcknowledged) +
                ",\"allowOnServerClients\":" + B(allowOnServerClients) + ",\"server\":" + B(server) +
                ",\"dedicated\":" + B(dedicated) + ",\"joinedClient\":" + B(joinedClient) +
                ",\"localPlayer\":" + B(localPlayer) + ",\"profileAvailable\":" + B(profileAvailable) + "}";
        }
    }
}
