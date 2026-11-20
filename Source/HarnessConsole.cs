namespace valheimCLI
{
    /// <summary>
    /// Where a console line goes while ValheimCLI runs a caller's command (#981). The line always goes back to the
    /// caller; it reaches the player's in-game console only when no harness command is capturing it, or when another
    /// terminal (the chat window, say) wrote it.
    /// </summary>
    internal static class HarnessConsole
    {
        /// <summary>Whether the game's own AddString should still show the line.</summary>
        internal static bool ShowsInGame(bool capturedForCaller, bool writtenToConsole) => !(capturedForCaller && writtenToConsole);
    }
}
