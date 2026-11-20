using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace valheimCLI
{
    /// <summary>
    /// Scripted free-fly camera moves for cinematic shots: an orbit around a focus
    /// (cli_camera_arc) and a smooth fly-through of waypoints (cli_camera_path), plus
    /// cli_camera_stop / cli_camera_status, cli_camera_clean and cli_hud. One command starts a move;
    /// the game then advances it every frame in a postfix of GameCamera.UpdateFreeFly, after the
    /// game has applied mouse and key input, so the pose is exact and input does not fight it.
    /// The same frame hook carries the player along (--carry, the default for paths) and keeps
    /// the shot clean (--clean). When the move ends free-fly goes off and the camera returns to
    /// the player (after a carried player is set back); --stay keeps the last pose in free-fly.
    /// </summary>
    public static class CinematicCamera
    {
        public const string HarmonyId = "valheimCLI.standard.camera";
        /// <summary>Spacing of the points along a move that --clean measures distances to.</summary>
        private const double CleanStep = 2.0;
        private static Harmony? _harmony;
        private static Move? _move;
        private static PlayerCarry? _carry;
        /// <summary>A finished move owes the player its view once the carried player has landed.</summary>
        private static bool _viewPending;
        /// <summary>The free-fly smoothness before the first move took the camera (moves set it to 0).</summary>
        private static float? _smoothBefore;
        private static string _last = "idle";

        private sealed class Move
        {
            public Move(string kind, double seconds, Func<double, CameraVector> position, Func<double, CameraVector> look, CameraMoveOptions options, List<CameraMoveSample> near)
            {
                Kind = kind;
                Seconds = seconds;
                Position = position;
                Look = look;
                Options = options;
                Near = near;
            }

            public string Kind { get; }
            public double Seconds { get; }
            public Func<double, CameraVector> Position { get; }
            public Func<double, CameraVector> Look { get; }
            public CameraMoveOptions Options { get; }
            /// <summary>Points along the move (and an arc's focus) that --clean keeps clear around; empty without --clean.</summary>
            public List<CameraMoveSample> Near { get; }
            /// <summary>The carry the move really has: an inherited one, or none when a default carry was not possible.</summary>
            public CameraCarry Carry { get; set; }
            /// <summary>What the start changed from the request (a default carry skipped, the last move's player released), for the OK line.</summary>
            public string Note { get; set; } = string.Empty;
            public ShotCleaner.Counts Cleaned;
            public double Elapsed { get; set; }
        }

        public static bool IsRunning => _move != null;

        public static void Patch()
        {
            if (_harmony != null) return;
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(AccessTools.Method(typeof(GameCamera), "UpdateFreeFly"), postfix: new HarmonyMethod(typeof(CinematicCamera), nameof(AfterUpdateFreeFly)));
            _harmony.Patch(AccessTools.Method(typeof(Game), nameof(Game.SavePlayerProfile)),
                prefix: new HarmonyMethod(typeof(CinematicCamera), nameof(BeforeSavePlayerProfile)),
                finalizer: new HarmonyMethod(typeof(CinematicCamera), nameof(AfterSavePlayerProfile)));
        }

        /// <summary>
        /// Every save that records a logout point (quit, logout, and also the periodic world save)
        /// reads the player's position. While the player is carried that is a point in the air by
        /// the camera, so for the save only the player stands at its start; the move goes on.
        /// </summary>
        private static void BeforeSavePlayerProfile(bool setLogoutPoint, out Vector3? __state)
        {
            __state = null;
            PlayerCarry? carry = _carry;
            if (!setLogoutPoint || carry == null || carry.Returning) return;
            Player player = carry.Player;
            if (player == null || player != Player.m_localPlayer) return;
            __state = player.transform.position;
            player.transform.position = carry.Start;
        }

        private static Exception? AfterSavePlayerProfile(Exception? __exception, Vector3? __state)
        {
            if (__state.HasValue && _carry != null && _carry.Player != null && _carry.Player == Player.m_localPlayer)
            {
                _carry.Player.transform.position = __state.Value;
            }

            return __exception;
        }

        public static void Unpatch()
        {
            _move = null;
            // A reload must not leave the player hidden, flying or in god mode, nor spawning paused.
            try { _carry?.Abandon(); }
            catch (Exception ex)
            {
                valheimCLIPlugin.Log.LogWarning("Camera carry restore at unload failed: " + ex.Message);
                _carry?.ForceRestore();
            }
            _carry = null;
            SpawnPause.Release();
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        internal static void Register()
        {
            _ = new Terminal.ConsoleCommand("cli_camera_arc", "Orbit the free-fly camera around a focus, always looking at it, with ease in/out: cli_camera_arc <degrees> <seconds> [x y z] [--radius m] [--height m] [--start bearing] [--distance m] [--swing] [--linear] " + CameraMoveOptions.Usage + ". Without x y z the focus is what the camera centre looks at now (the ground it hits, or --distance m ahead); radius, height and start bearing come from the current camera unless given. Positive degrees move the camera to its right. --swing goes left by degrees, right by twice that, and back to the start. --carry takes the player along (hidden, flying, god and ghost) and puts it back at the start (or 'end': the ground under the camera's end); --check [m] is a dry run listing where the arc comes within m (default 1) of solid colliders; --clean [m] despawns hostile creatures and removes drops within m (default 40) of the arc and its focus, at the start and every frame; --nospawn pauses natural spawning for the move. When it ends, free-fly goes off and the camera returns to the player (even if free-fly was on before the move); --stay keeps the last pose in free-fly", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                StartArc(args.Args, args.Context.AddString);
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_camera_path", "Fly the free-fly camera through waypoints on a smooth curve at constant speed with ease in/out: cli_camera_path <seconds> <x,y,z[@lx,ly,lz]> <x,y,z[@lx,ly,lz]>... [--look x,y,z] [--pitch deg] [--ahead m] [--from-here] [--linear] " + CameraMoveOptions.Usage + ". The camera looks along the path (a point --ahead m on it, default 8) unless it is given per-waypoint @look points, one fixed --look target, or a fixed --pitch (positive down) along the path's heading. --from-here starts at the current camera. The player is carried along so zones load (hidden, flying, god and ghost) and put back at the start (--carry end: the ground under the camera's end; --no-carry: stays). --check [m] is a dry run listing where the path comes within m (default 1) of solid colliders or crosses unloaded ground; --clean [m] despawns hostile creatures and removes drops within m (default 40) of the path, at the start and every frame; --nospawn pauses natural spawning for the move. When it ends, free-fly goes off and the camera returns to the player (even if free-fly was on before the move); --stay keeps the last pose in free-fly", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                StartPath(args.Args, args.Context.AddString);
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_camera_stop", "Stop a running cli_camera_arc or cli_camera_path: a carried player is put back, then free-fly goes off and the camera returns to the player: cli_camera_stop [--stay]. --stay keeps the camera where it stopped, in free-fly", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                if (!CameraStopRequest.TryParse(args.Args, out CameraStopRequest request, out string error))
                {
                    args.Context.AddString(error);
                    return;
                }

                if (_move == null)
                {
                    // Nothing to end: a camera framed by hand stays as it is. A view return still owed to a
                    // landing player is kept, or dropped with --stay.
                    string owed = string.Empty;
                    if (_viewPending)
                    {
                        if (request.Stay) CancelPendingView();
                        owed = request.Stay ? "; camera kept in free-fly (--stay): no return to the player on landing" : "; camera returns to the player once it has landed";
                    }

                    args.Context.AddString("OK: no camera move running" + owed + "; last=" + _last);
                    return;
                }

                Finish("stopped", CameraMoveEnd.Stopped, request.Stay);
                args.Context.AddString("OK: camera move stopped; " + _last);
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_camera_status", "Report the running camera move (kind, progress, pose, carry, clean, nospawn) or how the last one ended", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                args.Context.AddString(Status());
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_camera_clean", "Clear a shot: despawn hostile creatures (no corpse, no loot) and remove item drops and corpses within radius m (default 60) of x y z or of the camera: cli_camera_clean [radius] [x y z] [--all]. --all also removes creatures that are not hostile (deer); tamed creatures, bosses and players always stay", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                CleanCommand(args.Args, args.Context.AddString);
            }, isCheat: true);

            _ = new Terminal.ConsoleCommand("cli_hud", "Hide or show the HUD as Ctrl+F3 does (crosshair and hover text go with it): cli_hud [hide|show|toggle]. No argument reports the state", (Terminal.ConsoleEvent)delegate(Terminal.ConsoleEventArgs args)
            {
                SetHud(args.Length >= 2 ? args[1] : null, args.Context.AddString);
            }, isCheat: true);
        }

        /// <summary>
        /// Ends a running move when free-fly is turned off under it or the camera goes away, and
        /// finishes putting a carried player back when that needed a distant teleport.
        /// </summary>
        internal static void Tick()
        {
            if (_move != null)
            {
                GameCamera camera = GameCamera.instance;
                if (camera == null || !camera.m_freeFly)
                {
                    Finish("interrupted (free-fly off)", CameraMoveEnd.Interrupted, stay: false);
                }
            }

            // A return in flight finishes even while a later move without carry runs.
            if (_carry != null && _carry.Returning)
            {
                try
                {
                    if (_carry.Tick()) Landed("; player back");
                }
                catch (Exception ex)
                {
                    valheimCLIPlugin.Log.LogError("Camera carry return failed: " + ex);
                    _carry?.ForceRestore();
                    Landed("; player return FAILED (flags restored where it is): " + ex.Message);
                }
            }

            // Whoever turned free-fly off (a move's end, cli_freefly_release, the free-fly key, a world
            // change), the smoothness the moves set to 0 goes back to what it was.
            GameCamera now = GameCamera.instance;
            if (_smoothBefore.HasValue && _move == null && (now == null || !now.m_freeFly)) RestoreSmoothness(now);
        }

        /// <summary>The carried player is back: the view owed to it (a move that ended while it was on its way) is given.</summary>
        private static void Landed(string what)
        {
            _carry = null;
            _last += what;
            if (_viewPending) _last += ReturnView(GameCamera.instance);
        }

        /// <summary>A view return still owed to a landing player is dropped: someone else now decides the view.</summary>
        private static void CancelPendingView() => _viewPending = false;

        private static void RestoreSmoothness(GameCamera? camera)
        {
            if (camera != null && _smoothBefore.HasValue) camera.SetFreeFlySmoothness(_smoothBefore.Value);
            _smoothBefore = null;
        }

        /// <summary>Stops a move without moving the camera; free-fly release and an explicit pose call this. They decide the view, so a view return still owed is dropped too.</summary>
        internal static void StopForManualPose()
        {
            CancelPendingView();
            if (_move != null) Finish("stopped by a manual pose or release", CameraMoveEnd.ManualPose, stay: true);
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
            else if (Physics.Raycast(view.position, view.forward, out RaycastHit hit, 5000f, LayerMask.GetMask("Default", "static_solid", "terrain", "vehicle", "piece", "viewblock", "Water")))
            {
                focus = From(hit.point);
            }
            else
            {
                output("ERROR: the camera centre hits nothing within 5000 m; give x y z or --distance m");
                return;
            }

            CameraArc arc = CameraArc.FromCamera(here, focus, request.Radius, request.Height, request.StartBearing, request.Degrees, request.Swing, request.Linear);
            if (!request.Radius.HasValue && arc.Radius < 0.5)
            {
                output("ERROR: the camera is right above the focus (radius " + arc.Radius.ToString("F2", CultureInfo.InvariantCulture) + " m); give --radius m");
                return;
            }

            string summary = string.Format(CultureInfo.InvariantCulture,
                "focus={0} radius={1:F1} height={2:F1} start={3:F1} degrees={4:F1}{5}",
                focus.Format(), arc.Radius, arc.Height, arc.StartBearing, request.Degrees, request.Swing ? " swing" : string.Empty);
            if (request.Options.Check.HasValue)
            {
                Check("arc", "deg", "deg is the bearing offset from the start in degrees, positive to the camera's right", arc.Samples(CameraClearance.SampleStep), request.Options, summary, output);
                return;
            }

            List<CameraMoveSample> near = new List<CameraMoveSample>();
            if (request.Options.Clean.HasValue)
            {
                near = arc.Samples(CleanStep);
                near.Add(new CameraMoveSample(0, 0, focus));
            }

            if (!Begin(camera, new Move("arc", request.Seconds, arc.PositionAt, arc.LookAt, request.Options, near), output)) return;
            output(string.Format(CultureInfo.InvariantCulture, "OK: camera arc started {0} seconds={1:F1} ease={2} {3}{4}",
                summary, request.Seconds, request.Linear ? "linear" : "inout", request.Options.Describe(_move?.Carry), Cleaned(_move) + (_move?.Note ?? string.Empty)));
        }

        private static void StartPath(string[] args, Action<string> output)
        {
            if (!CameraPathRequest.TryParse(args, out CameraPathRequest request, out string error))
            {
                output(error);
                return;
            }

            if (!TryCamera(output, out GameCamera camera)) return;
            List<CameraVector> points = new List<CameraVector>();
            if (request.FromHere) points.Add(From(camera.transform.position));
            points.AddRange(request.Points);
            CameraPath path = new CameraPath(points, request.Looks, request.Target, request.Pitch, request.LookAhead, request.Linear);
            if (path.Length < 0.5)
            {
                output("ERROR: the path is shorter than 0.5 m");
                return;
            }

            string summary = string.Format(CultureInfo.InvariantCulture, "waypoints={0} length={1:F1}m", path.Count, path.Length);
            if (request.Options.Check.HasValue)
            {
                string legend = request.FromHere
                    ? "u is the spline parameter: u=0 the camera (--from-here), u=1 the first listed waypoint, u=k.5 halfway between path points k and k+1"
                    : "u is the spline parameter: u=0 the first waypoint, u=k.5 halfway between waypoints k and k+1 (counting from 0)";
                Check("path", "u", legend, path.Samples(CameraClearance.SampleStep), request.Options, summary, output);
                return;
            }

            List<CameraMoveSample> near = request.Options.Clean.HasValue ? path.Samples(CleanStep) : new List<CameraMoveSample>();
            if (!Begin(camera, new Move("path", request.Seconds, path.PositionAt, path.LookAt, request.Options, near), output)) return;
            string look = request.Target.HasValue ? "target " + request.Target.Value.Format()
                : request.Looks != null ? "waypoint looks"
                : request.Pitch.HasValue ? "heading pitch=" + request.Pitch.Value.ToString("F1", CultureInfo.InvariantCulture)
                : "along path ahead=" + request.LookAhead.ToString("F1", CultureInfo.InvariantCulture);
            output(string.Format(CultureInfo.InvariantCulture,
                "OK: camera path started {0} seconds={1:F1} speed={2:F2}m/s look={3} ease={4} {5}{6}",
                summary, request.Seconds, path.Length / request.Seconds, look, request.Linear ? "linear" : "inout", request.Options.Describe(_move?.Carry), Cleaned(_move) + (_move?.Note ?? string.Empty)));
        }

        /// <summary>The --check dry run: the camera does not move; one line per stretch that clips or is unloaded.</summary>
        private static void Check(string kind, string parameter, string legend, List<CameraMoveSample> samples, CameraMoveOptions options, string summary, Action<string> output)
        {
            if (ZoneSystem.instance == null || ZNetScene.instance == null)
            {
                output("ERROR: no world loaded");
                return;
            }

            float clearance = (float)options.Check!.Value;
            List<ClearanceSample> probed = ClearanceProbe.Probe(samples, clearance);
            List<ClearanceStretch> stretches = CameraClearance.Merge(probed);
            int clips = 0;
            int unloaded = 0;
            foreach (ClearanceStretch stretch in stretches)
            {
                if (stretch.Kind == ClearanceKind.Clip) clips++;
                else unloaded++;
            }

            double step = samples.Count > 1 ? samples[1].Along - samples[0].Along : 0;
            output(string.Format(CultureInfo.InvariantCulture,
                "OK: camera {0} check (dry run, the camera did not move) {1} clearance={2:F2}m samples={3} step={4:F2}m clip_stretches={5} unloaded_stretches={6} result={7}; {8}; stretches merge across gaps up to {9:F0} m; only loaded colliders are checked",
                kind, summary, clearance, samples.Count, step, clips, unloaded,
                clips == 0 && unloaded == 0 ? "clear" : (clips > 0 ? "clips" : "unchecked"), legend, CameraClearance.MergeGap));
            foreach (ClearanceStretch stretch in stretches)
            {
                output(CameraClearance.Format(stretch, parameter));
            }
        }

        private static void CleanCommand(string[] args, Action<string> output)
        {
            if (!CameraCleanRequest.TryParse(args, out CameraCleanRequest request, out string error))
            {
                output(error);
                return;
            }

            if (ZNetScene.instance == null)
            {
                output("ERROR: no world loaded");
                return;
            }

            CameraVector centre;
            if (request.Centre.HasValue) centre = request.Centre.Value;
            else if (GameCamera.instance != null) centre = From(GameCamera.instance.transform.position);
            else if (Player.m_localPlayer != null) centre = From(Player.m_localPlayer.transform.position);
            else
            {
                output("ERROR: no camera or player to clean around; give x y z");
                return;
            }

            ShotCleaner.Counts counts = ShotCleaner.Clean(new[] { new CameraMoveSample(0, 0, centre) }, request.Radius, request.All, corpses: true);
            output(string.Format(CultureInfo.InvariantCulture, "OK: camera clean centre={0} radius={1:F0} {2}{3}", centre.Format(), request.Radius, request.All ? "all " : "hostile ", counts));
        }

        private static string Cleaned(Move? move) => move != null && move.Options.Clean.HasValue ? " cleaned " + move.Cleaned : string.Empty;

        private static bool TryCamera(Action<string> output, out GameCamera camera)
        {
            camera = GameCamera.instance;
            if (_harmony == null)
            {
                output("ERROR: camera moves are unavailable: the frame hook on GameCamera.UpdateFreeFly did not install (see the BepInEx log)");
                return false;
            }

            if (camera == null)
            {
                output("ERROR: GameCamera not available");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Starts a move: takes (or keeps, when the move it replaces carried it) the player, pauses
        /// spawning, cleans the move's surroundings once with corpses, then sets the first pose.
        /// </summary>
        private static bool Begin(GameCamera camera, Move move, Action<string> output)
        {
            CameraMoveOptions options = move.Options;
            bool given = options.CarryGiven;
            bool carrying = _carry != null && !_carry.Returning;
            // A default carry gives way to the carry already running: a move that replaces a carried
            // move without saying --no-carry keeps carrying (to the landing the first move chose).
            CameraCarry carry = !given && carrying ? _carry!.Landing : options.Carry;
            string note = string.Empty;
            if (carry != CameraCarry.Off && !carrying)
            {
                string reason;
                PlayerCarry? taken = null;
                bool ok = _carry == null
                    ? PlayerCarry.TryTake(carry, out taken, out reason)
                    : Fail("the player is still being put back after the last carried move", out reason);
                if (!ok)
                {
                    // Asked for: refuse. Only the default: fly without it and say so.
                    if (given)
                    {
                        output("ERROR: --carry: " + reason + "; use --no-carry");
                        return false;
                    }

                    carry = CameraCarry.Off;
                    note = " (not carried: " + reason + ")";
                }
                else
                {
                    _carry = taken;
                }
            }
            else if (carry != CameraCarry.Off && given)
            {
                _carry!.Landing = carry;
            }

            if (_move != null) _last = _move.Kind + " replaced by a new move";
            _move = null;
            move.Carry = carry;
            if (carry == CameraCarry.Off)
            {
                string released = ReleaseCarry(From(camera.transform.position));
                if (released.Length > 0) note += released.Replace("; player", " (the last move's player") + ")";
            }

            if (options.NoSpawn) SpawnPause.Hold();
            else SpawnPause.Release();
            if (options.Clean.HasValue) move.Cleaned.Add(ShotCleaner.Clean(move.Near, options.Clean.Value, all: false, corpses: true));
            if (!_smoothBefore.HasValue) _smoothBefore = camera.GetFreeFlySmoothness();
            _viewPending = false;
            TakeFreeFly(camera);
            _move = move;
            Apply(camera, move, 0);
            move.Note = note;
            return true;
        }

        private static bool Fail(string why, out string reason)
        {
            reason = why;
            return false;
        }

        /// <summary>Free-fly on, no smoothing, lock-on, target or drift: the state every scripted pose starts from.</summary>
        internal static void TakeFreeFly(GameCamera camera)
        {
            CancelPendingView();
            if (!camera.m_freeFly) camera.ToggleFreeFly();
            camera.SetFreeFlySmoothness(0f);
            camera.m_freeFlyLockon = null;
            camera.m_freeFlyTarget = null;
            camera.m_freeFlyVel = Vector3.zero;
            camera.m_freeFlySavedVel = Vector3.zero;
        }

        /// <summary>
        /// Writes a free-fly pose: the position, and the yaw/pitch the free-fly update derives its
        /// rotation from every frame (a bare LookAt would be overwritten on the next LateUpdate).
        /// When the target coincides with the position the camera keeps its current heading.
        /// </summary>
        internal static void SetPose(GameCamera camera, Vector3 position, Vector3 target, out float yaw, out float pitch)
        {
            if (CameraMath.TryYawPitch(From(target) - From(position), out double y, out double p))
            {
                camera.m_freeFlyYaw = (float)y;
                camera.m_freeFlyPitch = (float)p;
            }

            yaw = camera.m_freeFlyYaw;
            pitch = camera.m_freeFlyPitch;
            camera.m_freeFlyVel = Vector3.zero;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            camera.m_freeFlyRef = rotation;
            camera.transform.position = position;
            camera.transform.rotation = rotation;
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
                if (t >= 1) Finish("finished", CameraMoveEnd.Finished, move.Options.Stay);
            }
            catch (Exception ex)
            {
                _move = null;
                _last = move.Kind + " failed: " + ex.Message;
                valheimCLIPlugin.Log.LogError("Camera move failed: " + ex);
                _last += EndExtras(__instance) + LeaveView(__instance, CameraMoveEnd.Failed, move.Options.Stay);
            }
        }

        private static void Apply(GameCamera camera, Move move, double t)
        {
            t = CameraMath.Clamp01(t);
            SetPose(camera, To(move.Position(t)), To(move.Look(t)), out _, out _);
            _carry?.Follow(camera.transform.position);
            if (move.Options.Clean.HasValue && t > 0)
            {
                double radius = move.Options.Clean.Value;
                move.Cleaned.Add(ShotCleaner.Clean(LoadedPart(move.Near, radius), radius, all: false, corpses: false));
            }
        }

        /// <summary>How far from this game's reference position creatures and drops can exist: its loaded zones (5 x 5 zones of 64 m), corner to centre, plus a zone.</summary>
        private const double LoadedReach = 290;
        private static readonly List<CameraMoveSample> Window = new List<CameraMoveSample>();

        /// <summary>
        /// The move's points that anything this game has loaded can be near: creatures and drops exist
        /// only in the zones around the reference position (the carried player, or the player), so a
        /// per-frame sweep measures against those points only, not the whole of a long path.
        /// </summary>
        private static List<CameraMoveSample> LoadedPart(List<CameraMoveSample> near, double radius)
        {
            Window.Clear();
            if (ZNet.instance == null) return Window;
            Vector3 centre = ZNet.instance.GetReferencePosition();
            double reach = LoadedReach + radius;
            double reach2 = reach * reach;
            foreach (CameraMoveSample sample in near)
            {
                double dx = sample.Position.X - centre.x;
                double dz = sample.Position.Z - centre.z;
                if ((dx * dx) + (dz * dz) <= reach2) Window.Add(sample);
            }

            return Window;
        }

        private static void Finish(string how, CameraMoveEnd end, bool stay)
        {
            Move? move = _move;
            _move = null;
            if (move == null) return;
            GameCamera camera = GameCamera.instance;
            string pose = camera != null ? " " + Pose(camera) : string.Empty;
            _last = string.Format(CultureInfo.InvariantCulture, "{0} {1} after {2:F1}/{3:F1}s{4}{5}", move.Kind, how, Math.Min(move.Elapsed, move.Seconds), move.Seconds, pose, Cleaned(move));
            // The carried player is set back first, so the view returns to where it now stands.
            _last += EndExtras(camera);
            _last += LeaveView(camera, end, stay);
            valheimCLIPlugin.Log.LogInfo("Camera " + _last);
        }

        /// <summary>
        /// Leaves the view as <see cref="CameraMoveEnding.View"/> says. A return to the player waits
        /// while the carried player is still on its way back (a distant teleport), so the view does
        /// not jump to where the player still hangs at the camera's end; Tick gives it back on landing.
        /// </summary>
        private static string LeaveView(GameCamera? camera, CameraMoveEnd end, bool stay)
        {
            switch (CameraMoveEnding.View(end, stay))
            {
                case CameraViewEnd.Leave:
                    return string.Empty;
                case CameraViewEnd.Stay:
                    return "; camera stays in free-fly (--stay)";
            }

            if (_carry != null && _carry.Returning)
            {
                _viewPending = true;
                return "; camera returns to the player once it has landed";
            }

            return ReturnView(camera);
        }

        /// <summary>Free-fly off (as cli_freefly_release does) and the free-fly smoothness the moves set to 0 put back.</summary>
        private static string ReturnView(GameCamera? camera)
        {
            _viewPending = false;
            if (camera == null) return string.Empty;
            RestoreSmoothness(camera);

            if (camera.m_freeFly) camera.ToggleFreeFly();
            return "; freefly off, camera returned to the player";
        }

        /// <summary>Puts a carried player back and resumes spawning; answers what happened, for the last-move line.</summary>
        private static string EndExtras(GameCamera? camera)
        {
            bool paused = SpawnPause.Active;
            SpawnPause.Release();
            string carried = camera != null ? ReleaseCarry(From(camera.transform.position)) : ReleaseCarry(null);
            return carried + (paused ? "; spawning resumed" : string.Empty);
        }

        private static string ReleaseCarry(CameraVector? cameraEnd)
        {
            PlayerCarry? carry = _carry;
            if (carry == null || carry.Returning) return string.Empty;
            try
            {
                Vector3 end = cameraEnd.HasValue ? To(cameraEnd.Value) : carry.Player.transform.position;
                if (carry.Release(end, out string how))
                {
                    _carry = null;
                    return "; player put back at " + how;
                }

                return "; player being put back at " + how;
            }
            catch (Exception ex)
            {
                valheimCLIPlugin.Log.LogError("Camera carry release failed: " + ex);
                carry.ForceRestore();
                _carry = null;
                return "; player put back FAILED (flags restored where it is): " + ex.Message;
            }
        }

        private static string Status()
        {
            Move? move = _move;
            GameCamera camera = GameCamera.instance;
            string carry = _carry != null ? " " + _carry.Describe() : string.Empty;
            if (move == null)
            {
                return "OK: camera idle; last=" + _last + (camera != null ? " now " + Pose(camera) : string.Empty) + carry;
            }

            double fraction = move.Seconds > 0 ? Math.Min(1, move.Elapsed / move.Seconds) : 1;
            return string.Format(CultureInfo.InvariantCulture, "OK: camera {0} running {1:F1}/{2:F1}s ({3:F0}%) {4} {5}{6}{7}",
                move.Kind, move.Elapsed, move.Seconds, fraction * 100, camera != null ? Pose(camera) : string.Empty, move.Options.Describe(move.Carry), Cleaned(move), carry);
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
