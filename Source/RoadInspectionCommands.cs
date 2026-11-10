using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace valheimCLI;

/// <summary>Optional road integrations for live inspection. These commands do not alter road or travel state.</summary>
internal static class RoadInspectionCommands
{
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private static Type? Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);
    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_ground_surface", "Read loaded ground height, paint, terrain compiler and an optional metadata field: <x> <z> [metadata-key]", args =>
        {
            if (args.Length < 3 || !float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
                float.IsNaN(x) || float.IsNaN(z) || Mathf.Abs(x) > 10000 || Mathf.Abs(z) > 10000)
            { args.Context.AddString("ERROR: cli_ground_surface <x> <z> [metadata-key]"); return; }
            Vector3 point = new(x, 0, z);
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetGroundHeight(point, out float height))
            { args.Context.AddString("ERROR: Terrain is not loaded"); return; }
            point.y = height;
            Heightmap heightmap = Heightmap.FindHeightmap(point);
            TerrainComp compiler = TerrainComp.FindTerrainCompiler(point);
            ZDO? data = compiler != null ? compiler.GetComponent<ZNetView>()?.GetZDO() : null;
            args.Context.AddString("position=" + point.ToString("F3") + " paint=" + heightmap.GetPaintMask(point).ToString("F3") + " terrain=" + (data?.m_uid.ToString() ?? "none"));
            if (args.Length > 3) args.Context.AddString(args[3] + "=" + (data?.GetString(args[3]) ?? ""));
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_road_network", "Read supported road geometry: cli_road_network [route-index] [point-step=100]", args =>
        {
            Type? api = Find("ProceduralRoads.RoadTravelApi");
            if (api == null) { args.Context.AddString("ERROR: Road travel API is absent"); return; }
            object[] roads = ((IEnumerable)api.GetMethod("GetRoutes")!.Invoke(null, null)).Cast<object>().ToArray();
            bool worldMatches = ZNet.instance != null && (long)api.GetField("WorldId", Static)!.GetValue(null) == ZNet.instance.GetWorldUID();
            args.Context.AddString("ready=" + api.GetProperty("Ready")!.GetValue(null) + " revision=" + api.GetProperty("Revision")!.GetValue(null) + " routes=" + roads.Length + " worldMatches=" + worldMatches);
            if (args.Length > 1 && int.TryParse(args[1], out int index) && index >= 0 && index < roads.Length)
            {
                int step = args.Length > 2 && int.TryParse(args[2], out int n) ? Math.Max(1, n) : 100;
                object road = roads[index]; Type type = road.GetType();
                args.Context.AddString("route=" + index + " id=" + type.GetProperty("Id")!.GetValue(road) + " start=" + type.GetProperty("StartCenter")!.GetValue(road) + " end=" + type.GetProperty("EndCenter")!.GetValue(road));
                Vector3[] points = ((IEnumerable)type.GetProperty("Points")!.GetValue(road)).Cast<Vector3>().ToArray();
                for (int i = 0; i < points.Length; i += step) args.Context.AddString("point=" + i + " position=" + points[i].ToString("F3"));
            }
            else
            {
                for (int i = 0; i < roads.Length; i++)
                {
                    object road = roads[i]; Type type = road.GetType();
                    args.Context.AddString("route=" + i + " id=" + type.GetProperty("Id")!.GetValue(road) + " start=" + type.GetProperty("StartCenter")!.GetValue(road) + " end=" + type.GetProperty("EndCenter")!.GetValue(road));
                }
            }
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_road_travel_status", "Read optional PraetorisClient road travel state and server-approved menu entries", args =>
        {
            Type? service = Find("PraetorisClient.RoadTravel.RoadTravelService");
            if (service == null) { args.Context.AddString("ERROR: PraetorisClient road travel is absent"); return; }
            args.Context.AddString("ready=" + service.GetProperty("Ready", Static)!.GetValue(null) + " status=" + service.GetProperty("Status", Static)!.GetValue(null) + " result=" + service.GetProperty("LastResult", Static)!.GetValue(null));
            IEnumerable? entries = service.GetProperty("Destinations", Static)!.GetValue(null) as IEnumerable;
            if (entries == null) return;
            int index = 0;
            foreach (object entry in entries)
            {
                Type type = entry.GetType();
                args.Context.AddString("index=" + index++ + " id=" + type.GetField("Id", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(entry) +
                    " name=" + type.GetField("Name", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(entry) +
                    " price=" + type.GetField("Price", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(entry));
            }
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_road_port_access", "Read each known port's actual road connection and guide position", args =>
        {
            Type? bridge = Find("PraetorisClient.RoadTravel.RoadTravelBridge"), service = Find("PraetorisClient.RoadTravel.RoadTravelService");
            if (bridge == null || service == null || !(bool)bridge.GetProperty("Available", Static)!.GetValue(null))
            { args.Context.AddString("ERROR: Road travel integration unavailable"); return; }
            Delegate getPorts = (Delegate)bridge.GetField("Ports", Static)!.GetValue(null);
            foreach (ZDO port in (IEnumerable)getPorts.DynamicInvoke())
            {
                object?[] parameters = { port.m_uid, true, null };
                bool resolved = (bool)service.GetMethod("TryResolve", Static)!.Invoke(null, parameters);
                object destination = parameters[2]!; Type type = destination.GetType();
                args.Context.AddString("port=" + port.GetString("PortName") + " id=" + port.m_uid + " connected=" + resolved +
                    " trader=" + port.GetPosition().ToString("F3") + (resolved ? " guide=" + ((Vector3)type.GetField("Position", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(destination)).ToString("F3") +
                    " route=" + type.GetField("Route", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(destination) : ""));
            }
        }, isCheat: true);
    }
}
