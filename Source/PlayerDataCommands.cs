using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Text;
using HarmonyLib;

namespace valheimCLI;

internal static class PlayerDataCommands
{
    private static PlayerProfile? failingProfile;
    private static bool failureInjected;
    private static int repliesToDrop;
    private static bool replyPatchInstalled;

    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_endgame_drop_cleanup_reply", "Drop the next Endgame cleanup replies for retry validation: <count 0-10>", args =>
        {
            if (args.Length != 2 || !int.TryParse(args[1], out int count) || count < 0 || count > 10 || Player.m_localPlayer == null)
            { args.Context.AddString("ERROR: A local player and count 0-10 are required"); return; }
            Type? type = Type.GetType("ValheimEndgame.QuestService, ValheimEndgame", false);
            MethodInfo? apply = type?.GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic);
            if (apply == null) { args.Context.AddString("ERROR: Endgame is unavailable"); return; }
            if (!replyPatchInstalled)
            {
                new Harmony("valheimCLI.validation.cleanup-reply").Patch(apply,
                    prefix: new HarmonyMethod(typeof(PlayerDataCommands), nameof(DropReply)));
                replyPatchInstalled = true;
            }
            repliesToDrop = count;
            args.Context.AddString("OK: cleanupRepliesToDrop=" + count);
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_endgame_rewards", "Inspect or use Endgame's normal rewards actions: <list|refresh|claim> [run]", args =>
        {
            Type? type = Type.GetType("ValheimEndgame.QuestService, ValheimEndgame", false);
            object? service = type?.GetProperty("Instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            if (service == null || Player.m_localPlayer == null || args.Length < 2)
            { args.Context.AddString("ERROR: Endgame and a local player are required"); return; }
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            if (args[1] == "refresh" && args.Length == 2) type!.GetMethod("Request", flags)!.Invoke(service, null);
            else if (args[1] == "claim" && args.Length == 3)
                type!.GetMethod("ClaimEncounter", flags)!.Invoke(service, new object[] { args[2] });
            else if (args[1] == "list" && args.Length == 2)
            {
                IEnumerable rewards = (IEnumerable)type!.GetProperty("EncounterRewards", flags)!.GetValue(service);
                int count = 0;
                foreach (object reward in rewards)
                {
                    args.Context.AddString("run=" + reward.GetType().GetProperty("Run")!.GetValue(reward));
                    count++;
                }
                args.Context.AddString("rewards=" + count);
            }
            else { args.Context.AddString("ERROR: Use cli_endgame_rewards <list|refresh|claim> [run]"); return; }
            args.Context.AddString("OK: Endgame reward action " + args[1]);
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_player_data_set", "Set a test character field: <key> <base64-UTF8-value>", args =>
        {
            if (args.Length != 3 || Player.m_localPlayer == null)
            { args.Context.AddString("ERROR: A local player, key and base64 value are required"); return; }
            string value = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(args[2]));
            Player.m_localPlayer.m_customData[args[1]] = value;
            args.Context.AddString("OK: Set " + args[1] + " bytes=" + Encoding.UTF8.GetByteCount(value));
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_player_data_read", "Read test character fields: <key-prefix>", args =>
        {
            if (args.Length != 2 || Player.m_localPlayer == null)
            { args.Context.AddString("ERROR: A local player and key prefix are required"); return; }
            args.Context.AddString($"world={ZNet.instance.GetWorldUID()} player={Player.m_localPlayer.GetPlayerID()}");
            foreach (System.Collections.Generic.KeyValuePair<string, string> entry in Player.m_localPlayer.m_customData
                .Where(entry => entry.Key.StartsWith(args[1], StringComparison.Ordinal)).OrderBy(entry => entry.Key))
                args.Context.AddString(entry.Key + "=" + entry.Value.Replace("\n", "\\n"));
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_player_data_save", "Save through the normal character path and verify disk bytes: [fail]", args =>
        {
            if (args.Length > 2 || args.Length == 2 && args[1] != "fail" || Player.m_localPlayer == null || Game.instance == null)
            { args.Context.AddString("ERROR: Use cli_player_data_save [fail] in a world"); return; }
            PlayerProfile profile = Game.instance.GetPlayerProfile();
            PlayerProfile before = new(profile.GetFilename(), profile.m_fileSource);
            bool existed = before.Load();
            if (args.Length == 2) failingProfile = profile;
            failureInjected = false;
            try
            {
                Stopwatch elapsed = Stopwatch.StartNew();
                Game.instance.SavePlayerProfile(false);
                PlayerProfile saved = new(profile.GetFilename(), profile.m_fileSource);
                bool loaded = saved.Load();
                bool verified = args.Length == 2
                    ? failureInjected && existed == loaded && (!loaded || before.m_playerData.SequenceEqual(saved.m_playerData))
                    : loaded && profile.m_playerData.SequenceEqual(saved.m_playerData);
                args.Context.AddString($"{(verified ? "OK" : "ERROR")}: CHARACTER_SAVE failed={args.Length == 2} verified={verified} elapsedMs={elapsed.ElapsedMilliseconds}");
            }
            finally { failingProfile = null; }
        }, isCheat: true);
    }

    [HarmonyPrefix, HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.Save))]
    private static bool FailSave(PlayerProfile __instance, ref bool __result)
    {
        if (__instance != failingProfile) return true;
        failureInjected = true;
        __result = false;
        return false;
    }

    private static bool DropReply(object reply)
    {
        if (repliesToDrop == 0 || reply.GetType().GetProperty("Cleanup")?.GetValue(reply) == null) return true;
        repliesToDrop--;
        return false;
    }
}
