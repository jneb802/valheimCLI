using System;
using System.Collections.Generic;
using System.Globalization;

namespace valheimCLI
{
    /// <summary>A plain world point or direction (x east, y up, z north), so the camera moves are tested without the game.</summary>
    public readonly struct CameraVector
    {
        public readonly double X;
        public readonly double Y;
        public readonly double Z;

        public CameraVector(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double Length => Math.Sqrt((X * X) + (Y * Y) + (Z * Z));

        public static CameraVector operator +(CameraVector a, CameraVector b) => new CameraVector(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static CameraVector operator -(CameraVector a, CameraVector b) => new CameraVector(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static CameraVector operator *(CameraVector a, double s) => new CameraVector(a.X * s, a.Y * s, a.Z * s);

        public static double Distance(CameraVector a, CameraVector b) => (a - b).Length;

        public string Format() => string.Format(CultureInfo.InvariantCulture, "{0:F2},{1:F2},{2:F2}", X, Y, Z);
    }

    /// <summary>
    /// The math of the scripted camera moves: easing, the free-fly yaw/pitch of a look direction,
    /// an orbit around a focus, and a constant-speed spline path. Plain .NET on purpose.
    /// </summary>
    public static class CameraMath
    {
        /// <summary>Sine ease in/out: starts and ends at rest, fastest at the middle.</summary>
        public static double EaseInOut(double t)
        {
            t = Clamp01(t);
            return 0.5 - (0.5 * Math.Cos(Math.PI * t));
        }

        public static double Ease(double t, bool linear) => linear ? Clamp01(t) : EaseInOut(t);

        public static double Clamp01(double t) => t < 0 ? 0 : (t > 1 ? 1 : t);

        /// <summary>
        /// The free-fly camera's yaw (compass bearing, 0 = north/+z, 90 = east/+x) and pitch
        /// (positive looks down) for a look direction, as GameCamera derives its rotation:
        /// Euler(0, yaw, 0) * Euler(pitch, 0, 0).
        /// </summary>
        public static bool TryYawPitch(CameraVector direction, out double yaw, out double pitch)
        {
            double length = direction.Length;
            if (length < 1e-4)
            {
                yaw = 0;
                pitch = 0;
                return false;
            }

            yaw = Math.Atan2(direction.X, direction.Z) * (180.0 / Math.PI);
            pitch = -Math.Asin(Math.Max(-1.0, Math.Min(1.0, direction.Y / length))) * (180.0 / Math.PI);
            return true;
        }

        /// <summary>The unit look direction of a free-fly yaw/pitch (the inverse of <see cref="TryYawPitch"/>).</summary>
        public static CameraVector Direction(double yaw, double pitch)
        {
            double y = yaw * (Math.PI / 180.0);
            double p = pitch * (Math.PI / 180.0);
            return new CameraVector(Math.Sin(y) * Math.Cos(p), -Math.Sin(p), Math.Cos(y) * Math.Cos(p));
        }

        /// <summary>The compass bearing (degrees) from <paramref name="from"/> to <paramref name="to"/> on the ground plane.</summary>
        public static double Bearing(CameraVector from, CameraVector to) => Math.Atan2(to.X - from.X, to.Z - from.Z) * (180.0 / Math.PI);

        public static double HorizontalDistance(CameraVector a, CameraVector b)
        {
            double dx = a.X - b.X;
            double dz = a.Z - b.Z;
            return Math.Sqrt((dx * dx) + (dz * dz));
        }
    }

    /// <summary>
    /// An orbit around a focus at a fixed horizontal radius and height. The camera's bearing
    /// from the focus starts at <see cref="StartBearing"/> and changes by <see cref="Sweep"/>
    /// degrees over the move; a positive sweep moves the camera to its right (counter-clockwise
    /// seen from above). A swing goes out to -Sweep, across to +Sweep and back to the start,
    /// easing to rest at each turn, for a centred shot that pans both ways.
    /// </summary>
    public sealed class CameraArc
    {
        public CameraArc(CameraVector focus, double radius, double height, double startBearing, double sweep, bool swing, bool linear)
        {
            Focus = focus;
            Radius = radius;
            Height = height;
            StartBearing = startBearing;
            Sweep = sweep;
            Swing = swing;
            Linear = linear;
        }

        public CameraVector Focus { get; }
        public double Radius { get; }
        public double Height { get; }
        public double StartBearing { get; }
        public double Sweep { get; }
        public bool Swing { get; }
        public bool Linear { get; }

        /// <summary>The camera's bearing offset from the start (degrees) at progress <paramref name="t"/> in 0..1.</summary>
        public double OffsetAt(double t)
        {
            t = CameraMath.Clamp01(t);
            if (!Swing)
            {
                return Sweep * CameraMath.Ease(t, Linear);
            }

            // Out -X (a quarter of the time), across to +X (half), back to 0 (a quarter): the same
            // angular speed profile on every leg, at rest at each turn.
            if (t < 0.25)
            {
                return -Sweep * CameraMath.Ease(t / 0.25, Linear);
            }

            if (t < 0.75)
            {
                return -Sweep + (2 * Sweep * CameraMath.Ease((t - 0.25) / 0.5, Linear));
            }

            return Sweep - (Sweep * CameraMath.Ease((t - 0.75) / 0.25, Linear));
        }

        /// <summary>The bearing from the focus to the camera at progress <paramref name="t"/>.</summary>
        public double BearingAt(double t) => StartBearing - OffsetAt(t);

        public CameraVector PositionAt(double t) => PositionAtOffset(OffsetAt(t));

        /// <summary>The camera's position at a bearing offset (degrees) from the start.</summary>
        public CameraVector PositionAtOffset(double offset)
        {
            double bearing = (StartBearing - offset) * (Math.PI / 180.0);
            return new CameraVector(Focus.X + (Radius * Math.Sin(bearing)), Focus.Y + Height, Focus.Z + (Radius * Math.Cos(bearing)));
        }

        /// <summary>The camera always looks at the focus.</summary>
        public CameraVector LookAt(double t) => Focus;

        /// <summary>
        /// Points on every part of the circle the move visits (0..Sweep, or -Sweep..+Sweep for a
        /// swing; one revolution at most, as more laps visit the same points), at most
        /// <paramref name="maximumStep"/> m apart along the arc. A sample's parameter is its bearing
        /// offset from the start in degrees (positive = to the camera's right).
        /// </summary>
        public List<CameraMoveSample> Samples(double maximumStep)
        {
            double from = Swing ? -Math.Abs(Sweep) : Math.Min(0, Sweep);
            double to = Swing ? Math.Abs(Sweep) : Math.Max(0, Sweep);
            if (to - from > 360)
            {
                // Keep the end the move starts from: 0 for a plain arc, -Sweep for a swing.
                if (Swing || Sweep > 0) to = from + 360;
                else from = to - 360;
            }

            double metresPerDegree = Math.Max(Radius, 1e-6) * (Math.PI / 180.0);
            int steps = CameraMoveSample.Steps((to - from) * metresPerDegree, maximumStep);
            List<CameraMoveSample> samples = new List<CameraMoveSample>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                double offset = from + ((to - from) * i / steps);
                samples.Add(new CameraMoveSample(offset, (offset - from) * metresPerDegree, PositionAtOffset(offset)));
            }

            return samples;
        }

        /// <summary>
        /// An arc whose start is the camera's current position: its radius, height and bearing
        /// relative to the focus, each replaced by an override when one is given.
        /// </summary>
        public static CameraArc FromCamera(CameraVector camera, CameraVector focus, double? radius, double? height, double? startBearing, double sweep, bool swing, bool linear)
        {
            double r = radius ?? CameraMath.HorizontalDistance(camera, focus);
            double h = height ?? (camera.Y - focus.Y);
            double b = startBearing ?? CameraMath.Bearing(focus, camera);
            return new CameraArc(focus, r, h, b, sweep, swing, linear);
        }
    }

    /// <summary>
    /// A smooth path through waypoints: a centripetal Catmull-Rom spline (no cusps or loops
    /// between close points), walked at constant speed along its length with ease in/out.
    /// The camera looks along the path (at a point a little ahead on it), at a fixed target,
    /// or at per-waypoint look points interpolated on the same spline parameter.
    /// </summary>
    public sealed class CameraPath
    {
        private const int SamplesPerSegment = 128;
        private readonly CameraVector[] _points;
        private readonly CameraVector[]? _looks;
        private readonly double[] _cumulative;

        public CameraPath(IReadOnlyList<CameraVector> points, IReadOnlyList<CameraVector>? looks, CameraVector? target, double? pitch, double lookAhead, bool linear)
        {
            if (points.Count < 2)
            {
                throw new ArgumentException("A path needs at least two waypoints.");
            }

            if (looks != null && looks.Count != points.Count)
            {
                throw new ArgumentException("Give a look point for every waypoint or for none.");
            }

            _points = new CameraVector[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                _points[i] = points[i];
            }

            if (looks != null)
            {
                _looks = new CameraVector[looks.Count];
                for (int i = 0; i < looks.Count; i++)
                {
                    _looks[i] = looks[i];
                }
            }

            Target = target;
            Pitch = pitch;
            LookAhead = lookAhead;
            Linear = linear;
            int segments = _points.Length - 1;
            _cumulative = new double[(segments * SamplesPerSegment) + 1];
            CameraVector previous = _points[0];
            for (int i = 1; i < _cumulative.Length; i++)
            {
                CameraVector here = Evaluate(_points, (double)i / SamplesPerSegment);
                _cumulative[i] = _cumulative[i - 1] + CameraVector.Distance(previous, here);
                previous = here;
            }
        }

        public int Count => _points.Length;
        public double Length => _cumulative[_cumulative.Length - 1];
        public CameraVector? Target { get; }
        public double? Pitch { get; }
        public double LookAhead { get; }
        public bool Linear { get; }
        public bool HasLooks => _looks != null;

        /// <summary>The spline parameter (segment index plus fraction) at distance <paramref name="s"/> along the path.</summary>
        public double ParameterAt(double s)
        {
            double length = Length;
            if (length <= 0 || s <= 0)
            {
                return 0;
            }

            if (s >= length)
            {
                return _points.Length - 1;
            }

            int lo = 0;
            int hi = _cumulative.Length - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_cumulative[mid] <= s)
                {
                    lo = mid;
                }
                else
                {
                    hi = mid;
                }
            }

            double span = _cumulative[hi] - _cumulative[lo];
            double f = span > 0 ? (s - _cumulative[lo]) / span : 0;
            return (lo + f) / SamplesPerSegment;
        }

        public CameraVector PointAtDistance(double s) => Evaluate(_points, ParameterAt(s));

        /// <summary>
        /// Points along the whole path at equal distances at most <paramref name="maximumStep"/> m
        /// apart, first and last waypoint included. A sample's parameter is the spline parameter
        /// (waypoint index plus fraction, 0 = the first waypoint of the path).
        /// </summary>
        public List<CameraMoveSample> Samples(double maximumStep)
        {
            double length = Length;
            int steps = CameraMoveSample.Steps(length, maximumStep);
            List<CameraMoveSample> samples = new List<CameraMoveSample>(steps + 1);
            for (int i = 0; i <= steps; i++)
            {
                double s = length * i / steps;
                double u = ParameterAt(s);
                samples.Add(new CameraMoveSample(u, s, Evaluate(_points, u)));
            }

            return samples;
        }

        /// <summary>The distance along the path at progress <paramref name="t"/>: constant speed, eased at both ends.</summary>
        public double DistanceAt(double t) => Length * CameraMath.Ease(t, Linear);

        public CameraVector PositionAt(double t) => PointAtDistance(DistanceAt(t));

        /// <summary>The point the camera looks at, at progress <paramref name="t"/>.</summary>
        public CameraVector LookAt(double t)
        {
            double s = DistanceAt(t);
            CameraVector position = PointAtDistance(s);
            if (Target.HasValue)
            {
                return Target.Value;
            }

            if (_looks != null)
            {
                return Evaluate(_looks, ParameterAt(s));
            }

            CameraVector ahead = PointAhead(s);
            if (Pitch.HasValue)
            {
                // Along the path's heading, tilted to a fixed pitch.
                double yaw = CameraMath.Bearing(position, ahead);
                if (CameraMath.HorizontalDistance(position, ahead) < 1e-4)
                {
                    yaw = CameraMath.Bearing(_points[0], _points[_points.Length - 1]);
                }

                return position + CameraMath.Direction(yaw, Pitch.Value);
            }

            return ahead;
        }

        private CameraVector PointAhead(double s)
        {
            double length = Length;
            double step = Math.Max(LookAhead, 0.05);
            double target = s + step;
            if (target <= length)
            {
                return PointAtDistance(target);
            }

            // Past the end: carry on along the end tangent.
            CameraVector end = _points[_points.Length - 1];
            CameraVector before = PointAtDistance(Math.Max(0, length - Math.Min(0.5, length)));
            CameraVector tangent = end - before;
            double tl = tangent.Length;
            if (tl < 1e-6)
            {
                return end + new CameraVector(0, 0, 1);
            }

            return end + (tangent * ((target - length) / tl));
        }

        /// <summary>A centripetal Catmull-Rom point at parameter <paramref name="u"/> (segment index plus fraction).</summary>
        public static CameraVector Evaluate(CameraVector[] points, double u)
        {
            int last = points.Length - 1;
            if (u <= 0)
            {
                return points[0];
            }

            if (u >= last)
            {
                return points[last];
            }

            int i = (int)Math.Floor(u);
            double f = u - i;
            CameraVector p1 = points[i];
            CameraVector p2 = points[i + 1];
            // Mirror the ends so the curve starts and ends heading at its neighbour.
            CameraVector p0 = i > 0 ? points[i - 1] : p1 + (p1 - p2);
            CameraVector p3 = i + 2 <= last ? points[i + 2] : p2 + (p2 - p1);
            double t0 = 0;
            double t1 = t0 + Knot(p0, p1);
            double t2 = t1 + Knot(p1, p2);
            double t3 = t2 + Knot(p2, p3);
            if (t2 - t1 < 1e-9)
            {
                return p1;
            }

            double t = t1 + ((t2 - t1) * f);
            CameraVector a1 = Lerp(p0, p1, t0, t1, t);
            CameraVector a2 = Lerp(p1, p2, t1, t2, t);
            CameraVector a3 = Lerp(p2, p3, t2, t3, t);
            CameraVector b1 = Lerp(a1, a2, t0, t2, t);
            CameraVector b2 = Lerp(a2, a3, t1, t3, t);
            return Lerp(b1, b2, t1, t2, t);
        }

        private static double Knot(CameraVector a, CameraVector b) => Math.Max(Math.Sqrt(CameraVector.Distance(a, b)), 1e-6);

        private static CameraVector Lerp(CameraVector a, CameraVector b, double ta, double tb, double t)
        {
            double span = tb - ta;
            if (span < 1e-12)
            {
                return a;
            }

            return (a * ((tb - t) / span)) + (b * ((t - ta) / span));
        }
    }

