using Xunit;

namespace valheimCLI.Tests;

/// <summary>
/// The math and parsing behind cli_camera_arc and cli_camera_path: a cinematic move is only
/// smooth if it starts and ends at rest, keeps its radius, always faces its focus, and walks a
/// path at constant speed rather than at the spline's uneven parameter speed.
/// </summary>
public class CameraMotionTests
{
    private const double Tolerance = 1e-6;

    [Fact]
    public void EaseStartsAndEndsAtRestAndIsSymmetric()
    {
        Assert.Equal(0, CameraMath.EaseInOut(0), 12);
        Assert.Equal(1, CameraMath.EaseInOut(1), 12);
        Assert.Equal(0.5, CameraMath.EaseInOut(0.5), 12);
        double h = 1e-5;
        Assert.True(CameraMath.EaseInOut(h) / h < 1e-3, "starts at rest");
        Assert.True((1 - CameraMath.EaseInOut(1 - h)) / h < 1e-3, "ends at rest");
        for (double t = 0; t <= 1; t += 0.05)
        {
            Assert.Equal(1 - CameraMath.EaseInOut(t), CameraMath.EaseInOut(1 - t), 9);
            Assert.True(CameraMath.EaseInOut(t + 0.01) >= CameraMath.EaseInOut(t), "never runs backwards");
        }

        Assert.Equal(0, CameraMath.EaseInOut(-1), 12);
        Assert.Equal(1, CameraMath.EaseInOut(2), 12);
        Assert.Equal(0.3, CameraMath.Ease(0.3, linear: true), 12);
    }

    [Theory]
    [InlineData(0, 0, 1, 0, 0)]     // north
    [InlineData(1, 0, 0, 90, 0)]    // east
    [InlineData(0, 0, -1, 180, 0)]  // south
    [InlineData(-1, 0, 0, -90, 0)]  // west
    [InlineData(0, -1, 1, 0, 45)]   // down 45 degrees: the game's pitch is positive downwards
    [InlineData(0, 1, 1, 0, -45)]
    public void YawPitchMatchesTheFreeFlyCamerasConvention(double x, double y, double z, double yaw, double pitch)
    {
        Assert.True(CameraMath.TryYawPitch(new CameraVector(x, y, z), out double gotYaw, out double gotPitch));
        Assert.Equal(yaw, gotYaw, 6);
        Assert.Equal(pitch, gotPitch, 6);
    }

    [Fact]
    public void DirectionInvertsYawPitch()
    {
        CameraVector dir = CameraMath.Direction(37, 22);
        Assert.Equal(1, dir.Length, 9);
        Assert.True(CameraMath.TryYawPitch(dir, out double yaw, out double pitch));
        Assert.Equal(37, yaw, 6);
        Assert.Equal(22, pitch, 6);
        Assert.False(CameraMath.TryYawPitch(new CameraVector(0, 0, 0), out _, out _));
    }

    [Fact]
    public void ArcKeepsRadiusAndHeightAndAlwaysFacesTheFocus()
    {
        CameraVector focus = new(5136, 66, -5408);
        CameraArc arc = new(focus, radius: 80, height: 30, startBearing: 180, sweep: 90, swing: false, linear: false);
        for (double t = 0; t <= 1.0001; t += 0.1)
        {
            CameraVector p = arc.PositionAt(t);
            Assert.Equal(80, CameraMath.HorizontalDistance(p, focus), 6);
            Assert.Equal(96, p.Y, 6);
            Assert.True(CameraMath.TryYawPitch(arc.LookAt(t) - p, out double yaw, out double pitch));
            // Looking at the focus: the yaw points back along the bearing, pitched down at atan(30/80).
            double expectedYaw = arc.BearingAt(t) + 180;
            Assert.Equal(0, Normalize(yaw - expectedYaw), 6);
            Assert.Equal(System.Math.Atan2(30, 80) * 180 / System.Math.PI, pitch, 6);
        }
    }

