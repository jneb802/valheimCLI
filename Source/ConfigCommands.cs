using System;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace valheimCLI;

internal static class ConfigCommands
{
    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_config_get", "Read a loaded mod setting: cli_config_get <pluginGuid> <section>/<key>", args => Run(args, false));
        new Terminal.ConsoleCommand("cli_config_set", "Set an existing loaded mod setting: cli_config_set <pluginGuid> <section>/<key> = <serializedValue>", args => Run(args, true));
    }

    private static void Run(Terminal.ConsoleEventArgs args, bool set)
    {
        string text = args.FullLine;
        int commandEnd = text.IndexOf(' ');
        text = commandEnd < 0 ? "" : text.Substring(commandEnd + 1).Trim();
        int pluginEnd = text.IndexOf(' ');
        if (pluginEnd < 1)
        {
            args.Context.AddString("ERROR: Specify a plugin GUID and section/key");
            return;
        }
        string guid = text.Substring(0, pluginEnd);
        string path = text.Substring(pluginEnd + 1).Trim();
        string value = "";
        if (set)
        {
            int separator = path.IndexOf('=');
            if (separator < 0)
            {
                args.Context.AddString("ERROR: Expected section/key = serializedValue");
                return;
            }
            value = path.Substring(separator + 1).Trim();
            path = path.Substring(0, separator).Trim();
        }
        int slash = path.IndexOf('/');
        if (slash < 1 || slash == path.Length - 1)
        {
            args.Context.AddString("ERROR: Expected section/key");
            return;
        }
        if (!Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo info) || info.Instance == null)
        {
            args.Context.AddString("ERROR: Plugin is not loaded");
            return;
        }
        ConfigDefinition definition = new(path.Substring(0, slash).Trim(), path.Substring(slash + 1).Trim());
        if (!info.Instance.Config.ContainsKey(definition))
        {
            args.Context.AddString("ERROR: Configuration entry does not exist");
            return;
        }
        ConfigEntryBase entry = info.Instance.Config[definition];
        try
        {
            if (set)
            {
                object converted = TomlTypeConverter.ConvertToValue(value, entry.SettingType);
                if (entry.Description.AcceptableValues != null && !entry.Description.AcceptableValues.IsValid(converted))
                {
                    args.Context.AddString("ERROR: Value is outside the setting's accepted values");
                    return;
                }
                entry.BoxedValue = converted;
            }
            args.Context.AddString($"CONFIG plugin={guid} section={definition.Section} key={definition.Key} type={entry.SettingType.Name} value={entry.GetSerializedValue()}");
        }
        catch (Exception exception)
        {
            args.Context.AddString($"ERROR: Configuration operation failed ({exception.GetType().Name})");
        }
    }
}
