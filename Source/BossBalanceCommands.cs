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

// The optional mod bridge is compiled only into Endgame development builds.
// Polling and all bridge calls run on the Unity thread, including after atomic saves.
internal sealed class BossBalanceCommands : MonoBehaviour
{
    private static readonly string FilePath = Path.Combine(BepInEx.Paths.ConfigPath, "ValheimEndgame.boss-balance.dev.yaml");
    private static readonly ISerializer Writer = new SerializerBuilder().Build();
    private static readonly IDeserializer Reader = new DeserializerBuilder().WithDuplicateKeyChecking().Build();
    private Type? bridge;
    private bool watching;
    private string attempted = "";
    private string applied = "none";
    private string candidate = "";
    private string lastError = "";
    private float nextPoll;

    internal static void Register(GameObject host)
    {
        BossBalanceCommands controller = host.AddComponent<BossBalanceCommands>();
        new Terminal.ConsoleCommand("cli_boss_balance", "Development Eikthyr tuning: cli_boss_balance export|reload|reset|status|watch|stop|heal", args =>
        {
            try
            {
                controller.Resolve();
                string action = args.Length > 1 ? args[1].ToLowerInvariant() : "status";
                switch (action)
                {
                    case "export":
                        if (File.Exists(FilePath)) throw new InvalidOperationException("File already exists; export does not overwrite your edits. Use reload or reset.");
                        controller.Save((Dictionary<string, Dictionary<string, float>>)controller.Call("Export"));
                        controller.watching = true;
                        break;
                    case "reload": controller.Reload(); controller.watching = true; break;
                    case "reset":
                        controller.Call("Reset");
                        controller.Save((Dictionary<string, Dictionary<string, float>>)controller.Call("Export"));
                        controller.watching = true;
                        break;
                    case "watch": controller.watching = true; controller.attempted = ""; break;
                    case "stop": controller.watching = false; break;
                    case "heal":
                        if (!(bool)controller.Call("IsReady")) throw new InvalidOperationException("Enter a local world with no peers first.");
                        foreach (Character character in Character.GetAllCharacters())
                            if (Utils.GetPrefabName(character.gameObject) == "VE_UberEikthyr" && character.IsOwner() && !character.IsDead()) character.SetHealth(character.GetMaxHealth());
                        break;
                    case "status": break;
                    default: throw new ArgumentException("Use export, reload, reset, status, watch, stop or heal.");
                }
                args.Context.AddString(controller.Call("Status") + "\nYAML: " + FilePath + "\nWatching: " + controller.watching + "; applied SHA256: " + controller.applied + (controller.lastError.Length == 0 ? "" : "; last error: " + controller.lastError));
            }
            catch (Exception error) { args.Context.AddString("Boss balance: " + Explain(error)); }
        });
    }

    private void Resolve()
    {
        bridge ??= AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("ValheimEndgame.BossBalanceDev")).FirstOrDefault(type => type != null);
        if (bridge == null) throw new InvalidOperationException("Install Endgame built with -p:BossBalanceDev=true and restart once. Normal releases do not support this tool.");
    }

    private object Call(string name, params object[] args) => bridge!.GetMethod(name, BindingFlags.Public | BindingFlags.Static)!.Invoke(null, args)!;

    private void Start() { watching = File.Exists(FilePath); }

    private void Update()
    {
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + 0.5f;
        try
        {
            if (bridge == null)
            {
                bridge = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("ValheimEndgame.BossBalanceDev")).FirstOrDefault(type => type != null);
                if (bridge == null) return;
            }
            if (!(bool)Call("IsReady")) { attempted = ""; candidate = ""; return; }
            Call("Tick");
            if (!watching || !File.Exists(FilePath)) return;
            string yaml = ReadFile();
            string hash = Hash(yaml);
            if (hash == attempted) return;
            // Require two identical reads so an editor's partial write is not applied.
            if (candidate != hash) { candidate = hash; return; }
            attempted = hash;
            Apply(yaml, hash);
        }
        catch (Exception error)
        {
            string message = Explain(error);
            if (message != lastError) valheimCLIPlugin.Log.LogWarning("Boss balance: " + message + ". Previous valid values remain active.");
            lastError = message;
        }
    }

    private void Reload()
    {
        string yaml = ReadFile();
        string hash = Hash(yaml);
        attempted = hash;
        Apply(yaml, hash);
    }

    private void Apply(string yaml, string hash)
    {
        Dictionary<string, Dictionary<string, float>> values = Reader.Deserialize<Dictionary<string, Dictionary<string, float>>>(yaml);
        string status = (string)Call("Apply", values);
        applied = hash; lastError = "";
        valheimCLIPlugin.Log.LogInfo(status + " SHA256=" + hash);
        if (Console.instance != null) Console.instance.AddString(status);
    }

    private static string ReadFile()
    {
        if (new FileInfo(FilePath).Length > 1024 * 1024) throw new InvalidOperationException("Balance YAML exceeds 1 MiB.");
        return File.ReadAllText(FilePath);
    }

    private void Save(Dictionary<string, Dictionary<string, float>> values)
    {
        string yaml = "# Local development only. Save to apply; invalid edits keep the last valid values.\n" +
            "# Health is base health. Damage fields are per hit; m_hitInterval is seconds between hits.\n" +
            "# m_ttl is effect lifetime. Some effects use colliders rather than m_radius.\n" +
            "# Existing hazards and scheduled cooldowns finish with their prior values.\n" +
            "# cli_boss_balance reset restores captured defaults; heal restores live Eikthyr health.\n" + Writer.Serialize(values);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, yaml);
        attempted = candidate = Hash(yaml); applied = attempted; lastError = "";
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
