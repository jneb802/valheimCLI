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

        public CameraVector PositionAt(double t)
        {
            double bearing = BearingAt(t) * (Math.PI / 180.0);
            return new CameraVector(Focus.X + (Radius * Math.Sin(bearing)), Focus.Y + Height, Focus.Z + (Radius * Math.Cos(bearing)));
        }

        /// <summary>The camera always looks at the focus.</summary>
        public CameraVector LookAt(double t) => Focus;

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

    /// <summary>A parsed <c>cli_camera_arc</c> request.</summary>
    public sealed class CameraArcRequest
    {
        public const string Usage = "Usage: cli_camera_arc <degrees> <seconds> [x y z] [--radius m] [--height m] [--start bearing] [--distance m] [--swing] [--linear]";
        public double Degrees { get; private set; }
        public double Seconds { get; private set; }
        public CameraVector? Focus { get; private set; }
        public double? Radius { get; private set; }
        public double? Height { get; private set; }
        public double? StartBearing { get; private set; }
        public double? Distance { get; private set; }
        public bool Swing { get; private set; }
        public bool Linear { get; private set; }

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
                        error = $"ERROR: unknown argument '{args[i]}'. {Usage}";
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
        public const string Usage = "Usage: cli_camera_path <seconds> <x,y,z[@lx,ly,lz]> <x,y,z[@lx,ly,lz]>... [--look x,y,z] [--pitch deg] [--ahead m] [--from-here] [--linear]";
        public const int MaximumWaypoints = 64;
        public double Seconds { get; private set; }
        public List<CameraVector> Points { get; } = new List<CameraVector>();
        public List<CameraVector>? Looks { get; private set; }
        public CameraVector? Target { get; private set; }
        public double? Pitch { get; private set; }
        public double LookAhead { get; private set; } = 8;
        public bool FromHere { get; private set; }
        public bool Linear { get; private set; }

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
                    }

                    continue;
                }

                if (token.StartsWith("--", StringComparison.Ordinal))
                {
                    error = $"ERROR: unknown argument '{token}'. {Usage}";
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
            if (lookModes > 1)
            {
                error = "ERROR: choose one look: per-waypoint @look points, --look x,y,z, or --pitch along the path";
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
