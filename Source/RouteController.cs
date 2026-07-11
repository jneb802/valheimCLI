using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace valheimCLI
{
    /// <summary>
    /// Scripted player movement: plan a waypoint route with a gait per leg,
    /// then execute it. The runner steers the local player through
    /// Player.SetControls every frame, the same input path a human uses.
    /// </summary>
    public static class RouteController
    {
        public enum Gait
        {
            Walk,
            Run,
            Sprint,
        }

        public struct Waypoint
        {
            public Vector3 Position;
            public Gait Gait;
        }

        private const float ArrivalRadius = 1.3f;
        private const float RouteLookAheadDistance = 7f;
        private const float WalkTurnRateDegreesPerSecond = 80f;
        private const float RunTurnRateDegreesPerSecond = 140f;
        private const float SprintTurnRateDegreesPerSecond = 190f;

        private static readonly List<Waypoint> Route = new();
        private static Coroutine? _active;
        private static int _currentIndex = -1;
        private static string _lastResult = "idle";
        private static PlayerController? _disabledController;

        public static bool IsRunning => _active != null;

        internal static void Register()
        {
            _ = new Terminal.ConsoleCommand("cli_route_add", "Append a waypoint: cli_route_add <x> <y> <z> [walk|run|sprint]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (args.Length < 4 ||
                    !TryParseFloat(args[1], out float x) ||
                    !TryParseFloat(args[2], out float y) ||
                    !TryParseFloat(args[3], out float z))
                {
                    args.Context.AddString("Usage: cli_route_add <x> <y> <z> [walk|run|sprint]");
                    return;
                }

                Gait gait = Gait.Run;
                if (args.Length >= 5 && !TryParseGait(args[4], out gait))
                {
                    args.Context.AddString($"ERROR: Unknown gait '{args[4]}'. Use walk, run, or sprint.");
                    return;
                }

                Route.Add(new Waypoint { Position = new Vector3(x, y, z), Gait = gait });
                args.Context.AddString($"OK: waypoint {Route.Count - 1} added at {x:F1},{y:F1},{z:F1} gait={gait.ToString().ToLowerInvariant()} (route has {Route.Count})");
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_route_clear", "Remove all route waypoints", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                Stop(_ => { });
                Route.Clear();
                _lastResult = "idle";
                args.Context.AddString("OK: route cleared");
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_route_list", "List route waypoints", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (Route.Count == 0)
                {
                    args.Context.AddString("OK: route is empty");
                    return;
                }

                StringBuilder sb = new();
                for (int i = 0; i < Route.Count; i++)
                {
                    Waypoint wp = Route[i];
                    sb.AppendLine($"WAYPOINT {i} pos=({wp.Position.x:F1},{wp.Position.y:F1},{wp.Position.z:F1}) gait={wp.Gait.ToString().ToLowerInvariant()}");
                }

                sb.Append($"OK: {Route.Count} waypoint(s)");
                args.Context.AddString(sb.ToString());
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_route_start", "Follow the planned route: cli_route_start", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                Start(args.Context.AddString);
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_route_stop", "Stop following the route", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                Stop(args.Context.AddString);
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_route_status", "Report route execution state", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                Status(args.Context.AddString);
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_route_from_road", "Load a ProceduralRoads route: cli_route_from_road <index|nearest> [spacing=6] [walk|run|sprint] [reverse] [radius=<m>]", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                LoadFromProceduralRoads(args);
            }, isCheat: true);
        }

        public static int ReplaceRoute(IEnumerable<Vector3> positions, Gait gait)
        {
            Stop(_ => { });
            Route.Clear();

            foreach (Vector3 position in positions)
            {
                Route.Add(new Waypoint { Position = position, Gait = gait });
            }

            _lastResult = "idle";
            _currentIndex = -1;
            return Route.Count;
        }

        public static void Start(Action<string> addOutput)
        {
            if (Route.Count == 0)
            {
                addOutput("ERROR: Route is empty. Add waypoints with cli_route_add first.");
                return;
            }

            if (Player.m_localPlayer == null)
            {
                addOutput("ERROR: No local player found");
                return;
            }

            if (valheimCLIPlugin.Instance == null)
            {
                addOutput("ERROR: valheimCLI plugin instance is not available");
                return;
            }

            if (_active != null)
            {
                valheimCLIPlugin.Instance.StopCoroutine(_active);
                ReleaseControls();
            }

            _lastResult = "running";
            _active = valheimCLIPlugin.Instance.StartCoroutine(FollowRoutine());
            addOutput($"OK: route started with {Route.Count} waypoint(s)");
        }

        public static void Stop(Action<string> addOutput)
        {
            if (_active != null && valheimCLIPlugin.Instance != null)
            {
                valheimCLIPlugin.Instance.StopCoroutine(_active);
                _active = null;
                _lastResult = $"stopped at waypoint {_currentIndex}";
                ReleaseControls();
            }

            _currentIndex = -1;
            addOutput("OK: route stopped");
        }

        public static void Status(Action<string> addOutput)
        {
            Player player = Player.m_localPlayer;
            string distance = "n/a";
            if (IsRunning && player != null && _currentIndex >= 0 && _currentIndex < Route.Count)
            {
                distance = HorizontalDistance(player.transform.position, Route[_currentIndex].Position).ToString("F1", CultureInfo.InvariantCulture);
            }

            addOutput($"OK: running={IsRunning} waypoint={_currentIndex}/{Route.Count} distanceToNext={distance} lastResult={_lastResult}");
        }

        private static IEnumerator FollowRoutine()
        {
            // Vanilla PlayerController re-issues SetControls from real input every
            // physics tick, zeroing scripted steering. Disable it for the duration.
            Player routePlayer = Player.m_localPlayer;
            if (routePlayer == null)
            {
                _lastResult = "aborted: player lost";
                _active = null;
                yield break;
            }

            _disabledController = routePlayer.GetComponent<PlayerController>();
            if (_disabledController != null)
            {
                _disabledController.enabled = false;
            }

            WaitForFixedUpdate wait = new();
            Vector3 steeringDirection = InitialSteeringDirection(routePlayer);

            for (int i = 0; i < Route.Count; i++)
            {
                _currentIndex = i;
                Waypoint wp = Route[i];

                Player startPlayer = Player.m_localPlayer;
                if (startPlayer == null)
                {
                    _lastResult = "aborted: player lost";
                    break;
                }

                // Generous deadline scaled to leg length and gait so a blocked
                // path fails loudly instead of steering into a wall forever.
                float legDistance = HorizontalDistance(startPlayer.transform.position, wp.Position);
                float assumedSpeed = wp.Gait == Gait.Walk ? 1.0f : 2.0f;
                float deadline = Time.time + Mathf.Max(10f, legDistance / assumedSpeed * 2f + 5f);

                while (true)
                {
                    Player player = Player.m_localPlayer;
                    if (player == null)
                    {
                        _lastResult = "aborted: player lost";
                        ReleaseControls();
                        _active = null;
                        yield break;
                    }

                    if (HorizontalDistance(player.transform.position, wp.Position) <= ArrivalRadius)
                    {
                        break;
                    }

                    if (Time.time > deadline)
                    {
                        _lastResult = $"stuck at waypoint {i}";
                        ReleaseControls();
                        _active = null;
                        yield break;
                    }

                    Vector3 direction = wp.Position - player.transform.position;
                    direction.y = 0f;
                    if (direction.sqrMagnitude < 0.0001f)
                    {
                        break;
                    }

                    Vector3 targetDirection = GetSteeringDirection(player.transform.position, i);
                    if (targetDirection.sqrMagnitude < 0.0001f)
                    {
                        targetDirection = direction.normalized;
                    }

                    float turnRate = TurnRateDegreesPerSecond(wp.Gait) * Mathf.Deg2Rad;
                    steeringDirection = Vector3.RotateTowards(
                        steeringDirection,
                        targetDirection.normalized,
                        turnRate * Time.fixedDeltaTime,
                        0f);

                    if (steeringDirection.sqrMagnitude < 0.0001f)
                    {
                        steeringDirection = targetDirection.normalized;
                    }

                    steeringDirection.Normalize();

                    // SetControls' movedir is look-relative (z = along look direction),
                    // so rotate smoothly toward the route and push forward.
                    player.SetLookDir(steeringDirection);
                    player.SetWalk(wp.Gait == Gait.Walk);
                    player.SetControls(Vector3.forward, false, false, false, false, false, false, false, false, wp.Gait == Gait.Sprint, false);
                    yield return wait;
                }
            }

            if (_lastResult == "running")
            {
                _lastResult = "completed";
            }

            ReleaseControls();
            _active = null;
            _currentIndex = -1;
        }

        private static Vector3 InitialSteeringDirection(Player player)
        {
            Vector3 forward = player.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude >= 0.0001f)
            {
                return forward.normalized;
            }

            if (Route.Count > 0)
            {
                Vector3 routeDirection = Route[0].Position - player.transform.position;
                routeDirection.y = 0f;
                if (routeDirection.sqrMagnitude >= 0.0001f)
                {
                    return routeDirection.normalized;
                }
            }

            return Vector3.forward;
        }

        private static Vector3 GetSteeringDirection(Vector3 position, int routeIndex)
        {
            Vector3 target = GetLookAheadTarget(position, routeIndex);
            Vector3 direction = target - position;
            direction.y = 0f;
            return direction;
        }

        private static Vector3 GetLookAheadTarget(Vector3 position, int routeIndex)
        {
            if (Route.Count == 0)
            {
                return position;
            }

            int index = Mathf.Clamp(routeIndex, 0, Route.Count - 1);
            Vector3 target = Route[index].Position;
            float distanceBudget = RouteLookAheadDistance;
            Vector3 from = position;

            while (index < Route.Count - 1)
            {
                float distanceToTarget = HorizontalDistance(from, target);
                if (distanceToTarget >= distanceBudget)
                {
                    return target;
                }

                distanceBudget -= distanceToTarget;
                index++;
                from = target;
                target = Route[index].Position;
            }

            return target;
        }

        private static float TurnRateDegreesPerSecond(Gait gait)
        {
            switch (gait)
            {
                case Gait.Walk:
                    return WalkTurnRateDegreesPerSecond;
                case Gait.Sprint:
                    return SprintTurnRateDegreesPerSecond;
                default:
                    return RunTurnRateDegreesPerSecond;
            }
        }

        private static void ReleaseControls()
        {
            Player player = Player.m_localPlayer;
            if (player != null)
            {
                player.SetWalk(false);
                player.SetControls(Vector3.zero, false, false, false, false, false, false, false, false, false, false);
            }

            if (_disabledController != null)
            {
                _disabledController.enabled = true;
                _disabledController = null;
            }
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public static bool TryParseGait(string value, out Gait gait)
        {
            switch (value.ToLowerInvariant())
            {
                case "walk":
                    gait = Gait.Walk;
                    return true;
                case "run":
                    gait = Gait.Run;
                    return true;
                case "sprint":
                    gait = Gait.Sprint;
                    return true;
                default:
                    gait = Gait.Run;
                    return false;
            }
        }

        private static bool TryParseFloat(string value, out float parsed)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed) || float.TryParse(value, out parsed);
        }

        private static void LoadFromProceduralRoads(Terminal.ConsoleEventArgs args)
        {
            if (args.Length < 2)
            {
                args.Context.AddString("Usage: cli_route_from_road <index|nearest> [spacing=6] [walk|run|sprint] [reverse] [radius=<m>]");
                return;
            }

            float spacing = 6f;
            float nearestRadius = 200f;
            bool reverse = false;
            Gait gait = Gait.Walk;
            bool spacingParsed = false;

            for (int i = 2; i < args.Length; i++)
            {
                string option = args[i].Trim();
                string lower = option.ToLowerInvariant();

                if (lower == "reverse")
                {
                    reverse = true;
                }
                else if (lower.StartsWith("radius=", StringComparison.OrdinalIgnoreCase))
                {
                    string value = option.Substring("radius=".Length);
                    if (!TryParseFloat(value, out nearestRadius))
                    {
                        args.Context.AddString($"ERROR: Invalid radius '{value}'");
                        return;
                    }
                }
                else if (TryParseGait(lower, out Gait parsedGait))
                {
                    gait = parsedGait;
                }
                else if (!spacingParsed && TryParseFloat(option, out float parsedSpacing))
                {
                    spacing = parsedSpacing;
                    spacingParsed = true;
                }
                else
                {
                    args.Context.AddString($"ERROR: Unknown option '{option}'");
                    return;
                }
            }

            Assembly? assembly = FindProceduralRoadsAssembly();
            if (assembly == null)
            {
                args.Context.AddString("ERROR: ProceduralRoads assembly is not loaded");
                return;
            }

            Type? generatorType = assembly.GetType("ProceduralRoads.RoadNetworkGenerator");
            if (generatorType == null)
            {
                args.Context.AddString("ERROR: ProceduralRoads.RoadNetworkGenerator was not found");
                return;
            }

            int routeIndex;
            if (args[1].Equals("nearest", StringComparison.OrdinalIgnoreCase))
            {
                Player player = Player.m_localPlayer;
                if (player == null)
                {
                    args.Context.AddString("ERROR: No local player found");
                    return;
                }

                MethodInfo? nearestMethod = generatorType.GetMethod("FindNearestRoadRouteIndex", BindingFlags.Public | BindingFlags.Static);
                if (nearestMethod == null)
                {
                    args.Context.AddString("ERROR: ProceduralRoads does not expose FindNearestRoadRouteIndex");
                    return;
                }

                object? nearestResult = nearestMethod.Invoke(null, new object[] { player.transform.position, nearestRadius });
                routeIndex = nearestResult is int index ? index : -1;
                if (routeIndex < 0)
                {
                    args.Context.AddString($"ERROR: No ProceduralRoads route found within {nearestRadius.ToString("F1", CultureInfo.InvariantCulture)}m");
                    return;
                }
            }
            else if (!int.TryParse(args[1], out routeIndex))
            {
                args.Context.AddString($"ERROR: Route selector '{args[1]}' is not an index or nearest");
                return;
            }

            MethodInfo? waypointsMethod = generatorType.GetMethod("GetRoadRouteWaypoints", BindingFlags.Public | BindingFlags.Static);
            if (waypointsMethod == null)
            {
                args.Context.AddString("ERROR: ProceduralRoads does not expose GetRoadRouteWaypoints");
                return;
            }

            object? waypointsResult = waypointsMethod.Invoke(null, new object[] { routeIndex, spacing, reverse });
            IEnumerable? waypointEnumerable = waypointsResult as IEnumerable;
            if (waypointEnumerable == null)
            {
                args.Context.AddString("ERROR: ProceduralRoads returned no waypoint collection");
                return;
            }

            List<Vector3> waypoints = new List<Vector3>();
            foreach (object waypointObject in waypointEnumerable)
            {
                if (waypointObject is Vector3 waypoint)
                {
                    waypoints.Add(waypoint);
                }
            }

            if (waypoints.Count == 0)
            {
                args.Context.AddString($"ERROR: ProceduralRoads route {routeIndex} has no exported waypoints");
                return;
            }

            int count = ReplaceRoute(waypoints, gait);
            string label = GetProceduralRoadsRouteLabel(generatorType, routeIndex);
            args.Context.AddString(
                $"OK: loaded ProceduralRoads route={routeIndex} label=\"{label}\" waypoints={count} spacing={spacing.ToString("F1", CultureInfo.InvariantCulture)} gait={gait.ToString().ToLowerInvariant()} reverse={reverse}");
        }

        private static Assembly? FindProceduralRoadsAssembly()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (assembly.GetName().Name == "ProceduralRoads")
                {
                    return assembly;
                }
            }

            return null;
        }

        private static string GetProceduralRoadsRouteLabel(Type generatorType, int routeIndex)
        {
            MethodInfo? labelMethod = generatorType.GetMethod("GetRoadRouteLabel", BindingFlags.Public | BindingFlags.Static);
            if (labelMethod == null)
            {
                return "";
            }

            object? labelResult = labelMethod.Invoke(null, new object[] { routeIndex });
            return labelResult as string ?? "";
        }
    }
}