    [Fact]
    public void ArcStartsAtTheCameraAndPositiveDegreesMoveItToItsRight()
    {
        CameraVector focus = new(0, 0, 0);
        CameraVector camera = new(0, 10, -50); // south of the focus, looking north
        CameraArc arc = CameraArc.FromCamera(camera, focus, null, null, null, sweep: 90, swing: false, linear: false);
        Assert.Equal(50, arc.Radius, 9);
        Assert.Equal(10, arc.Height, 9);
        Assert.Equal(0, Normalize(arc.StartBearing - 180), 9);
        AssertNear(camera, arc.PositionAt(0));
        // Looking north, the camera's right is east: a quarter turn to the right ends east of the focus.
        AssertNear(new CameraVector(50, 10, 0), arc.PositionAt(1));
        Assert.True(arc.PositionAt(0.1).X > 0, "moves to the camera's right (east) from the start");
        CameraArc left = CameraArc.FromCamera(camera, focus, null, null, null, sweep: -90, swing: false, linear: false);
        AssertNear(new CameraVector(-50, 10, 0), left.PositionAt(1));
    }

    [Fact]
    public void ArcOverridesReplaceTheCamerasOwnValues()
    {
        CameraArc arc = CameraArc.FromCamera(new CameraVector(0, 10, -50), new CameraVector(0, 0, 0), radius: 20, height: 5, startBearing: 90, sweep: 10, swing: false, linear: true);
        AssertNear(new CameraVector(20, 5, 0), arc.PositionAt(0));
    }

    [Fact]
    public void ArcEasesSoTheSweepIsHalfDoneAtHalfTime()
    {
        CameraArc arc = new(new CameraVector(0, 0, 0), 10, 0, 0, 40, swing: false, linear: false);
        Assert.Equal(0, arc.OffsetAt(0), 9);
        Assert.Equal(20, arc.OffsetAt(0.5), 9);
        Assert.Equal(40, arc.OffsetAt(1), 9);
        Assert.True(arc.OffsetAt(0.1) < 4, "slower than linear at the start");
    }

    [Fact]
    public void SwingGoesLeftThenRightTwiceAsFarThenBackToTheCentre()
    {
        CameraArc arc = new(new CameraVector(0, 0, 0), 10, 0, 180, 20, swing: true, linear: false);
        Assert.Equal(0, arc.OffsetAt(0), 9);
        Assert.Equal(-20, arc.OffsetAt(0.25), 9);
        Assert.Equal(0, arc.OffsetAt(0.5), 9);
        Assert.Equal(20, arc.OffsetAt(0.75), 9);
        Assert.Equal(0, arc.OffsetAt(1), 9);
        double min = 0;
        double max = 0;
        for (double t = 0; t <= 1; t += 0.001)
        {
            double offset = arc.OffsetAt(t);
            min = System.Math.Min(min, offset);
            max = System.Math.Max(max, offset);
        }

        Assert.Equal(-20, min, 3);
        Assert.Equal(20, max, 3);
        // At rest at each turn: no jump in angle between neighbouring frames there.
        Assert.True(System.Math.Abs(arc.OffsetAt(0.2501) - arc.OffsetAt(0.2499)) < 1e-3);
    }

    [Fact]
    public void PathPassesThroughEveryWaypoint()
    {
        CameraVector[] points = { new(0, 50, 0), new(40, 55, 10), new(80, 60, -20), new(120, 50, 0) };
        CameraPath path = new(points, null, null, null, 8, linear: false);
        for (int i = 0; i < points.Length; i++)
        {
            AssertNear(points[i], CameraPath.Evaluate(points, i));
        }

        AssertNear(points[0], path.PositionAt(0));
        AssertNear(points[3], path.PositionAt(1));
    }

