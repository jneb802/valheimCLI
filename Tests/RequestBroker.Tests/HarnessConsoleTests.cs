using valheimCLI;
using Xunit;

namespace valheimCLI.Tests;

/// <summary>#981: a harness command's output goes to its caller, not the player's console.</summary>
public sealed class HarnessConsoleTests
{
    [Fact]
    public void AHarnessCommandsConsoleLineStaysOutOfTheGame() => Assert.False(HarnessConsole.ShowsInGame(capturedForCaller: true, writtenToConsole: true));

    [Fact]
    public void APlayerTypedCommandStillShows() => Assert.True(HarnessConsole.ShowsInGame(capturedForCaller: false, writtenToConsole: true));

    [Fact]
    public void ChatWrittenDuringAHarnessCommandStillShows() => Assert.True(HarnessConsole.ShowsInGame(capturedForCaller: true, writtenToConsole: false));
}
