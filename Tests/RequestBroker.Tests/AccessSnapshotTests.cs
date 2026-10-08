using System.Text.Json;
using Xunit;
namespace valheimCLI.Tests;
public class AccessSnapshotTests
{
    [Theory]
    [InlineData(false, false, false, false, false, false, false, false)]
    [InlineData(true, false, false, true, true, false, false, true)]
    [InlineData(true, true, false, false, false, true, true, true)]
    [InlineData(true, true, true, false, false, true, true, true)]
    public void AccessFactsDoNotConflateDifferentGates(bool dev, bool ack, bool allow, bool server,
        bool dedicated, bool joined, bool player, bool profile)
    {
        using var json = JsonDocument.Parse(AccessSnapshot.Json(dev, ack, allow, server, dedicated, joined, player, profile));
        var root = json.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.True(root.GetProperty("complete").GetBoolean());
        foreach (var fact in new[] { ("devcommands", dev), ("cheatsAcknowledged", ack), ("allowOnServerClients", allow),
            ("server", server), ("dedicated", dedicated), ("joinedClient", joined), ("localPlayer", player), ("profileAvailable", profile) })
            Assert.Equal(fact.Item2, root.GetProperty(fact.Item1).GetBoolean());
    }
}