    /// <summary>A point on a move: its parameter (spline parameter or arc bearing offset), its distance along the move, and where it is.</summary>
    public readonly struct CameraMoveSample
    {
        public readonly double Parameter;
        public readonly double Along;
        public readonly CameraVector Position;

        public CameraMoveSample(double parameter, double along, CameraVector position)
        {
            Parameter = parameter;
            Along = along;
            Position = position;
        }

        /// <summary>How many equal steps of at most <paramref name="maximumStep"/> m cover <paramref name="length"/> m (at least one).</summary>
        public static int Steps(double length, double maximumStep)
        {
            if (!(maximumStep > 0)) throw new ArgumentOutOfRangeException(nameof(maximumStep));
            if (!(length > 0)) return 1;
            return Math.Max(1, (int)Math.Ceiling((length / maximumStep) - 1e-9));
        }

        /// <summary>Whether <paramref name="point"/> is within <paramref name="radius"/> m of any sample.</summary>
        public static bool NearAny(CameraVector point, IReadOnlyList<CameraMoveSample> samples, double radius)
        {
            double r2 = radius * radius;
            for (int i = 0; i < samples.Count; i++)
            {
                CameraVector d = point - samples[i].Position;
                if ((d.X * d.X) + (d.Y * d.Y) + (d.Z * d.Z) <= r2) return true;
            }

            return false;
        }
    }