    [Fact]
    public void PathWalksItsLengthAtConstantSpeed()
    {
        // Uneven spacing: the spline parameter runs fast on the long leg and slow on the short one,
        // so an arc-length table is what keeps the camera's speed even.
        CameraVector[] points = { new(0, 0, 0), new(5, 0, 0), new(100, 0, 30), new(110, 0, 35) };
        CameraPath path = new(points, null, null, null, 8, linear: true);
        int steps = 200;
        double expected = path.Length / steps;
        CameraVector previous = path.PositionAt(0);
        for (int i = 1; i <= steps; i++)
        {
            CameraVector here = path.PositionAt((double)i / steps);
            double step = CameraVector.Distance(previous, here);
            Assert.True(System.Math.Abs(step - expected) < expected * 0.02, $"step {i}: {step} vs {expected}");
            previous = here;
        }
    }

    [Fact]
    public void ParameterAtDistanceInvertsTheArcLength()
    {
        CameraVector[] points = { new(0, 0, 0), new(30, 0, 0) };
        CameraPath path = new(points, null, null, null, 8, linear: true);
        Assert.Equal(30, path.Length, 6);
        Assert.Equal(0.5, path.ParameterAt(15), 3);
        AssertNear(new CameraVector(15, 0, 0), path.PointAtDistance(15), 1e-3);
        Assert.Equal(0, path.ParameterAt(-1), 9);
        Assert.Equal(1, path.ParameterAt(31), 9);
    }

    [Fact]
    public void PathEasesInAndOut()
    {
        CameraPath path = new(new[] { new CameraVector(0, 0, 0), new CameraVector(100, 0, 0) }, null, null, null, 8, linear: false);
        Assert.Equal(50, path.DistanceAt(0.5), 6);
        Assert.True(path.DistanceAt(0.01) < 0.1, "starts at rest");
        Assert.True(100 - path.DistanceAt(0.99) < 0.1, "ends at rest");
    }

    [Fact]
    public void PathLooksAheadAlongItselfAndPastTheEnd()
    {
        CameraPath path = new(new[] { new CameraVector(0, 10, 0), new CameraVector(0, 10, 100) }, null, null, null, 8, linear: true);
        Assert.True(CameraMath.TryYawPitch(path.LookAt(0.5) - path.PositionAt(0.5), out double yaw, out double pitch));
        Assert.Equal(0, yaw, 6);
        Assert.Equal(0, pitch, 6);
        Assert.True(CameraMath.TryYawPitch(path.LookAt(1) - path.PositionAt(1), out yaw, out pitch), "still has a heading at the end");
        Assert.Equal(0, yaw, 6);
    }

    [Fact]
    public void PathLookModes()
    {
        CameraVector[] points = { new(0, 40, 0), new(100, 40, 0) };
        CameraVector target = new(50, 0, 50);
        CameraPath fixedTarget = new(points, null, target, null, 8, linear: true);
        AssertNear(target, fixedTarget.LookAt(0.3));

        CameraPath pitched = new(points, null, null, 30, 8, linear: true);
        Assert.True(CameraMath.TryYawPitch(pitched.LookAt(0.5) - pitched.PositionAt(0.5), out double yaw, out double pitch));
        Assert.Equal(90, yaw, 6); // heading east
        Assert.Equal(30, pitch, 6);

        CameraVector[] looks = { new(0, 0, 50), new(100, 0, 50) };
        CameraPath perPoint = new(points, looks, null, null, 8, linear: true);
        AssertNear(looks[0], perPoint.LookAt(0));
        AssertNear(looks[1], perPoint.LookAt(1));
        AssertNear(new CameraVector(50, 0, 50), perPoint.LookAt(0.5), 1e-2);
    }

    [Fact]
    public void ArcRequestParsesFocusAndOptions()
    {
        Assert.True(CameraArcRequest.TryParse(Args("cli_camera_arc 40 20 5136 66 -5408 --radius 90 --height 25 --start 200 --swing --linear"), out CameraArcRequest r, out _));
        Assert.Equal(40, r.Degrees);
        Assert.Equal(20, r.Seconds);
        Assert.True(r.Focus.HasValue);
        AssertNear(new CameraVector(5136, 66, -5408), r.Focus!.Value);
        Assert.Equal(90, r.Radius);
        Assert.Equal(25, r.Height);
        Assert.Equal(200, r.StartBearing);
        Assert.True(r.Swing);
        Assert.True(r.Linear);

        Assert.True(CameraArcRequest.TryParse(Args("cli_camera_arc -20 12"), out r, out _));
        Assert.False(r.Focus.HasValue);
        Assert.False(r.Swing);
        Assert.Equal(-20, r.Degrees);

        Assert.True(CameraArcRequest.TryParse(Args("cli_camera_arc 20 12 --distance 60 --swing"), out r, out _));
        Assert.Equal(60, r.Distance);
    }

