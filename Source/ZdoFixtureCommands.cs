using System;
using System.Globalization;
using System.Linq;

namespace valheimCLI;

internal static class ZdoFixtureCommands
{
    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_zdo_set_string",
            "Set a loaded, locally owned object's saved string in a host world: cli_zdo_set_string <user:id> <key> <value...>. Value is the remaining unquoted text.", args =>
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || Player.m_localPlayer == null)
            {
                args.Context.AddString("ERROR: String fixtures require a local host world and player.");
                return;
            }
            if (args.Length < 4 || string.IsNullOrWhiteSpace(args[2]))
            {
                args.Context.AddString("ERROR: Expected object ID, string key, and value.");
                return;
            }
            string[] parts = args[1].Split(':');
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long user) ||
                !uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint id))
            {
                args.Context.AddString("ERROR: Invalid object ID.");
                return;
            }
            ZDO? record = ZDOMan.instance.GetZDO(new ZDOID(user, id));
            ZNetView? view = record == null ? null : ZNetScene.instance.FindInstance(record);
            if (record == null || view == null || !view.IsValid() || !view.IsOwner())
            {
                args.Context.AddString("ERROR: Object must be loaded and locally owned.");
                return;
            }
            string key = args[2];
            string before = record.GetString(key);
            string value = string.Join(" ", args.Args.Skip(3));
            record.Set(key, value);
            args.Context.AddString($"OK: ZDO {record.m_uid} string {key}: '{Escape(before)}' -> '{Escape(record.GetString(key))}'.");
        }, isCheat: true);
    }

    private static string Escape(string text) => text.Replace("\r", "\\r").Replace("\n", "\\n");
}