    /// <summary>Whether the local player is carried along with a move, and where it is put back when the move ends.</summary>
    public enum CameraCarry
    {
        /// <summary>The player stays where it is; the camera flies alone.</summary>
        Off,
        /// <summary>Carried, then put back where it stood when the move started.</summary>
        Start,
        /// <summary>Carried, then put on the solid ground under the camera's final pose (the start when there is none).</summary>
        End
    }

    /// <summary>
    /// The options both camera moves take: carry the player along (zones load around it), a
    /// dry-run clearance check, cleaning creatures and drops around the move, and pausing spawns.
    /// </summary>
    public sealed class CameraMoveOptions
    {
        public const string Usage = "[--carry [start|end]] [--no-carry] [--check [clearance m]] [--clean [radius m]] [--nospawn] [--stay]";
        public const double DefaultClearance = 1.0;
        public const double MaximumClearance = 20;
        public const double DefaultCleanRadius = 40;
        public const double MaximumCleanRadius = 1000;
        private bool _carryGiven;
        private bool _noCarryGiven;

        public CameraMoveOptions(CameraCarry defaultCarry)
        {
            Carry = defaultCarry;
        }

        public enum Parsed
        {
            NotAnOption,
            Ok,
            Error
        }

        public CameraCarry Carry { get; private set; }
        /// <summary>
        /// Whether --carry or --no-carry was given. A carry that was only the default gives way: it
        /// is skipped when the player cannot be carried, and a move that replaces a carried move
        /// keeps carrying.
        /// </summary>
        public bool CarryGiven => _carryGiven || _noCarryGiven;
        /// <summary>When set, the command is a dry run: report where the move comes within this many metres of a solid collider.</summary>
        public double? Check { get; private set; }
        /// <summary>When set, hostile creatures, drops and corpses within this many metres of the move are removed at its start and every frame of it.</summary>
        public double? Clean { get; private set; }
        /// <summary>Pause the game's natural spawning (SpawnSystem.m_nospawn) for the move.</summary>
        public bool NoSpawn { get; private set; }
        /// <summary>Keep the last pose in free-fly when the move ends (for chaining shots); by default the move ends by returning the camera to the player.</summary>
        public bool Stay { get; private set; }

