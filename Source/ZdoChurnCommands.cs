using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace valheimCLI;

/// <summary>Read-only network diagnostics: which saved objects change most often, and therefore resend ZDOData.</summary>
internal static class ZdoChurnCommands
{
    private static Dictionary<ZDOID, uint>? _snapshot;
    private static float _snapshotTime;

    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_zdo_churn", "Find objects whose data changes often: cli_zdo_churn start | report [top=15] [radius=0 for all]", args =>
        {
            if (ZDOMan.instance == null) { args.Context.AddString("ERROR: No world loaded"); return; }
            string mode = args.Length > 1 ? args[1] : "";
            if (mode == "start")
            {
                _snapshot = ZDOMan.instance.m_objectsByID.ToDictionary(pair => pair.Key, pair => pair.Value.DataRevision);
                _snapshotTime = Time.realtimeSinceStartup;
                args.Context.AddString($"OK: snapshot objects={_snapshot.Count}");
                return;
            }
            if (mode != "report" || _snapshot == null) { args.Context.AddString("ERROR: cli_zdo_churn start, then cli_zdo_churn report [top] [radius]"); return; }
            int top = args.Length > 2 && int.TryParse(args[2], out int parsedTop) ? parsedTop : 15;
            float radius = args.Length > 3 && float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedRadius) ? parsedRadius : 0f;
            Vector3 origin = Player.m_localPlayer != null ? Player.m_localPlayer.transform.position : Vector3.zero;
            float seconds = Math.Max(0.001f, Time.realtimeSinceStartup - _snapshotTime);
            Dictionary<int, (int objects, long revisions, Vector3 sample)> byPrefab = new();
            int changed = 0, added = 0;
            foreach (KeyValuePair<ZDOID, ZDO> pair in ZDOMan.instance.m_objectsByID)
            {
                ZDO zdo = pair.Value;
                if (radius > 0 && Utils.DistanceXZ(zdo.GetPosition(), origin) > radius) continue;
                long delta;
                if (_snapshot.TryGetValue(pair.Key, out uint before)) delta = (long)zdo.DataRevision - before;
                else { delta = zdo.DataRevision; added++; }
                if (delta <= 0) continue;
                changed++;
                byPrefab.TryGetValue(zdo.GetPrefab(), out (int objects, long revisions, Vector3 sample) entry);
                byPrefab[zdo.GetPrefab()] = (entry.objects + 1, entry.revisions + delta, entry.objects == 0 ? zdo.GetPosition() : entry.sample);
            }
            args.Context.AddString(FormattableString.Invariant($"OK: seconds={seconds:F1} changedObjects={changed} newObjects={added} radius={(radius > 0 ? radius.ToString("F0", CultureInfo.InvariantCulture) : "all")}"));
            foreach (KeyValuePair<int, (int objects, long revisions, Vector3 sample)> entry in byPrefab.OrderByDescending(item => item.Value.revisions).Take(top))
            {
                GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(entry.Key) : null!;
                string name = prefab != null ? prefab.name : entry.Key.ToString(CultureInfo.InvariantCulture);
                args.Context.AddString(FormattableString.Invariant($"prefab={name} objects={entry.Value.objects} revisions={entry.Value.revisions} perSecond={entry.Value.revisions / seconds:F1} sample={entry.Value.sample.ToString("F0")}"));
            }
        }, isCheat: true);
    }
}
