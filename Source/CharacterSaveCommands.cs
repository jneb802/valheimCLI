using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace valheimCLI;

internal static class CharacterSaveCommands
{
    internal static void Register() => new Terminal.ConsoleCommand("cli_save_character",
        "Save the current character through the normal game path and verify the snapshot: [count=1, maximum 5]. Multiple saves run before the next frame.",
        Save, isCheat: true);

    private static void Save(Terminal.ConsoleEventArgs args)
    {
        int count = 1;
        if (args.Length > 2 || args.Length == 2 &&
            (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1 || count > 5))
        { args.Context.AddString("ERROR: Use cli_save_character [count 1–5]"); return; }
        Player player = Player.m_localPlayer;
        if (player == null || Game.instance == null)
        { args.Context.AddString("ERROR: A local player in a world is required"); return; }
        if (SaveSystem.HasSessionFlag(SaveSystemSessionFlags.DontSaveCharacter))
        { args.Context.AddString("ERROR: Character saving is blocked for this session"); return; }
        try
        {
            for (int index = 0; index < count; index++)
            {
                Stopwatch elapsed = Stopwatch.StartNew();
                Game.instance.SavePlayerProfile(false);
                PlayerProfile profile = Game.instance.GetPlayerProfile();
                PlayerProfile saved = new(profile.GetFilename(), profile.m_fileSource);
                if (!saved.Load() || saved.m_playerData == null || profile.m_playerData == null ||
                    !saved.m_playerData.SequenceEqual(profile.m_playerData))
                    throw new InvalidOperationException("Saved character snapshot did not match the current profile");
                args.Context.AddString($"OK: CHARACTER_SAVE index={index + 1} count={count} verified=true elapsedMs={elapsed.ElapsedMilliseconds}");
            }
        }
        catch (Exception exception)
        { args.Context.AddString("ERROR: Character save failed: " + exception.GetBaseException().Message); }
    }
}