        /// <summary>
        /// Parses the move option at <paramref name="i"/>, advancing past its value if it has one.
        /// Answers NotAnOption for anything else, so each command keeps its own options.
        /// </summary>
        public Parsed TryParse(IReadOnlyList<string> args, ref int i, out string error)
        {
            error = string.Empty;
            string option = args[i].ToLowerInvariant();
            switch (option)
            {
                case "--carry":
                    if (_noCarryGiven)
                    {
                        error = "ERROR: --carry and --no-carry contradict each other";
                        return Parsed.Error;
                    }

                    _carryGiven = true;
                    Carry = CameraCarry.Start;
                    if (i + 1 < args.Count)
                    {
                        string landing = args[i + 1].ToLowerInvariant();
                        if (landing == "start" || landing == "end")
                        {
                            Carry = landing == "end" ? CameraCarry.End : CameraCarry.Start;
                            i++;
                        }
                    }

                    return Parsed.Ok;
                case "--no-carry":
                    if (_carryGiven)
                    {
                        error = "ERROR: --carry and --no-carry contradict each other";
                        return Parsed.Error;
                    }

                    _noCarryGiven = true;
                    Carry = CameraCarry.Off;
                    return Parsed.Ok;
                case "--stay":
                    Stay = true;
                    return Parsed.Ok;
                case "--nospawn":
                    NoSpawn = true;
                    return Parsed.Ok;
                case "--check":
                case "--clean":
                    bool check = option == "--check";
                    double value = check ? DefaultClearance : DefaultCleanRadius;
                    double maximum = check ? MaximumClearance : MaximumCleanRadius;
                    if (i + 1 < args.Count && CameraArcRequest.TryNumber(args[i + 1], out double given))
                    {
                        if (given <= 0 || given > maximum)
                        {
                            error = string.Format(CultureInfo.InvariantCulture, "ERROR: {0} must be above 0 and at most {1} m", option, maximum);
                            return Parsed.Error;
                        }

                        value = given;
                        i++;
                    }

                    if (check) Check = value;
                    else Clean = value;
                    return Parsed.Ok;
                default:
                    return Parsed.NotAnOption;
            }
        }

