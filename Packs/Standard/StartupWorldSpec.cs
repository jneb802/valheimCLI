using System;
using System.Collections.Generic;
using System.IO;

namespace valheimCLI
{
    // A test client's startup request. Only the path to this file is inherited by the game; a password is read from
    // another named environment variable inside the game process and is never written into the file or command line.
    internal sealed class StartupWorldSpec
    {
        internal const string PathVariable = "VALHEIMCLI_START_WORLD_FILE";
        internal const string ClaimedVariable = "VALHEIMCLI_START_WORLD_CLAIMED";

        internal string Mode { get; private set; } = "";
        internal string Character { get; private set; } = "";
        internal string Target { get; private set; } = "";
        internal string? PasswordVariable { get; private set; }
        internal bool PublicServer { get; private set; }
        internal bool Crossplay { get; private set; }
        internal bool Devcommands { get; private set; }

        internal static StartupWorldSpec Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            Dictionary<string, string> fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string raw in text.Split(new[] { '\n' }, StringSplitOptions.None))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                int equals = line.IndexOf('=');
                if (equals < 1 || equals == line.Length - 1 || line.IndexOf('=', equals + 1) >= 0)
                    throw new InvalidDataException("The startup spec needs one key=value per line.");
                string key = line.Substring(0, equals);
                string value = line.Substring(equals + 1);
                if (fields.ContainsKey(key)) throw new InvalidDataException("Duplicate startup spec key: " + key);
                fields.Add(key, value);
            }
            foreach (string key in fields.Keys)
                if (key != "version" && key != "mode" && key != "character" && key != "target" &&
                    key != "passwordVariable" && key != "public" && key != "crossplay" && key != "devcommands")
                    throw new InvalidDataException("Unknown startup spec key: " + key);
            if (!fields.TryGetValue("version", out string? version) || version != "1")
                throw new InvalidDataException("The startup spec requires version=1.");
            if (!fields.TryGetValue("mode", out string? mode) || mode != "join" && mode != "host" && mode != "local")
                throw new InvalidDataException("The startup mode must be join, host or local.");
            if (!fields.TryGetValue("character", out string? character) || !Token(character) || character.Length < 3)
                throw new InvalidDataException("The startup character must be a local filename of at least three characters.");
            if (!fields.TryGetValue("target", out string? target) || !Token(target))
                throw new InvalidDataException("The startup target must be one address or world name.");
            fields.TryGetValue("passwordVariable", out string? passwordVariable);
            if (passwordVariable != null && (!Token(passwordVariable) || !EnvironmentName(passwordVariable)))
                throw new InvalidDataException("passwordVariable must name an environment variable, not contain a password.");
            bool publicServer = Boolean(fields, "public"), crossplay = Boolean(fields, "crossplay"), devcommands = Boolean(fields, "devcommands");
            if (mode == "local" && (passwordVariable != null || publicServer || crossplay) ||
                mode == "join" && (fields.ContainsKey("public") || fields.ContainsKey("crossplay")))
                throw new InvalidDataException("The startup spec has options that do not apply to its mode.");
            return new StartupWorldSpec { Mode = mode, Character = character, Target = target,
                PasswordVariable = passwordVariable, PublicServer = publicServer, Crossplay = crossplay, Devcommands = devcommands };
        }

        private static bool Boolean(Dictionary<string, string> fields, string key)
        {
            if (!fields.TryGetValue(key, out string? value)) return false;
            if (value == "true") return true;
            if (value == "false") return false;
            throw new InvalidDataException(key + " must be true or false.");
        }

        private static bool Token(string value)
        {
            if (value.Length == 0 || value == "." || value == "..") return false;
            foreach (char c in value) if (char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\' || c == '/') return false;
            return true;
        }

        private static bool EnvironmentName(string value)
        {
            if (!(char.IsLetter(value[0]) || value[0] == '_')) return false;
            foreach (char c in value) if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return true;
        }
    }
}