    [Theory]
    [InlineData("cli_camera_arc")]
    [InlineData("cli_camera_arc 20")]
    [InlineData("cli_camera_arc x 10")]
    [InlineData("cli_camera_arc 0 10")]
    [InlineData("cli_camera_arc 20 0")]
    [InlineData("cli_camera_arc 20 -5")]
    [InlineData("cli_camera_arc 20 4000")]
    [InlineData("cli_camera_arc 20 10 1 2")]
    [InlineData("cli_camera_arc 20 10 1 2 z")]
    [InlineData("cli_camera_arc 20 10 --radius")]
    [InlineData("cli_camera_arc 20 10 --radius 0")]
    [InlineData("cli_camera_arc 20 10 --bogus")]
    [InlineData("cli_camera_arc 20 10 1 2 3 --distance 50")]
    [InlineData("cli_camera_arc NaN 10")]
    public void ArcRequestRefusesBadInput(string line)
    {
        Assert.False(CameraArcRequest.TryParse(Args(line), out _, out string error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void PathRequestParsesWaypointsAndLooks()
    {
        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 30 5000,90,-5400 5100,95,-5420 5200,90,-5400 --pitch 25"), out CameraPathRequest r, out _));
        Assert.Equal(30, r.Seconds);
        Assert.Equal(3, r.Points.Count);
        AssertNear(new CameraVector(5100, 95, -5420), r.Points[1]);
        Assert.Equal(25, r.Pitch);
        Assert.Null(r.Looks);

        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 0,1,2@3,4,5 6,7,8@9,10,11"), out r, out _));
        Assert.NotNull(r.Looks);
        AssertNear(new CameraVector(9, 10, 11), r.Looks![1]);

        Assert.True(CameraPathRequest.TryParse(Args("cli_camera_path 10 6,7,8 --from-here --look 1,2,3 --ahead 0 --linear"), out r, out _));
        Assert.True(r.FromHere);
        Assert.True(r.Linear);
        Assert.Equal(0, r.LookAhead);
        AssertNear(new CameraVector(1, 2, 3), r.Target!.Value);
    }

    [Theory]
    [InlineData("cli_camera_path 10")]
    [InlineData("cli_camera_path 10 1,2,3")]
    [InlineData("cli_camera_path 0 1,2,3 4,5,6")]
    [InlineData("cli_camera_path 10 1,2 4,5,6")]
    [InlineData("cli_camera_path 10 1,2,3,4 4,5,6")]
    [InlineData("cli_camera_path 10 1,2,3@1,1,1 4,5,6")]
    [InlineData("cli_camera_path 10 1,2,3@1,1 4,5,6@1,1,1")]
    [InlineData("cli_camera_path 10 1,2,3@1,1,1 4,5,6@1,1,1 --look 1,1,1")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --look 1,1,1 --pitch 20")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --pitch 95")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --ahead -1")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --look")]
    [InlineData("cli_camera_path 10 1,2,3 4,5,6 --fast")]
    [InlineData("cli_camera_path 10 4,5,6@1,1,1 --from-here")]
    public void PathRequestRefusesBadInput(string line)
    {
        Assert.False(CameraPathRequest.TryParse(Args(line), out _, out string error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    private static string[] Args(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static double Normalize(double degrees)
    {
        double d = degrees % 360;
        if (d > 180) d -= 360;
        if (d < -180) d += 360;
        return d;
    }

    private static void AssertNear(CameraVector expected, CameraVector actual, double tolerance = Tolerance)
    {
        Assert.True(CameraVector.Distance(expected, actual) <= tolerance, $"expected {expected.Format()} got {actual.Format()}");
    }
}