        /// <summary>The options as a status fragment; <paramref name="carried"/> is the carry the move really has, when it differs from the request.</summary>
        public string Describe(CameraCarry? carried = null)
        {
            CameraCarry effective = carried ?? Carry;
            string carry = effective == CameraCarry.Off ? "off" : (effective == CameraCarry.End ? "end" : "start");
            string clean = Clean.HasValue ? Clean.Value.ToString("F0", CultureInfo.InvariantCulture) + "m" : "off";
            return "carry=" + carry + " clean=" + clean + " nospawn=" + (NoSpawn ? "on" : "off") + " end=" + (Stay ? "stay" : "player");
        }
    }

    /// <summary>Why a camera move ended.</summary>
    public enum CameraMoveEnd
    {
        /// <summary>It ran to the end.</summary>
        Finished,
        /// <summary>cli_camera_stop.</summary>
        Stopped,
        /// <summary>It threw; the view is given back rather than left on a broken pose.</summary>
        Failed,
        /// <summary>A manual pose (cli_freefly_pose, cli_capture, cli_freefly_release) took the camera over: it decides the view.</summary>
        ManualPose,
        /// <summary>Free-fly was turned off under it: the view is already the player's.</summary>
        Interrupted
    }

    /// <summary>What the end of a move does with the view.</summary>
    public enum CameraViewEnd
    {
        /// <summary>Nothing: whoever ended the move decides the view.</summary>
        Leave,
        /// <summary>--stay: the camera keeps its last pose in free-fly.</summary>
        Stay,
        /// <summary>Free-fly off and the camera back on the player, as cli_freefly_release does.</summary>
        ReturnToPlayer
    }

    /// <summary>How a camera move leaves the view: the one owner of that choice.</summary>
    public static class CameraMoveEnding
    {
        /// <summary>
        /// A move that finished, was stopped or failed returns the camera to the player, unless
        /// --stay (the move's, or cli_camera_stop's) keeps the last pose. Whether free-fly was
        /// already on before the move does not matter: the view still goes back to the player.
        /// </summary>
        public static CameraViewEnd View(CameraMoveEnd end, bool stay)
        {
            switch (end)
            {
                case CameraMoveEnd.Finished:
                case CameraMoveEnd.Stopped:
                case CameraMoveEnd.Failed:
                    return stay ? CameraViewEnd.Stay : CameraViewEnd.ReturnToPlayer;
                default:
                    return CameraViewEnd.Leave;
            }
        }
    }

    /// <summary>A parsed <c>cli_camera_stop</c> request.</summary>
    public sealed class CameraStopRequest
    {
        public const string Usage = "Usage: cli_camera_stop [--stay]";
        /// <summary>Keep the camera where it stopped, in free-fly; by default the view returns to the player.</summary>
        public bool Stay { get; private set; }

        public static bool TryParse(IReadOnlyList<string> args, out CameraStopRequest request, out string error)
        {
            request = new CameraStopRequest();
            error = string.Empty;
            for (int i = 1; i < args.Count; i++)
            {
                if (!args[i].Equals("--stay", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"ERROR: unknown argument '{args[i]}'. {Usage}";
                    return false;
                }

                request.Stay = true;
            }

            return true;
        }
    }

    /// <summary>What the clearance probe found at one sample of a move.</summary>
    public enum ClearanceKind
    {
        Clear,
        /// <summary>A solid collider within the clearance (distance 0: the sample is inside it).</summary>
        Clip,
        /// <summary>The ground or its objects are not loaded here, so nothing could be checked.</summary>
        Unloaded
    }

    /// <summary>One probed sample: where it is on the move and what is nearest.</summary>
    public readonly struct ClearanceSample
    {
        public readonly CameraMoveSample Sample;
        public readonly ClearanceKind Kind;
        public readonly string Collider;
        public readonly double Distance;

        public ClearanceSample(CameraMoveSample sample, ClearanceKind kind, string collider = "", double distance = 0)
        {
            Sample = sample;
            Kind = kind;
            Collider = collider ?? string.Empty;
            Distance = distance;
        }
    }

    /// <summary>A run of samples of one kind, from its first to its last sample, with the closest one.</summary>
    public sealed class ClearanceStretch
    {
        public ClearanceStretch(ClearanceSample first)
        {
            Kind = first.Kind;
            First = first;
            Last = first;
            Closest = first;
            Samples = 1;
            if (first.Collider.Length > 0) Colliders.Add(first.Collider);
        }

