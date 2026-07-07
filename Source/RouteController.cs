using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
            _disabledController = routePlayer != null ? routePlayer.GetComponent<PlayerController>() : null;
            if (_disabledController != null)
            {
                _disabledController.enabled = false;
            }

            WaitForFixedUpdate wait = new();

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

                    // SetControls' movedir is look-relative (z = along look direction),
                    // so face the waypoint and push forward.
                    player.SetLookDir(direction.normalized);
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

        private static bool TryParseGait(string value, out Gait gait)
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
    }
}
