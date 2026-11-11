using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace valheimCLI;

internal static class ZdoLifecycleCommands
{
    private const int Limit = 64;
    private static readonly Harmony TraceHarmony = new("valheimCLI.ZdoLifecycleTrace");
    private static readonly Dictionary<ZDOID, Entry> Entries = new();
    private static readonly Queue<ZDOID> Order = new();
    private static int hash;
    private static string prefab = "";
    private static bool active, wrapped;
    private static int created, assigned;
    private static MethodInfo? createMethod;
    private static readonly MethodInfo SetMethod = AccessTools.Method(typeof(ZDO), nameof(ZDO.SetPrefab));
    private static readonly MethodInfo AwakeMethod = AccessTools.Method(typeof(ZNetView), nameof(ZNetView.Awake));

    private sealed class Entry
    {
        internal ZDOID Id;
        internal bool Created, Wrapped;
        internal int Before, Calls, LastBefore, LastAfter;
    }

    internal static void Register() => new Terminal.ConsoleCommand("cli_zdo_trace",
        "Trace prefab creation callbacks: cli_zdo_trace start <prefab> | report | clear | wrap-view | stop. wrap-view explicitly recompiles the view initialization through a diagnostic Harmony wrapper.", args =>
        {
            if (args.Length < 2) { args.Context.AddString("ERROR: Specify start, report, clear, wrap-view, or stop"); return; }
            try
            {
                switch (args[1])
                {
                    case "start":
                        if (active || args.Length != 3) { args.Context.AddString("ERROR: Stop an existing trace before starting a prefab trace"); return; }
                        Start(args[2]);
                        args.Context.AddString("TRACE active=True prefab=" + prefab + " limit=" + Limit);
                        break;
                    case "report": Report(args.Context.AddString); break;
                    case "clear": Clear(); args.Context.AddString("TRACE cleared"); break;
                    case "wrap-view":
                        if (!active || wrapped) { args.Context.AddString("ERROR: An active unwrapped trace is required"); return; }
                        TraceHarmony.Patch(AwakeMethod, postfix: Hook(nameof(ViewPostfix)));
                        wrapped = true;
                        args.Context.AddString("TRACE viewWrapper=True; subsequent creation uses the rebuilt Awake method");
                        break;
                    case "stop": Stop(); args.Context.AddString("TRACE active=False patches=" + OwnPatchCount()); break;
                    default: args.Context.AddString("ERROR: Unknown trace operation"); break;
                }
            }
            catch (Exception exception)
            {
                Stop();
                args.Context.AddString("ERROR: Trace stopped after " + exception.GetType().Name + ": " + exception.Message);
            }
        }, isCheat: true);

    private static HarmonyMethod Hook(string name) => new(AccessTools.Method(typeof(ZdoLifecycleCommands), name));

    private static void Start(string name)
    {
        createMethod = AccessTools.Method(typeof(ZDOMan), "CreateNewZDO", new[] { typeof(Vector3), typeof(int) });
        if (createMethod == null) throw new MissingMethodException("ZDOMan.CreateNewZDO(Vector3, int)");
        prefab = name; hash = name.GetStableHashCode(); wrapped = false; Clear();
        try
        {
            TraceHarmony.Patch(createMethod, postfix: Hook(nameof(CreatePostfix)));
            TraceHarmony.Patch(SetMethod, prefix: Hook(nameof(SetPrefix)), postfix: Hook(nameof(SetPostfix)));
            active = true;
        }
        catch { Stop(); throw; }
    }

    internal static void Stop()
    {
        active = false;
        TraceHarmony.UnpatchSelf();
        wrapped = false;
        Clear();
    }

    private static void Clear() { Entries.Clear(); Order.Clear(); created = assigned = 0; }

    private static Entry Get(ZDO data)
    {
        if (Entries.TryGetValue(data.m_uid, out Entry entry)) return entry;
        if (Order.Count == Limit) Entries.Remove(Order.Dequeue());
        entry = new Entry { Id = data.m_uid, Wrapped = wrapped };
        Entries.Add(entry.Id, entry); Order.Enqueue(entry.Id);
        return entry;
    }

    private static void CreatePostfix(ZDO __result, int __1)
    {
        if (!active || __result == null || __1 != hash) return;
        Entry entry = Get(__result);
        entry.Created = true; entry.Before = __result.GetPrefab(); created++;
    }

    private static void SetPrefix(ZDO __instance, out int __state) => __state = __instance.GetPrefab();

    private static void SetPostfix(ZDO __instance, int __state)
    {
        if (!active || (__instance.GetPrefab() != hash && !Entries.ContainsKey(__instance.m_uid))) return;
        Entry entry = Get(__instance);
        entry.Calls++; entry.LastBefore = __state; entry.LastAfter = __instance.GetPrefab(); assigned++;
    }

    private static void ViewPostfix() { }

    private static string Membership(string typeName, string fieldName, ZDOID id)
    {
        Type? type = AccessTools.TypeByName(typeName);
        IDictionary? records = type == null ? null : AccessTools.Field(type, fieldName)?.GetValue(null) as IDictionary;
        return records == null ? "unavailable" : records.Contains(id).ToString();
    }

    private static void Report(Action<string> output)
    {
        output("TRACE active=" + active + " prefab=" + prefab + " hash=" + hash + " viewWrapper=" + wrapped +
            " created=" + created + " setPrefabCalls=" + assigned + " retained=" + Entries.Count + " ownPatches=" + OwnPatchCount());
        foreach (ZDOID id in Order)
        {
            Entry entry = Entries[id];
            ZDO? current = ZDOMan.instance?.GetZDO(id);
            output("id=" + id + " created=" + entry.Created + " wrapped=" + entry.Wrapped + " prefabAtCreateReturn=" + entry.Before +
                " currentPrefab=" + (current == null ? "absent" : current.GetPrefab().ToString()) + " setCalls=" + entry.Calls +
                " lastSet=" + entry.LastBefore + "->" + entry.LastAfter +
                " communityIndex=" + Membership("ValheimCommunityPatch.Patches.Performance.ZdoPrefabIndexPatch", "PrefabOf", id) +
                " roadPortRegistry=" + Membership("More_World_Locations_AIO.RoadTravelPorts", "known", id));
        }
        foreach (MethodInfo method in new[] { SetMethod, AwakeMethod })
        {
            Patches? patches = Harmony.GetPatchInfo(method);
            output("method=" + method.DeclaringType!.FullName + "." + method.Name + " implementation=" + method.GetMethodImplementationFlags());
            if (patches == null) continue;
            foreach (Patch patch in patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Transpilers).Concat(patches.Finalizers))
                output(" patch owner=" + patch.owner + " callback=" + patch.PatchMethod.DeclaringType!.FullName + "." + patch.PatchMethod.Name +
                    " transpiler=" + patches.Transpilers.Contains(patch));
        }
    }

    private static int OwnPatchCount() => Harmony.GetAllPatchedMethods()
        .Count(method => Harmony.GetPatchInfo(method)?.Owners.Contains(TraceHarmony.Id) == true);
}