        public ClearanceKind Kind { get; }
        public ClearanceSample First { get; }
        public ClearanceSample Last { get; private set; }
        public ClearanceSample Closest { get; private set; }
        public int Samples { get; private set; }
        public List<string> Colliders { get; } = new List<string>();

        internal void Add(ClearanceSample sample)
        {
            Last = sample;
            Samples++;
            if (sample.Distance < Closest.Distance) Closest = sample;
            if (sample.Collider.Length > 0 && !Colliders.Contains(sample.Collider)) Colliders.Add(sample.Collider);
        }
    }

    /// <summary>
    /// The pure half of the clearance check (<c>--check</c>): the merging of the samples that
    /// clip or are unloaded into stretches a script can put waypoints into, and their report lines.
    /// </summary>
    public static class CameraClearance
    {
        /// <summary>The longest step between probed samples, in metres.</summary>
        public const double SampleStep = 1.0;
        /// <summary>Runs of one kind at most this far apart along the move are one stretch: a lone clear sample between two clips is no gap worth a waypoint.</summary>
        public const double MergeGap = 2.0;

        /// <summary>
        /// Merges the samples that are not clear into stretches, each kind on its own: a sample joins
        /// the open stretch of its kind when it is at most <paramref name="mergeGap"/> m along the
        /// move from that stretch's last sample, else it starts a new one. Stretches come back in
        /// order of their first sample along the move.
        /// </summary>
        public static List<ClearanceStretch> Merge(IReadOnlyList<ClearanceSample> samples, double mergeGap = MergeGap)
        {
            List<ClearanceStretch> done = new List<ClearanceStretch>();
            ClearanceStretch? clip = null;
            ClearanceStretch? unloaded = null;
            for (int i = 0; i < samples.Count; i++)
            {
                ClearanceSample sample = samples[i];
                if (sample.Kind == ClearanceKind.Clip) clip = Extend(clip, sample, mergeGap, done);
                else if (sample.Kind == ClearanceKind.Unloaded) unloaded = Extend(unloaded, sample, mergeGap, done);
            }

            if (clip != null) done.Add(clip);
            if (unloaded != null) done.Add(unloaded);
            done.Sort((a, b) => a.First.Sample.Along.CompareTo(b.First.Sample.Along));
            return done;
        }

        private static ClearanceStretch Extend(ClearanceStretch? open, ClearanceSample sample, double mergeGap, List<ClearanceStretch> done)
        {
            if (open != null && sample.Sample.Along - open.Last.Sample.Along <= mergeGap + 1e-9)
            {
                open.Add(sample);
                return open;
            }

            if (open != null) done.Add(open);
            return new ClearanceStretch(sample);
        }

        /// <summary>One report line for a stretch; <paramref name="parameter"/> names the sample parameter (u for a path, deg for an arc).</summary>
        public static string Format(ClearanceStretch stretch, string parameter)
        {
            string head = string.Format(CultureInfo.InvariantCulture,
                "{0} {1}={2:F3}..{3:F3} s={4:F1}..{5:F1}m samples={6} from={7} to={8}",
                stretch.Kind == ClearanceKind.Clip ? "CLIP" : "UNLOADED", parameter,
                stretch.First.Sample.Parameter, stretch.Last.Sample.Parameter,
                stretch.First.Sample.Along, stretch.Last.Sample.Along, stretch.Samples,
                stretch.First.Sample.Position.Format(), stretch.Last.Sample.Position.Format());
            if (stretch.Kind != ClearanceKind.Clip) return head;
            ClearanceSample c = stretch.Closest;
            return head + string.Format(CultureInfo.InvariantCulture,
                " closest {0}={1:F3} s={2:F1}m at={3} collider={4} dist={5:F2} colliders={6}",
                parameter, c.Sample.Parameter, c.Sample.Along, c.Sample.Position.Format(), c.Collider, c.Distance,
                string.Join(",", stretch.Colliders));
        }
    }

    /// <summary>A parsed <c>cli_camera_clean</c> request.</summary>
    public sealed class CameraCleanRequest
    {
        public const string Usage = "Usage: cli_camera_clean [radius m] [x y z] [--all]";
        public const double DefaultRadius = 60;
        public double Radius { get; private set; } = DefaultRadius;
        public CameraVector? Centre { get; private set; }
        /// <summary>Also remove creatures that are not hostile to the player (deer and the like); tamed creatures and bosses always stay.</summary>
        public bool All { get; private set; }

        public static bool TryParse(IReadOnlyList<string> args, out CameraCleanRequest request, out string error)
        {
            request = new CameraCleanRequest();
            error = Usage;
            List<double> numbers = new List<double>();
            for (int i = 1; i < args.Count; i++)
            {
                if (args[i].Equals("--all", StringComparison.OrdinalIgnoreCase))
                {
                    request.All = true;
                    continue;
                }

                if (!CameraArcRequest.TryNumber(args[i], out double number))
                {
                    error = $"ERROR: unknown argument '{args[i]}'. {Usage}";
                    return false;
                }

                numbers.Add(number);
            }

            if (numbers.Count == 2 || numbers.Count > 4)
            {
                return false;
            }

            int at = 0;
            if (numbers.Count == 1 || numbers.Count == 4)
            {
                double radius = numbers[0];
                if (radius <= 0 || radius > CameraMoveOptions.MaximumCleanRadius)
                {
                    error = "ERROR: the radius must be above 0 and at most 1000 m";
                    return false;
                }

                request.Radius = radius;
                at = 1;
            }

            if (numbers.Count >= 3)
            {
                request.Centre = new CameraVector(numbers[at], numbers[at + 1], numbers[at + 2]);
            }

            return true;
        }
    }

