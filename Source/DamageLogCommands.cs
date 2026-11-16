using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace valheimCLI;

/// <summary>
/// Records hits the local player receives as they arrive, before difficulty scaling, armour,
/// resistances, blocking and god mode change them. That is the damage creature attacks and
/// tuning files set, so a tuned value can be checked against a real hit.
/// </summary>
internal static class DamageLogCommands
{
    private sealed class Entry
    {
        internal float Time;
        internal string Attacker = "";
        internal string Types = "";
        internal float Total;
        internal bool Blockable, Dodgeable;
    }

    private const int Capacity = 200;
    private static readonly List<Entry> entries = new();
    private static bool recording, echo;

    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_damage_log",
            "Record hits the local player receives, before armour, resistances and god mode: cli_damage_log on|echo|off|clear|show [count=20]. " +
            "echo also prints each hit to the console as it lands.", Command, isCheat: true);
    }

    private static void Command(Terminal.ConsoleEventArgs args)
    {
        string action = args.Length > 1 ? args[1].ToLowerInvariant() : "show";
        switch (action)
        {
            case "on": recording = true; echo = false; args.Context.AddString("OK: recording hits"); return;
            case "echo": recording = true; echo = true; args.Context.AddString("OK: recording hits and printing each one"); return;
            case "off": recording = false; echo = false; args.Context.AddString("OK: stopped; " + entries.Count + " hits kept"); return;
            case "clear": entries.Clear(); args.Context.AddString("OK: cleared"); return;
            case "show":
                int count = args.Length > 2 && int.TryParse(args[2], out int parsed) ? Math.Max(1, parsed) : 20;
                args.Context.AddString("hits=" + entries.Count + " recording=" + recording);
                foreach (Entry entry in entries.Skip(Math.Max(0, entries.Count - count))) args.Context.AddString(Format(entry));
                return;
            default: args.Context.AddString("ERROR: Use on, echo, off, clear or show [count]"); return;
        }
    }

    private static string Format(Entry entry) =>
        "t=" + entry.Time.ToString("F2", CultureInfo.InvariantCulture) + " attacker=" + entry.Attacker + " total=" + entry.Total.ToString("0.##", CultureInfo.InvariantCulture) +
        " " + entry.Types + " blockable=" + entry.Blockable + " dodgeable=" + entry.Dodgeable;

    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    private static class RecordHits
    {
        private static void Prefix(Character __instance, HitData hit) => Record(__instance, hit);
    }

    private static void Record(Character __instance, HitData hit)
    {
        if (!recording || hit == null || __instance != Player.m_localPlayer || !__instance.m_nview.IsOwner()) return;
        Character attacker = hit.GetAttacker();
        Entry entry = new()
        {
            Time = Time.time,
            Attacker = attacker == null ? "none" : Utils.GetPrefabName(attacker.gameObject),
            Types = string.Join(" ", typeof(HitData.DamageTypes).GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => field.FieldType == typeof(float) && (float)field.GetValue(hit.m_damage) != 0)
                .Select(field => field.Name.Substring(2) + "=" + ((float)field.GetValue(hit.m_damage)).ToString("0.##", CultureInfo.InvariantCulture))),
            Total = hit.GetTotalDamage(),
            Blockable = hit.m_blockable,
            Dodgeable = hit.m_dodgeable
        };
        entries.Add(entry);
        if (entries.Count > Capacity) entries.RemoveAt(0);
        if (echo && Console.instance != null) Console.instance.AddString("hit: " + Format(entry));
    }
}
