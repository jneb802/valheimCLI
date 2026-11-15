using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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
    private static readonly ISerializer Writer = new SerializerBuilder().Build();
    private static readonly IDeserializer Reader = new DeserializerBuilder().WithDuplicateKeyChecking().Build();
    private readonly Dictionary<string, BossFile> files = new(StringComparer.OrdinalIgnoreCase);
    private Type? bridge;
    private float nextPoll;

    internal static void Register(GameObject host)
    {
        BossBalanceCommands controller = host.AddComponent<BossBalanceCommands>();
        new Terminal.ConsoleCommand("cli_boss_balance", "Development tuning: cli_boss_balance export|reload|reset|status|watch|stop|heal [eikthyr|elder|bonemass|moder|yagluth|queen|fader]. Use export all to create missing files. Omitted boss means eikthyr.", args => controller.Command(args));
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
            if (action == "export" && boss == "all")
            {
                foreach (BossFile selected in files.Values)
                {
                    if (File.Exists(selected.Path)) { args.Context.AddString("Preserved existing file: " + selected.Path); continue; }
                    Save(selected, (Dictionary<string, Dictionary<string, float>>)Call("Export", selected.Name));
                    selected.Watching = true;
                    args.Context.AddString("Created: " + selected.Path);
                }
                return;
            }
            if (!files.TryGetValue(boss, out file)) throw new ArgumentException("Supported bosses: " + string.Join(", ", files.Keys));
            switch (action)
            {
                case "export":
                    if (File.Exists(file.Path)) throw new InvalidOperationException("File exists; export does not overwrite your edits. Use reload or reset.");
                    Save(file, (Dictionary<string, Dictionary<string, float>>)Call("Export", boss));
                    file.Watching = true;
                    break;
                case "reload":
                    string yaml = ReadFile(file);
                    file.Attempted = Hash(yaml);
                    Apply(file, yaml, file.Attempted);
                    file.Watching = true;
                    break;
                case "reset":
                    Call("Reset", boss);
                    Save(file, (Dictionary<string, Dictionary<string, float>>)Call("Export", boss));
                    file.Watching = true;
                    break;
                case "watch": file.Watching = true; file.Attempted = ""; break;
                case "stop": file.Watching = false; break;
                case "heal": args.Context.AddString((string)Call("Heal", boss)); break;
                case "status": break;
                default: throw new ArgumentException("Use export, reload, reset, status, watch, stop or heal.");
            }
            args.Context.AddString((string)Call("Status", boss));
            args.Context.AddString("YAML: " + file.Path);
            args.Context.AddString("Watching: " + file.Watching + "; applied SHA256: " + file.Applied + (file.Error.Length == 0 ? "" : "; last error: " + file.Error));
        }
        catch (Exception error)
        {
            if (file != null) file.Error = Explain(error);
            args.Context.AddString("Boss balance: " + Explain(error));
        }
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
                if (message != file.Error) valheimCLIPlugin.Log.LogWarning(file.Name + " balance: " + message + ". Previous valid values remain active.");
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

    private static string ReadFile(BossFile file)
    {
        if (new FileInfo(file.Path).Length > 1024 * 1024) throw new InvalidOperationException("Balance YAML exceeds 1 MiB.");
        return File.ReadAllText(file.Path);
    }

    private static void Save(BossFile file, Dictionary<string, Dictionary<string, float>> values)
    {
        string yaml = "# " + file.Name + " development balance. Save to apply; invalid edits keep the last valid values.\n" +
            "# Health is base health. Damage fields are per hit; m_hitInterval is seconds between hits.\n" +
            "# m_aiAttackInterval is weapon cooldown. m_ttl is effect lifetime; m_aoe is projectile blast radius.\n" +
            "# Existing hazards, nests and scheduled cooldowns finish with their previous values.\n" +
            "# Scripted Moder ground-fire damage and interval change on the next tick.\n" +
            "# Indexed settings (such as TickBrackets/0) edit existing phase entries; keep the exported keys.\n" +
            "# Elder nest count/placement changes apply to the next placement; curse changes apply to the next cast.\n" +
            "# Elder thrown-log damage uses physical collision; its flight settings are under abilities.\n" +
            "# cli_boss_balance reset " + file.Name + " restores defaults; heal " + file.Name + " refills this boss's health.\n" + Writer.Serialize(values);
        Directory.CreateDirectory(Path.GetDirectoryName(file.Path)!);
        File.WriteAllText(file.Path, yaml);
        file.Attempted = file.Candidate = Hash(yaml); file.Applied = file.Attempted; file.Error = "";
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