    /// <summary>A parsed <c>cli_camera_arc</c> request.</summary>
    public sealed class CameraArcRequest
    {
        public const string Usage = "Usage: cli_camera_arc <degrees> <seconds> [x y z] [--radius m] [--height m] [--start bearing] [--distance m] [--swing] [--linear] " + CameraMoveOptions.Usage;
        public double Degrees { get; private set; }
        public double Seconds { get; private set; }
        public CameraVector? Focus { get; private set; }
        public double? Radius { get; private set; }
        public double? Height { get; private set; }
        public double? StartBearing { get; private set; }
        public double? Distance { get; private set; }
        public bool Swing { get; private set; }
        public bool Linear { get; private set; }
        /// <summary>Carry, check, clean and nospawn. An arc stays near its focus, so it carries the player only when asked (--carry).</summary>
        public CameraMoveOptions Options { get; } = new CameraMoveOptions(CameraCarry.Off);

        public static bool TryParse(IReadOnlyList<string> args, out CameraArcRequest request, out string error)
        {
            request = new CameraArcRequest();
            error = Usage;
            if (args.Count < 3 || !TryNumber(args[1], out double degrees) || !TryNumber(args[2], out double seconds))
            {
                return false;
            }

            if (Math.Abs(degrees) > 3600 || degrees == 0)
            {
                error = "ERROR: degrees must be non-zero and at most 3600 either way";
                return false;
            }

            if (!CameraMoveLimits.ValidSeconds(seconds))
            {
                error = CameraMoveLimits.SecondsError;
                return false;
            }

            request.Degrees = degrees;
            request.Seconds = seconds;
            int i = 3;
            if (args.Count >= 6 && !args[3].StartsWith("--", StringComparison.Ordinal))
            {
                if (!TryNumber(args[3], out double x) || !TryNumber(args[4], out double y) || !TryNumber(args[5], out double z))
                {
                    return false;
                }

                request.Focus = new CameraVector(x, y, z);
                i = 6;
            }

            for (; i < args.Count; i++)
            {
                string option = args[i].ToLowerInvariant();
                switch (option)
                {
                    case "--swing":
                        request.Swing = true;
                        continue;
                    case "--linear":
                        request.Linear = true;
                        continue;
                    case "--radius":
                    case "--height":
                    case "--start":
                    case "--distance":
                        if (i + 1 >= args.Count || !TryNumber(args[i + 1], out double value))
                        {
                            error = $"ERROR: {option} needs a number";
                            return false;
                        }

                        i++;
                        if ((option == "--radius" || option == "--distance") && (value <= 0 || value > 5000))
                        {
                            error = $"ERROR: {option} must be above 0 and at most 5000";
                            return false;
                        }

                        if (option == "--radius") request.Radius = value;
                        else if (option == "--height") request.Height = value;
                        else if (option == "--start") request.StartBearing = value;
                        else request.Distance = value;
                        continue;
                    default:
                        CameraMoveOptions.Parsed parsed = request.Options.TryParse(args, ref i, out string optionError);
                        if (parsed == CameraMoveOptions.Parsed.Ok) continue;
                        error = parsed == CameraMoveOptions.Parsed.Error ? optionError : $"ERROR: unknown argument '{args[i]}'. {Usage}";
                        return false;
                }
            }

            if (request.Focus.HasValue && request.Distance.HasValue)
            {
                error = "ERROR: --distance picks a focus ahead of the camera; it cannot be combined with x y z";
                return false;
            }

            return true;
        }

        internal static bool TryNumber(string text, out double value)
        {
            bool ok = CommandArguments.TryFiniteFloat(text, out float parsed);
            value = parsed;
            return ok;
        }
    }

    /// <summary>A parsed <c>cli_camera_path</c> request.</summary>
    public sealed class CameraPathRequest
    {
        public const string Usage = "Usage: cli_camera_path <seconds> <x,y,z[@lx,ly,lz]> <x,y,z[@lx,ly,lz]>... [--look x,y,z] [--pitch deg] [--ahead m] [--from-here] [--linear] " + CameraMoveOptions.Usage;
        public const int MaximumWaypoints = 64;
        public double Seconds { get; private set; }
        public List<CameraVector> Points { get; } = new List<CameraVector>();
        public List<CameraVector>? Looks { get; private set; }
        public CameraVector? Target { get; private set; }
        public double? Pitch { get; private set; }
        public double LookAhead { get; private set; } = 8;
        public bool FromHere { get; private set; }
        public bool Linear { get; private set; }
        /// <summary>
        /// Carry, check, clean and nospawn. A path carries the player by default: free-fly loads no
        /// zones, and any path that leaves the area loaded around the player flies into bare terrain.
        /// </summary>
        public CameraMoveOptions Options { get; } = new CameraMoveOptions(CameraCarry.Start);

