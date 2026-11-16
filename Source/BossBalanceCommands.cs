using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using YamlDotNet.Serialization;

namespace valheimCLI;

// Optional Endgame integration. All file reads and bridge calls run on Unity's
// thread. Each boss has independent file, reload and error state.
internal sealed class BossBalanceCommands : MonoBehaviour
{
    private sealed class BossFile
    {
        internal readonly string Name;
        internal readonly string Path;
        internal bool Watching;
        internal string Attempted = "", Candidate = "", Applied = "none", Error = "";
        internal BossFile(string name)
        {
            Name = name;
            Path = System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "ValheimEndgame.boss-balance." + name + ".dev.yaml");
            Watching = File.Exists(Path);
        }
    }
    private static readonly IDeserializer Reader = new DeserializerBuilder().WithDuplicateKeyChecking().Build();
    private static readonly Regex PlainKey = new(@"^[A-Za-z0-9_][A-Za-z0-9_/.\-*#@]*$");
    private readonly Dictionary<string, BossFile> files = new(StringComparer.OrdinalIgnoreCase);
    private Type? bridge;
    private float nextPoll;

    internal static void Register(GameObject host)
    {
        BossBalanceCommands controller = host.AddComponent<BossBalanceCommands>();
        new Terminal.ConsoleCommand("cli_boss_balance", "Development tuning: cli_boss_balance export|reload|upgrade|reset|status|watch|stop|heal [eikthyr|elder|bonemass|moder|yagluth|queen|fader|all]. " +
            "export all creates missing files; upgrade all rewrites every file in the current layout and keeps your values. Omitted boss means eikthyr.", args => controller.Command(args));
    }

    private void Command(Terminal.ConsoleEventArgs args)
    {
        BossFile? file = null;
        try
        {
            Resolve();
            if (args.Length > 3) throw new ArgumentException("Use cli_boss_balance <action> [boss]. Supported bosses: " + string.Join(", ", files.Keys));
            string boss = args.Length > 2 ? args[2].ToLowerInvariant() : "eikthyr";
            string action = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
            if (boss == "all")
            {
                if (action != "export" && action != "upgrade" && action != "status") throw new ArgumentException("Only export, upgrade and status accept all.");
                foreach (BossFile selected in files.Values)
                {
                    file = selected;
                    if (action == "status")
                    {
                        args.Context.AddString((string)Call("Status", selected.Name) + " Watching: " + selected.Watching +
                            (selected.Error.Length == 0 ? "" : "; last error: " + selected.Error));
                        continue;
                    }
                    if (!File.Exists(selected.Path))
                    {
                        Save(selected, Export(selected.Name));
                        selected.Watching = true;
                        args.Context.AddString("Created: " + selected.Path);
                    }
                    else if (action == "upgrade") args.Context.AddString(Upgrade(selected));
                    else args.Context.AddString("Preserved existing file: " + selected.Path);
                }
                return;
            }
            if (!files.TryGetValue(boss, out file)) throw new ArgumentException("Supported bosses: " + string.Join(", ", files.Keys));
            switch (action)
            {
                case "export":
                    if (File.Exists(file.Path)) throw new InvalidOperationException("File exists; export does not overwrite your edits. Use reload, upgrade or reset.");
                    Save(file, Export(boss));
                    file.Watching = true;
                    break;
                case "reload":
                    string yaml = ReadFile(file);
                    file.Attempted = Hash(yaml);
                    Apply(file, yaml, file.Attempted);
                    file.Watching = true;
                    break;
                case "upgrade":
                    if (!File.Exists(file.Path)) throw new InvalidOperationException("No file to upgrade. Use export.");
                    args.Context.AddString(Upgrade(file));
                    break;
                case "reset":
                    Call("Reset", boss);
                    Save(file, Export(boss));
                    file.Watching = true;
                    break;
                case "watch": file.Watching = true; file.Attempted = ""; break;
                case "stop": file.Watching = false; break;
                case "heal": args.Context.AddString((string)Call("Heal", boss)); break;
                case "status": break;
                default: throw new ArgumentException("Use export, reload, upgrade, reset, status, watch, stop or heal.");
            }
            AddStatus(args.Context, file);
        }
        catch (Exception error)
        {
            if (file != null) file.Error = Explain(error);
            args.Context.AddString("Boss balance: " + Explain(error));
        }
    }

    private void AddStatus(Terminal context, BossFile file)
    {
        context.AddString((string)Call("Status", file.Name));
        context.AddString("YAML: " + file.Path);
        context.AddString("Watching: " + file.Watching + "; applied SHA256: " + file.Applied + (file.Error.Length == 0 ? "" : "; last error: " + file.Error));
    }

    private void Resolve()
    {
        bridge ??= AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("ValheimEndgame.BossBalanceDev")).FirstOrDefault(type => type != null);
        if (bridge == null) throw new InvalidOperationException("Install Endgame built with -p:BossBalanceDev=true and restart once. Normal releases do not support tuning.");
        if (files.Count != 0) return;
        if (bridge.GetMethod("Bosses") == null) throw new InvalidOperationException("Update the Endgame development DLL for per-boss tuning, then restart.");
        foreach (string name in (string[])Call("Bosses")) files.Add(name, new BossFile(name));
        string legacy = Path.Combine(BepInEx.Paths.ConfigPath, "ValheimEndgame.boss-balance.dev.yaml");
        if (files.TryGetValue("eikthyr", out BossFile eikthyr) && File.Exists(legacy))
        {
            if (!File.Exists(eikthyr.Path))
            {
                File.Move(legacy, eikthyr.Path);
                eikthyr.Watching = true;
                valheimCLIPlugin.Log.LogInfo("Moved the existing Eikthyr balance file to " + eikthyr.Path + " without changing its contents.");
            }
            else valheimCLIPlugin.Log.LogWarning("Both old and per-boss Eikthyr YAML files exist. Using " + eikthyr.Path + "; the old file is preserved and ignored.");
        }
    }

    private object Call(string name, params object[] args) => bridge!.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, args)!;
    private T Optional<T>(string name, string boss) where T : class, new() =>
        bridge!.GetMethod(name, BindingFlags.Public | BindingFlags.Static) is MethodInfo method ? (T)method.Invoke(null, new object[] { boss }) : new T();
    private Dictionary<string, Dictionary<string, float>> Export(string boss) => (Dictionary<string, Dictionary<string, float>>)Call("Export", boss);

    private void Update()
    {
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + 0.5f;
        if (bridge == null && !AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetType("ValheimEndgame.BossBalanceDev") != null)) return;
        try
        {
            Resolve();
            if (!(bool)Call("IsReady"))
            {
                foreach (BossFile file in files.Values) { file.Attempted = ""; file.Candidate = ""; file.Applied = "none in this world"; }
                return;
            }
            Call("Tick");
        }
        catch (Exception error)
        {
            nextPoll = Time.unscaledTime + 10;
            valheimCLIPlugin.Log.LogWarning("Boss balance: " + Explain(error));
            return;
        }
        foreach (BossFile file in files.Values)
        {
            if (!file.Watching || !File.Exists(file.Path)) continue;
            try
            {
                string yaml = ReadFile(file);
                string hash = Hash(yaml);
                if (hash == file.Attempted) continue;
                if (file.Candidate != hash) { file.Candidate = hash; continue; }
                file.Attempted = hash;
                Apply(file, yaml, hash);
            }
            catch (Exception error)
            {
                string message = Explain(error);
                if (message != file.Error)
                {
                    valheimCLIPlugin.Log.LogWarning(file.Name + " balance: " + message.TrimEnd('.') + ". Previous valid values remain active.");
                    if (Console.instance != null) Console.instance.AddString(file.Name + " balance: " + message);
                }
                file.Error = message;
            }
        }
    }

    private void Apply(BossFile file, string yaml, string hash)
    {
        Dictionary<string, Dictionary<string, float>> values = Reader.Deserialize<Dictionary<string, Dictionary<string, float>>>(yaml);
        string status = (string)Call("Apply", file.Name, values);
        file.Applied = hash; file.Error = "";
        valheimCLIPlugin.Log.LogInfo(status + " SHA256=" + hash);
        if (Console.instance != null) Console.instance.AddString(status);
    }

    /// <summary>
    /// Rewrites a file in the current layout. Every value the file holds under the same or an
    /// earlier section name carries over; new entries take the current game values. The old file
    /// is kept beside it with a time-stamped .bak name.
    /// </summary>
    private string Upgrade(BossFile file)
    {
        Dictionary<string, Dictionary<string, float>> old = Reader.Deserialize<Dictionary<string, Dictionary<string, float>>>(ReadFile(file))
            ?? new Dictionary<string, Dictionary<string, float>>();
        Dictionary<string, Dictionary<string, float>> current = Export(file.Name);
        Dictionary<string, string[]> aliases = Optional<Dictionary<string, string[]>>("Aliases", file.Name);
        HashSet<string> used = new();
        List<string> added = new(), differing = new();
        int carried = 0;
        Dictionary<string, Dictionary<string, float>> result = new();
        foreach (KeyValuePair<string, Dictionary<string, float>> section in current)
        {
            string[] candidates = new[] { section.Key }.Concat(aliases.TryGetValue(section.Key, out string[] names) ? names : Array.Empty<string>())
                .Distinct().Where(old.ContainsKey).ToArray();
            Dictionary<string, float> fields = result[section.Key] = new Dictionary<string, float>();
            foreach (KeyValuePair<string, float> field in section.Value)
            {
                string[] holders = candidates.Where(name => old[name] != null && old[name].ContainsKey(field.Key)).ToArray();
                if (holders.Length == 0) { fields[field.Key] = field.Value; added.Add(section.Key + "/" + field.Key); continue; }
                fields[field.Key] = old[holders[0]][field.Key];
                carried++;
                foreach (string holder in holders) used.Add(holder + "\n" + field.Key);
                if (holders.Any(holder => old[holder][field.Key] != fields[field.Key])) differing.Add(section.Key + "/" + field.Key);
            }
        }
        string[] dropped = old.Where(section => section.Value != null).SelectMany(section => section.Value.Keys.Select(key => section.Key + "\n" + key))
            .Where(key => !used.Contains(key)).Select(key => key.Replace("\n", "/")).ToArray();
        string backup = file.Path + "." + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".bak";
        File.Copy(file.Path, backup);
        Save(file, result);
        file.Watching = true;
        string applied;
        try { applied = (string)Call("Apply", file.Name, result); }
        catch (Exception error) { file.Error = Explain(error); applied = "Not applied: " + file.Error + ". Fix the value in the file and save."; }
        StringBuilder report = new();
        report.Append(file.Name + " upgraded: " + carried + " values kept, " + added.Count + " new entries at current game values, " + dropped.Length + " old entries removed. Backup: " + backup);
        if (differing.Count > 0) report.Append(" Parts now edited together had different values; the first was kept for: " + string.Join(", ", differing.Take(10)) + (differing.Count > 10 ? " ..." : "") + ".");
        report.Append(" " + applied);
        valheimCLIPlugin.Log.LogInfo(report.ToString() + (dropped.Length == 0 ? "" : "\nRemoved entries: " + string.Join(", ", dropped)));
        return report.ToString();
    }

    private static string ReadFile(BossFile file)
    {
        if (new FileInfo(file.Path).Length > 1024 * 1024) throw new InvalidOperationException("Balance YAML exceeds 1 MiB.");
        return File.ReadAllText(file.Path);
    }

    private void Save(BossFile file, Dictionary<string, Dictionary<string, float>> values)
    {
        Dictionary<string, string> notes = Optional<Dictionary<string, string>>("Describe", file.Name);
        StringBuilder yaml = new();
        Comment(yaml, "", file.Name + " development balance. Save the file to apply it; an invalid edit is reported in the console and the last valid values stay active.");
        Comment(yaml, "", "Damage is per hit, before star scaling, armour and resistances. Damage types: m_damage (true damage), m_blunt, m_slash, m_pierce, " +
            "m_chop and m_pickaxe (only against trees and rocks), m_fire, m_frost, m_lightning, m_poison (total over its duration), m_spirit.");
        Comment(yaml, "", "Weapon and attack changes apply to the next attack. Existing hazards, nests and scheduled cooldowns finish with their previous values.");
        Comment(yaml, "", "cli_boss_balance reset " + file.Name + " restores defaults; heal " + file.Name + " refills the boss; upgrade " + file.Name + " rewrites an older file and keeps its values.");
        if (notes.TryGetValue("", out string missing)) Comment(yaml, "", missing);
        foreach (KeyValuePair<string, Dictionary<string, float>> section in values)
        {
            yaml.Append('\n');
            if (notes.TryGetValue(section.Key, out string note)) Comment(yaml, "", note);
            yaml.Append(Key(section.Key)).Append(":\n");
            foreach (KeyValuePair<string, float> field in section.Value)
                yaml.Append("  ").Append(Key(field.Key)).Append(": ").Append(field.Value.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
        }
        string text = yaml.ToString();
        Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
        File.WriteAllText(file.Path, text);
        file.Attempted = file.Candidate = Hash(text); file.Applied = file.Attempted; file.Error = "";
    }

    private static string Key(string key) => PlainKey.IsMatch(key) ? key : "'" + key.Replace("'", "''") + "'";

    private static void Comment(StringBuilder yaml, string indent, string text)
    {
        StringBuilder line = new();
        foreach (string word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + word.Length > 110) { yaml.Append(indent).Append("# ").Append(line).Append('\n'); line.Clear(); }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yaml.Append(indent).Append("# ").Append(line).Append('\n');
    }

    private static string Hash(string value)
    {
        using SHA256 hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
    }
    private static string Explain(Exception error)
    {
        while (error.InnerException != null) error = error.InnerException;
        return error.Message;
    }
}
