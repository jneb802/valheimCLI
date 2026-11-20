using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace valheimCLI
{
    /// <summary>
    /// Scripted free-fly camera moves for cinematic shots: an orbit around a focus
    /// (cli_camera_arc) and a smooth fly-through of waypoints (cli_camera_path), plus
    /// cli_camera_stop / cli_camera_status and cli_hud. One command starts a move; the game
    /// then advances it every frame in a postfix of GameCamera.UpdateFreeFly, after the game
    /// has applied mouse and key input, so the pose is exact and input does not fight it.
    /// When the move ends the camera stays at its last pose in free-fly; cli_freefly_release
    /// returns it to the player.
    /// </summary>
    public static class CinematicCamera
    {
        public const string HarmonyId = "valheimCLI.standard.camera";
        private static readonly int FocusMask = LayerMask.GetMask("Default", "static_solid", "terrain", "vehicle", "piece", "viewblock");
        private static Harmony? _harmony;
        private static Move? _move;
        private static string _last = "idle";

        private sealed class Move
        {
            public Move(string kind, double seconds, Func<double, CameraVector> position, Func<double, CameraVector> look)
            {
                Kind = kind;
                Seconds = seconds;
                Position = position;
                Look = look;
            }

            public string Kind { get; }
            public double Seconds { get; }
            public Func<double, CameraVector> Position { get; }
            public Func<double, CameraVector> Look { get; }
            public double Elapsed { get; set; }
        }

        public static bool IsRunning => _move != null;

        public static void Patch()
        {
            if (_harmony != null) return;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(AccessTools.Method(typeof(GameCamera), "UpdateFreeFly"), postfix: new HarmonyMethod(typeof(CinematicCamera), nameof(AfterUpdateFreeFly)));
        }

        public static void Unpatch()
        {
            _move = null;
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        internal static void Register()
        {
            _ = new Terminal.ConsoleCommand("cli_camera_arc", "Orbit the free-fly camera around a focus, always looking at it, with ease in/out: cli_camera_arc <degrees> <seconds> [x y z] [--radius m] [--height m] [--start bearing] [--distance m] [--swing] [--linear]. Without x y z the focus is what the camera centre looks at now (the ground it hits, or --distance m ahead); radius, height and start bearing come from the current camera unless given. Positive degrees move the camera to its right. --swing goes left by degrees, right by twice that, and back to the start", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                StartArc(args.Args, args.Context.AddString);
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_camera_path", "Fly the free-fly camera through waypoints on a smooth curve at constant speed with ease in/out: cli_camera_path <seconds> <x,y,z[@lx,ly,lz]> <x,y,z[@lx,ly,lz]>... [--look x,y,z] [--pitch deg] [--ahead m] [--from-here] [--linear]. The camera looks along the path (a point --ahead m on it, default 8) unless it is given per-waypoint @look points, one fixed --look target, or a fixed --pitch (positive down) along the path's heading. --from-here starts at the current camera", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                StartPath(args.Args, args.Context.AddString);
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_camera_stop", "Stop a running cli_camera_arc or cli_camera_path; the camera stays where it is, in free-fly", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (_move == null)
                {
                    args.Context.AddString("OK: no camera move running; last=" + _last);
                    return;
                }

                Finish("stopped");
                args.Context.AddString("OK: camera move stopped; " + _last);
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_camera_status", "Report the running camera move (kind, progress, pose) or how the last one ended", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                args.Context.AddString(Status());
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_hud", "Hide or show the HUD as Ctrl+F3 does (crosshair and hover text go with it): cli_hud [hide|show|toggle]. No argument reports the state", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                SetHud(args.Length >= 2 ? args[1] : null, args.Context.AddString);
            }, isCheat: true);
        }

        /// <summary>Ends a running move when free-fly is turned off under it or the camera goes away.</summary>
        internal static void Tick()
        {
            if (_move == null) return;
            GameCamera camera = GameCamera.instance;
            if (camera == null || !camera.m_freeFly)
            {
                Finish("interrupted (free-fly off)");
            }
        }

        /// <summary>Stops a move without moving the camera; free-fly release and an explicit pose call this.</summary>
        internal static void StopForManualPose()
        {
            if (_move != null) Finish("stopped by a manual pose or release");
        }

        private static void StartArc(string[] args, Action<string> output)
        {
            if (!CameraArcRequest.TryParse(args, out CameraArcRequest request, out string error))
            {
                output(error);
                return;
            }

            if (!TryCamera(output, out GameCamera camera)) return;
            Transform view = camera.transform;
            CameraVector here = From(view.position);
            CameraVector focus;
            if (request.Focus.HasValue)
            {
                focus = request.Focus.Value;
            }
            else if (request.Distance.HasValue)
            {
                focus = From(view.position + (view.forward * (float)request.Distance.Value));
            }
            else if (Physics.Raycast(view.position, view.forward, out RaycastHit hit, 5000f, FocusMask))
            {
                focus = From(hit.point);
            }
            else
            {
                output("ERROR: the camera centre hits nothing within 5000 m; give x y z or --distance m");
                return;
            }

            CameraArc arc = CameraArc.FromCamera(here, focus, request.Radius, request.Height, request.StartBearing, request.Degrees, request.Swing, request.Linear);
            if (arc.Radius < 0.5)
            {
                output("ERROR: the camera is right above the focus (radius " + arc.Radius.ToString("F2", CultureInfo.InvariantCulture) + " m); give --radius m");
                return;
            }

            Begin(camera, new Move("arc", request.Seconds, arc.PositionAt, arc.LookAt));
            output(string.Format(CultureInfo.InvariantCulture,
                "OK: camera arc started focus={0} radius={1:F1} height={2:F1} start={3:F1} degrees={4:F1}{5} seconds={6:F1} ease={7}",
                focus.Format(), arc.Radius, arc.Height, arc.StartBearing, request.Degrees, request.Swing ? " swing" : string.Empty, request.Seconds, request.Linear ? "linear" : "inout"));
        }

        private static void StartPath(string[] args, Action<string> output)
        {
            if (!CameraPathRequest.TryParse(args, out CameraPathRequest request, out string error))
            {
                output(error);
                return;
            }

            if (!TryCamera(output, out GameCamera camera)) return;
            System.Collections.Generic.List<CameraVector> points = new System.Collections.Generic.List<CameraVector>();
            if (request.FromHere) points.Add(From(camera.transform.position));
            points.AddRange(request.Points);
            CameraPath path = new CameraPath(points, request.Looks, request.Target, request.Pitch, request.LookAhead, request.Linear);
            if (path.Length < 0.5)
            {
                output("ERROR: the path is shorter than 0.5 m");
                return;
            }

            Begin(camera, new Move("path", request.Seconds, path.PositionAt, path.LookAt));
            string look = request.Target.HasValue ? "target " + request.Target.Value.Format()
                : request.Looks != null ? "waypoint looks"
                : request.Pitch.HasValue ? "heading pitch=" + request.Pitch.Value.ToString("F1", CultureInfo.InvariantCulture)
                : "along path ahead=" + request.LookAhead.ToString("F1", CultureInfo.InvariantCulture);
            output(string.Format(CultureInfo.InvariantCulture,
                "OK: camera path started waypoints={0} length={1:F1}m seconds={2:F1} speed={3:F2}m/s look={4} ease={5}",
                path.Count, path.Length, request.Seconds, path.Length / request.Seconds, look, request.Linear ? "linear" : "inout"));
        }

        private static bool TryCamera(Action<string> output, out GameCamera camera)
        {
            camera = GameCamera.instance;
            if (camera == null)
            {
                output("ERROR: GameCamera not available");
                return false;
            }

            return true;
        }

        private static void Begin(GameCamera camera, Move move)
        {
            if (_move != null) _last = _move.Kind + " replaced by a new move";
            if (!camera.m_freeFly) camera.ToggleFreeFly();
            camera.SetFreeFlySmoothness(0f);
            camera.m_freeFlyLockon = null;
            camera.m_freeFlyTarget = null;
            camera.m_freeFlySavedVel = Vector3.zero;
            _move = move;
            Apply(camera, move, 0);
        }

        private static void AfterUpdateFreeFly(GameCamera __instance, float dt)
        {
            Move? move = _move;
            if (move == null) return;
            try
            {
                move.Elapsed += dt;
                double t = move.Seconds > 0 ? move.Elapsed / move.Seconds : 1;
                Apply(__instance, move, t);
                if (t >= 1) Finish("finished");
            }
            catch (Exception ex)
            {
                _move = null;
                _last = move.Kind + " failed: " + ex.Message;
                valheimCLIPlugin.Log.LogError("Camera move failed: " + ex);
            }
        }

        private static void Apply(GameCamera camera, Move move, double t)
        {
            t = CameraMath.Clamp01(t);
            CameraVector position = move.Position(t);
            CameraVector look = move.Look(t);
            if (!CameraMath.TryYawPitch(look - position, out double yaw, out double pitch))
            {
                yaw = camera.m_freeFlyYaw;
                pitch = camera.m_freeFlyPitch;
            }

            camera.m_freeFlyYaw = (float)yaw;
            camera.m_freeFlyPitch = (float)pitch;
            camera.m_freeFlyVel = Vector3.zero;
            Quaternion rotation = Quaternion.Euler((float)pitch, (float)yaw, 0f);
            camera.m_freeFlyRef = rotation;
            camera.transform.position = To(position);
            camera.transform.rotation = rotation;
        }

        private static void Finish(string how)
        {
            Move? move = _move;
            _move = null;
            if (move == null) return;
            GameCamera camera = GameCamera.instance;
            string pose = camera != null ? " " + Pose(camera) : string.Empty;
            _last = string.Format(CultureInfo.InvariantCulture, "{0} {1} after {2:F1}/{3:F1}s{4}", move.Kind, how, Math.Min(move.Elapsed, move.Seconds), move.Seconds, pose);
            valheimCLIPlugin.Log.LogInfo("Camera " + _last);
        }

        private static string Status()
        {
            Move? move = _move;
            GameCamera camera = GameCamera.instance;
            if (move == null)
            {
                return "OK: camera idle; last=" + _last + (camera != null ? " now " + Pose(camera) : string.Empty);
            }

            double fraction = move.Seconds > 0 ? Math.Min(1, move.Elapsed / move.Seconds) : 1;
            return string.Format(CultureInfo.InvariantCulture, "OK: camera {0} running {1:F1}/{2:F1}s ({3:F0}%) {4}",
                move.Kind, move.Elapsed, move.Seconds, fraction * 100, camera != null ? Pose(camera) : string.Empty);
        }

        private static string Pose(GameCamera camera)
        {
            Vector3 p = camera.transform.position;
            return string.Format(CultureInfo.InvariantCulture, "pos={0:F2},{1:F2},{2:F2} yaw={3:F1} pitch={4:F1} freefly={5}",
                p.x, p.y, p.z, camera.m_freeFlyYaw, camera.m_freeFlyPitch, camera.m_freeFly);
        }

        private static void SetHud(string? mode, Action<string> output)
        {
            Hud hud = Hud.instance;
            if (hud == null)
            {
                output("ERROR: Hud not available (no world loaded)");
                return;
            }

            string verb = (mode ?? "status").ToLowerInvariant();
            switch (verb)
            {
                case "hide":
                    hud.m_userHidden = true;
                    break;
                case "show":
                    hud.m_userHidden = false;
                    break;
                case "toggle":
                    hud.m_userHidden = !hud.m_userHidden;
                    break;
                case "status":
                    break;
                default:
                    output("Usage: cli_hud [hide|show|toggle]");
                    return;
            }

            // Hud.Update applies m_userHidden each frame while a local player exists.
            output("OK: hud hidden=" + (hud.m_userHidden ? "true" : "false") + (Player.m_localPlayer == null ? " (applies once a local player exists)" : string.Empty));
        }

        private static CameraVector From(Vector3 v) => new CameraVector(v.x, v.y, v.z);

        private static Vector3 To(CameraVector v) => new Vector3((float)v.X, (float)v.Y, (float)v.Z);
    }
}