        public static bool TryParse(IReadOnlyList<string> args, out CameraPathRequest request, out string error)
        {
            request = new CameraPathRequest();
            error = Usage;
            if (args.Count < 3 || !CameraArcRequest.TryNumber(args[1], out double seconds))
            {
                return false;
            }

            if (!CameraMoveLimits.ValidSeconds(seconds))
            {
                error = CameraMoveLimits.SecondsError;
                return false;
            }

            request.Seconds = seconds;
            int withLook = 0;
            bool aheadGiven = false;
            for (int i = 2; i < args.Count; i++)
            {
                string token = args[i];
                string option = token.ToLowerInvariant();
                if (option == "--from-here")
                {
                    request.FromHere = true;
                    continue;
                }

                if (option == "--linear")
                {
                    request.Linear = true;
                    continue;
                }

                if (option == "--look" || option == "--pitch" || option == "--ahead")
                {
                    if (i + 1 >= args.Count)
                    {
                        error = $"ERROR: {option} needs a value";
                        return false;
                    }

                    string value = args[++i];
                    if (option == "--look")
                    {
                        if (!TryTriple(value, out CameraVector target))
                        {
                            error = "ERROR: --look needs x,y,z";
                            return false;
                        }

                        request.Target = target;
                    }
                    else if (!CameraArcRequest.TryNumber(value, out double number))
                    {
                        error = $"ERROR: {option} needs a number";
                        return false;
                    }
                    else if (option == "--pitch")
                    {
                        if (number < -89 || number > 89)
                        {
                            error = "ERROR: --pitch must be from -89 to 89 degrees (positive looks down)";
                            return false;
                        }

                        request.Pitch = number;
                    }
                    else
                    {
                        if (number < 0 || number > 1000)
                        {
                            error = "ERROR: --ahead must be from 0 to 1000 m";
                            return false;
                        }

                        request.LookAhead = number;
                        aheadGiven = true;
                    }

                    continue;
                }

                if (token.StartsWith("--", StringComparison.Ordinal))
                {
                    CameraMoveOptions.Parsed parsed = request.Options.TryParse(args, ref i, out string optionError);
                    if (parsed == CameraMoveOptions.Parsed.Ok) continue;
                    error = parsed == CameraMoveOptions.Parsed.Error ? optionError : $"ERROR: unknown argument '{token}'. {Usage}";
                    return false;
                }

                string[] halves = token.Split('@');
                if (halves.Length > 2 || !TryTriple(halves[0], out CameraVector point))
                {
                    error = $"ERROR: waypoint '{token}' is not x,y,z or x,y,z@lx,ly,lz";
                    return false;
                }

                request.Points.Add(point);
                if (halves.Length == 2)
                {
                    if (!TryTriple(halves[1], out CameraVector look))
                    {
                        error = $"ERROR: waypoint '{token}' has a look point that is not lx,ly,lz";
                        return false;
                    }

                    request.Looks ??= new List<CameraVector>();
                    request.Looks.Add(look);
                    withLook++;
                }
            }

            int total = request.Points.Count + (request.FromHere ? 1 : 0);
            if (total < 2)
            {
                error = "ERROR: a path needs at least two waypoints (or one with --from-here)";
                return false;
            }

            if (request.Points.Count > MaximumWaypoints)
            {
                error = $"ERROR: at most {MaximumWaypoints} waypoints";
                return false;
            }

            if (withLook != 0 && withLook != request.Points.Count)
            {
                error = "ERROR: give a look point (@lx,ly,lz) on every waypoint or on none";
                return false;
            }

            if (withLook != 0 && request.FromHere)
            {
                error = "ERROR: --from-here has no look point of its own; use --look x,y,z with it, or list the start waypoint";
                return false;
            }

            int lookModes = (withLook != 0 ? 1 : 0) + (request.Target.HasValue ? 1 : 0) + (request.Pitch.HasValue ? 1 : 0);
            if (lookModes > 1 || (lookModes == 1 && aheadGiven))
            {
                error = "ERROR: choose one look: per-waypoint @look points, --look x,y,z, --pitch along the path, or --ahead m along the path";
                return false;
            }

            return true;
        }

        public static bool TryTriple(string text, out CameraVector value)
        {
            value = default;
            string[] parts = text.Split(',');
            if (parts.Length != 3 ||
                !CameraArcRequest.TryNumber(parts[0], out double x) ||
                !CameraArcRequest.TryNumber(parts[1], out double y) ||
                !CameraArcRequest.TryNumber(parts[2], out double z))
            {
                return false;
            }

            value = new CameraVector(x, y, z);
            return true;
        }
    }

    public static class CameraMoveLimits
    {
        public const double MaximumSeconds = 3600;
        public const string SecondsError = "ERROR: seconds must be above 0 and at most 3600";

        public static bool ValidSeconds(double seconds) => seconds > 0 && seconds <= MaximumSeconds;
    }
}
